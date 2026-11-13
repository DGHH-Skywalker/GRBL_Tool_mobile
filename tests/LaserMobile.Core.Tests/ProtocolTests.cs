using System.Text;
using LaserMobile.Application;
using LaserMobile.Domain;
using LaserMobile.Grbl;
using LaserMobile.Persistence;
using Xunit;
namespace LaserMobile.Core.Tests;

public sealed class ProtocolTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static JobSender Sender() => new(DryRunJob.Parse(SimulationSession.Sample));
    [Fact]
    public void FramingHandlesFragmentsAndCoalescing()
    {
        var f = new ResponseFramer(); Assert.Empty(f.Feed("o"u8));
        var responses = f.Feed("k\r\n<Hold:0|MPos:1,2,3>\r\n"u8);
        Assert.IsType<Acknowledgement>(responses[0]); Assert.Equal("Hold:0", Assert.IsType<Status>(responses[1]).State);
    }
    [Fact]
    public void FramingRejectsOverflowAndRealtimeBytes()
    {
        Assert.Throws<FormatException>(() => new ResponseFramer(3).Feed("abcd"u8));
        Assert.Throws<FormatException>(() => new ResponseFramer().Feed([24]));
    }
    [Theory]
    [InlineData("M3 S100")]
    [InlineData("!")]
    [InlineData("$H")]
    [InlineData("G1 X50 Y0 F100")]
    [InlineData("G91")]
    public void DryRunRejectsUnsupportedCommands(string line) => Assert.Throws<FormatException>(() => DryRunJob.Parse($"G21\nG90\nM5\n{line}\nM5"));
    [Fact]
    public void SnapshotDoesNotCompleteAtFinalJobAcknowledgement()
    {
        var sender = Sender(); sender.Start(Now, true);
        for (var i = 0; i < 6; i++) { sender.DrainWrites(); sender.Receive(new Acknowledgement(), Now); }
        Assert.Equal(ExecutionState.Draining, sender.State);
        Assert.Equal(6, sender.Snapshot.AcknowledgedLines);
        sender.Receive(new Status("Idle", new Dictionary<string, string>()), Now);
        Assert.Equal(ExecutionState.Draining, sender.State);
        Assert.Contains("G4 P0\n", sender.DrainWrites());
        sender.Receive(new Acknowledgement(), Now);
        sender.Receive(new Status("Idle", new Dictionary<string, string>()), Now);
        Assert.Equal(ExecutionState.Draining, sender.State);
        sender.Tick(Now); Assert.Contains("?", sender.DrainWrites());
        sender.Receive(new Status("Idle", new Dictionary<string, string>()), Now);
        Assert.Equal(ExecutionState.Completed, sender.State);
    }
    [Fact]
    public void SimulationCompletesWithSeparateExecutionAndAck()
    {
        var s = new SimulationSession(); s.Start();
        for (var i = 0; i < 200 && !s.Sender.IsTerminal; i++) s.Step();
        Assert.Equal(ExecutionState.Completed, s.Sender.State); Assert.Equal(6, s.Controller.ExecutedLines);
    }
    [Fact]
    public void LostAcknowledgementDoesNotReplayMovement()
    {
        var sender = Sender(); sender.Start(Now, true); Assert.Single(sender.DrainWrites());
        sender.Receive(new Status("Idle", new Dictionary<string, string>()), Now + TimeSpan.FromSeconds(10));
        sender.Tick(Now + TimeSpan.FromSeconds(10));
        Assert.Equal(ExecutionState.Interrupted, sender.State); Assert.Equal(["!"], sender.DrainWrites());
    }
    [Theory]
    [InlineData("error:2")]
    [InlineData("ALARM:1")]
    [InlineData("Grbl 1.1h")]
    public void DeviceFaultStopsNewLines(string text)
    {
        var sender = Sender(); sender.Start(Now, true); sender.DrainWrites(); sender.Receive(ResponseParser.Parse(text), Now);
        Assert.Equal(ExecutionState.Interrupted, sender.State);
        Assert.Equal(text.StartsWith("Grbl", StringComparison.Ordinal) ? Array.Empty<string>() : ["!"], sender.DrainWrites());
    }
    [Fact]
    public void PauseRequiresControllerHold()
    {
        var sender = Sender(); sender.Start(Now, true); sender.DrainWrites(); sender.Pause();
        Assert.Equal(ExecutionState.Pausing, sender.State); Assert.Throws<InvalidOperationException>(() => sender.Resume(Now));
        sender.Receive(new Status("Hold:1", new Dictionary<string, string>()), Now); Assert.Equal(ExecutionState.Pausing, sender.State);
        sender.Receive(new Status("Hold:0", new Dictionary<string, string>()), Now); Assert.Equal(ExecutionState.Paused, sender.State);
        sender.Resume(Now); Assert.Contains("~", sender.DrainWrites());
    }
    [Fact]
    public void DuplicateStartAndUnverifiedBarrierAreRejected()
    {
        var sender = Sender(); Assert.Throws<InvalidOperationException>(() => sender.Start(Now, false)); sender.Start(Now, true);
        Assert.Throws<InvalidOperationException>(() => sender.Start(Now, true));
    }
    [Fact]
    public void CancellationDoesNotClaimQueueClearance()
    {
        var sender = Sender(); sender.Start(Now, true); sender.Cancel(); Assert.Equal(ExecutionState.Interrupted, sender.State);
    }
    [Fact]
    public void UnknownXingguangHasNoInventedTravel()
    {
        var x = new LaserMobile.Extensions.Xingguang.XingguangExtension(); Assert.Null(x.Profiles[0].TravelXMillimeters);
        Assert.False(x.Matches(new DeviceIdentity(0x1a86, 0x7523, "Grbl 1.1h")));
    }
    [Fact]
    public void RestartPersistsInterruptedAndDoesNotResume()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sqlite");
        try
        {
            var store = new ExecutionStore(path); var sender = Sender(); sender.Start(Now, true); store.Save(sender.Snapshot);
            Assert.Equal(1, new ExecutionStore(path).RecoverInterrupted()); Assert.Equal(ExecutionState.Interrupted, store.ReadState(sender.ExecutionId));
            Assert.Equal(0, store.RecoverInterrupted());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
