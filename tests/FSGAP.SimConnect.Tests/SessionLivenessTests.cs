using FSGAP.SimConnect.Native;
using SimConnect.NET;

namespace FSGAP.SimConnect.Tests;

/// <summary>
/// The end of a native connection, as SimConnect.NET 0.2.2 fails to report it (live 2026-10-13: MSFS 2024 closed,
/// the host stayed Connected). See <see cref="SessionLiveness"/>.
/// </summary>
public class SessionLivenessTests
{
    [Fact]
    public void Quit_message_ends_the_connection_once()
    {
        var liveness = new SessionLiveness();
        var raised = 0;
        liveness.Lost += () => raised++;

        liveness.OnMessage(SimConnectRecvId.Quit);
        liveness.OnMessage(SimConnectRecvId.Quit);
        liveness.OnLibraryStatus(false);

        Assert.True(liveness.IsLost);
        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData(SimConnectRecvId.Null)]
    [InlineData(SimConnectRecvId.Exception)]
    [InlineData(SimConnectRecvId.Open)]
    [InlineData(SimConnectRecvId.Event)]
    [InlineData(SimConnectRecvId.SimobjectData)]
    [InlineData(SimConnectRecvId.ClientData)]
    public void Other_messages_do_not_end_the_connection(SimConnectRecvId messageId)
    {
        var liveness = new SessionLiveness();
        var raised = 0;
        liveness.Lost += () => raised++;

        liveness.OnMessage(messageId);

        Assert.False(liveness.IsLost);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Library_disconnection_still_ends_the_connection_once()
    {
        var liveness = new SessionLiveness();
        var raised = 0;
        liveness.Lost += () => raised++;

        liveness.OnLibraryStatus(true);
        Assert.False(liveness.IsLost);

        liveness.OnLibraryStatus(false);
        liveness.OnMessage(SimConnectRecvId.Quit);

        Assert.True(liveness.IsLost);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void End_evidence_records_the_first_signal_and_counts_quit_messages()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero));
        var liveness = new SessionLiveness(clock);
        Assert.Null(liveness.End);
        Assert.Equal(0, liveness.QuitMessages);

        liveness.OnMessage(SimConnectRecvId.Quit);
        clock.Advance(TimeSpan.FromSeconds(1));
        liveness.OnLibraryStatus(false);
        liveness.OnMessage(SimConnectRecvId.Quit);

        Assert.Equal((SessionEndReason.Quit, new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero)), liveness.End);
        Assert.Equal(2, liveness.QuitMessages);
    }

    [Fact]
    public void Native_session_observes_quit_on_the_raw_message_channel()
    {
        // Regression guard: the adapter must listen to RawMessageReceived for QUIT, and derive IsConnected from it,
        // because the library's own IsConnected never turns false when the simulator closes.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "FSGAP.SimConnect", "Native", "SimConnectNetSession.cs"));
        Assert.Contains("_client.RawMessageReceived += OnRawMessage;", source);
        Assert.Contains("_liveness.OnMessage(e.MessageId)", source);
        Assert.Contains("_client.IsConnected && !_liveness.IsLost", source);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FSGAP.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
