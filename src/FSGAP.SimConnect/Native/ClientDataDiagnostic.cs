using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SimConnect.NET;
using SimConnect.NET.Events;

namespace FSGAP.SimConnect.Native;

// Read-only ClientData transport. No Create/SetClientData or aircraft control operation.
internal static class ClientDataPacket
{
    internal static byte[] Parse(ReadOnlySpan<byte> packet, uint request, uint definition, int expectedLength)
    {
        if (packet.Length != 40 + expectedLength || BinaryPrimitives.ReadUInt32LittleEndian(packet) != packet.Length)
            throw new FormatException($"ClientData packet length {packet.Length}; expected {40 + expectedLength}.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]) != request ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet[20..]) != definition ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet[24..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(packet[36..]) != 1)
            throw new FormatException("Unexpected ClientData request, definition, flags or datum count.");
        return packet[40..].ToArray();
    }
}

internal sealed partial class SimConnectNetSession
{
    private static int _nextClientDataDiagnostic = 0x504D4400;
    private readonly SemaphoreSlim _clientDataMapGate = new(1, 1);
    private readonly Dictionary<string, uint> _clientDataAreas = new(StringComparer.OrdinalIgnoreCase);

    public async Task ObserveClientDataAsync(string name, int length, Action<byte[]> observed, CancellationToken cancellationToken)
    {
        if (length is < 1 or > 8192) throw new ArgumentOutOfRangeException(nameof(length));
        var id = unchecked((uint)Interlocked.Increment(ref _nextClientDataDiagnostic));
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRaw(object? sender, RawSimConnectMessageEventArgs e)
        {
            if (e.MessageId != SimConnectRecvId.ClientData || e.DataSize < 16 ||
                unchecked((uint)Marshal.ReadInt32(e.DataPointer, 12)) != id) return;
            try
            {
                if (e.DataSize > 8232) throw new FormatException("Oversized ClientData packet.");
                var packet = new byte[e.DataSize];
                Marshal.Copy(e.DataPointer, packet, 0, packet.Length);
                observed(ClientDataPacket.Parse(packet, id, id, length));
            }
            catch (Exception ex) { fault.TrySetException(ex); }
        }
        static void Check(int hr)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
        _client.RawMessageReceived += OnRaw;
        var subscribed = false;
        var defined = false;
        uint area = 0;
        try
        {
            // Microsoft specifies mapping once per named area on each connection. Keep that mapping across
            // aircraft/provider subscriptions, while request/definition IDs remain unique to each subscription.
            await _clientDataMapGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_clientDataAreas.TryGetValue(name, out area))
                {
                    area = unchecked((uint)Interlocked.Increment(ref _nextClientDataDiagnostic));
                    Check(await FacilityInterop.InvokeNativeAsync(_client, h => Map(h, name, area), cancellationToken).ConfigureAwait(false));
                    _clientDataAreas.Add(name, area);
                }
            }
            finally { _clientDataMapGate.Release(); }
            Check(await FacilityInterop.InvokeNativeAsync(_client, h => Define(h, id, 0, (uint)length, 0, 0), cancellationToken).ConfigureAwait(false));
            defined = true;
            // ON_SET=3, flags=0: preserve repeated producer notifications, not just changed bytes.
            Check(await FacilityInterop.InvokeNativeAsync(_client, h => Request(h, area, id, id, 3, 0, 0, 0, 0), cancellationToken).ConfigureAwait(false));
            subscribed = true;
            await fault.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _client.RawMessageReceived -= OnRaw;
            if (defined && IsConnected)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    if (subscribed) Check(await FacilityInterop.InvokeNativeAsync(_client, h => Request(h, area, id, id, 0, 0, 0, 0, 0), cleanup.Token).ConfigureAwait(false));
                    Check(await FacilityInterop.InvokeNativeAsync(_client, h => Clear(h, id), cleanup.Token).ConfigureAwait(false));
                }
                catch (Exception) when (cleanup.IsCancellationRequested || !IsConnected) { }
            }
        }
    }

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_MapClientDataNameToID", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern int Map(IntPtr h, [MarshalAs(UnmanagedType.LPStr)] string name, uint id);
    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_AddToClientDataDefinition")]
    private static extern int Define(IntPtr h, uint id, uint offset, uint size, float epsilon, uint datum);
    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_RequestClientData")]
    private static extern int Request(IntPtr h, uint area, uint request, uint definition, uint period, uint flags, uint origin, uint interval, uint limit);
    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_ClearClientDataDefinition")]
    private static extern int Clear(IntPtr h, uint id);
}
