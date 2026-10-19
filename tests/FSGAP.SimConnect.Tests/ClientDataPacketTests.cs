using System.Buffers.Binary;
using FSGAP.SimConnect.Native;
namespace FSGAP.SimConnect.Tests;
public sealed class ClientDataPacketTests
{
    [Fact]
    public void ExtractsExactPayload() => Assert.Equal(916, ClientDataPacket.Parse(Valid(), 12, 13, 916).Length);
    [Theory]
    [InlineData(0)] [InlineData(12)] [InlineData(20)] [InlineData(24)] [InlineData(36)]
    public void RejectsIncompatibleHeader(int offset)
    {
        var packet = Valid();
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), 99);
        Assert.Throws<FormatException>(() => ClientDataPacket.Parse(packet, 12, 13, 916));
    }
    [Fact]
    public void RejectsTruncation() => Assert.Throws<FormatException>(() => ClientDataPacket.Parse(Valid()[..^1], 12, 13, 916));
    private static byte[] Valid()
    {
        var packet = new byte[956];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, 956);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(20), 13);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(36), 1);
        return packet;
    }
}
