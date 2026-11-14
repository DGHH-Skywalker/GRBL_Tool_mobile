using LaserMobile.Application;
using LaserMobile.Domain;
using LaserMobile.Grbl;
using LaserMobile.Persistence;
using Xunit;
namespace LaserMobile.Core.Tests;

public sealed class ReliabilityTests
{
    private static readonly JobLimits Limits = new(40, 40, 1200, 1000);
    [Fact] public void RelativePreflightUsesActualWorkPosition()
    {
        const string file = "G21 G91\nM5\nG1 X20 F300\nM5";
        Assert.Equal(20, GcodeProgram.Prepare(file, Limits).MaxX);
        Assert.Throws<FormatException>(() => GcodeProgram.Prepare(file, Limits, new(30, 0)));
        var shifted = GcodeProgram.Prepare(file, Limits, new(10, 5));
        Assert.Equal(30, shifted.MaxX); Assert.Equal(10, shifted.Segments[0].X1); Assert.Equal(5, shifted.MinY);
    }
    [Fact] public void FirstMotionRequiresExplicitModalCommand()
    {
        Assert.Throws<FormatException>(() => GcodeProgram.Prepare("G21 G90\nM5\nX5 F300\nM5", Limits));
    }
    [Fact] public void OlderConversionCannotOverwriteNewParameters()
    {
        var draft = new PreparationRevision(); var ticket = draft.Invalidate();
        draft.Invalidate(); Assert.False(draft.Commit(ticket)); Assert.False(draft.IsCurrent);
        Assert.True(draft.Commit(draft.Revision)); Assert.True(draft.IsCurrent);
        draft.Invalidate(); Assert.False(draft.IsCurrent);
    }
    [Fact] public void CharacterWindowBoundsOutstandingBytesAndDelaysBarrier()
    {
        var analysis = GcodeProgram.Prepare(DemoTransport.Sample, Limits);
        var sender = new JobSender(analysis, receiveWindowBytes: 80);
        var now = DateTimeOffset.UnixEpoch; sender.Start(now, true);
        var first = sender.DrainWrites(); Assert.True(first.Count > 1); Assert.True(first.Sum(x => x.Length) < 80);
        Assert.DoesNotContain("G4 P0\n", first);
        int outstanding = first.Sum(x => x.Length); var pending = new Queue<int>(first.Select(x => x.Length));
        for (int i = 0; i < analysis.Plan.Lines.Count; i++)
        {
            outstanding -= pending.Dequeue(); sender.Receive(new Acknowledgement(), now);
            foreach (var line in sender.DrainWrites())
            {
                if (line == "G4 P0\n") { Assert.Equal(analysis.Plan.Lines.Count, sender.Snapshot.AcknowledgedLines); continue; }
                outstanding += line.Length; pending.Enqueue(line.Length); Assert.True(outstanding < 80);
            }
        }
        Assert.Equal(ExecutionState.Draining, sender.State);
        sender.Receive(new Acknowledgement(), now); Assert.Equal(ExecutionState.Draining, sender.State);
        sender.Tick(now); sender.DrainWrites(); sender.Receive(new Status("Idle", new Dictionary<string, string>()), now);
        Assert.Equal(ExecutionState.Completed, sender.State);
    }
    [Fact] public async Task DemoUsesProductionSessionPauseCompletionAndHistory()
    {
        await WithDemo(async (session, transport, store, library) =>
        {
            await Ready(session);
            var job = library.Commit("Demo square", GcodeProgram.Prepare(DemoTransport.Sample, Limits).Plan, Limits, "S100 F600");
            await session.Start(job, Limits, Guid.NewGuid(), session.Snapshot.ControllerEpoch, true);
            await session.Pause(session.Snapshot.Execution!.ExecutionId);
            await Wait(() => session.Snapshot.Execution?.State == ExecutionState.Paused);
            await session.Resume(session.Snapshot.Execution!.ExecutionId, true);
            await Wait(() => session.Snapshot.Execution?.State == ExecutionState.Completed);
            var history = Assert.Single(store.History()); Assert.Equal("Demo square", history.JobName);
            Assert.Equal(job.Hash, history.Hash); Assert.Equal(history.TotalLines, history.AcknowledgedLines);
            Assert.StartsWith("S100 F600", history.Parameters);
        });
    }
    [Fact] public async Task ControllerResetRequiresNewConfigurationAndManualArm()
    {
        await WithDemo(async (session, transport, store, library) =>
        {
            await Ready(session);
            var job = library.Commit("Reset", GcodeProgram.Prepare(DemoTransport.Sample, Limits).Plan);
            await session.Start(job, Limits, Guid.NewGuid(), session.Snapshot.ControllerEpoch, true);
            transport.InjectReset(); await Wait(() => session.Snapshot.Execution?.State == ExecutionState.Interrupted && session.Snapshot.Machine == "Idle");
            Assert.False(session.Snapshot.IsArmed);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Arm(true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Start(job, Limits, Guid.NewGuid(), session.Snapshot.ControllerEpoch, true));
            await session.ReadConfiguration(); await SessionTestReadiness.ConfiguredIdle(session); await session.Arm(true); Assert.True(session.Snapshot.IsArmed);
        });
    }
    [Fact] public async Task SessionRejectsRelativeOvershootAndChangedConfirmationPosition()
    {
        await WithDemo(async (session, transport, store, library) =>
        {
            await Ready(session);
            for (int i = 1; i <= 3; i++)
            {
                await session.Jog(1, 0, 10, 300); var expected = i * 10;
                await Wait(() => session.Snapshot.WorkX == expected && session.Snapshot.Machine == "Idle");
            }
            await session.Arm(true);
            var job = library.Commit("Relative", GcodeProgram.Prepare("G21 G91\nM5\nG1 X20 F300\nM5", Limits).Plan);
            await Assert.ThrowsAsync<FormatException>(() => session.Start(job, Limits, Guid.NewGuid(), session.Snapshot.ControllerEpoch, true));
            Assert.Null(session.Snapshot.Execution);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Start(job, Limits, Guid.NewGuid(), session.Snapshot.ControllerEpoch, true, new(0, 0)));
            Assert.Empty(store.History());
        });
    }
    [Fact] public async Task AlarmRequiresResetReadAndManualArm()
    {
        await WithDemo(async (session, transport, store, library) =>
        {
            await Ready(session); var job = library.Commit("Alarm", GcodeProgram.Prepare(DemoTransport.Sample, Limits).Plan);
            await session.Start(job, Limits, Guid.NewGuid(), session.Snapshot.ControllerEpoch, true);
            transport.InjectAlarm(); await Wait(() => session.Snapshot.Execution?.State == ExecutionState.Interrupted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadConfiguration());
            await session.ResetController(true); await Wait(() => session.Snapshot.Machine == "Idle");
            await session.ReadConfiguration(); await SessionTestReadiness.ConfiguredIdle(session); await session.Arm(true);
            Assert.True(session.Snapshot.IsArmed); Assert.Equal(ExecutionState.Interrupted, store.History()[0].State);
        });
    }
    [Fact] public async Task DisconnectPersistsInterruption()
    {
        await WithDemo(async (session, transport, store, library) =>
        {
            await Ready(session);
            var job = library.Commit("Disconnect", GcodeProgram.Prepare(DemoTransport.Sample, Limits).Plan);
            await session.Start(job, Limits, Guid.NewGuid(), session.Snapshot.ControllerEpoch, true);
            transport.InjectDisconnect(); await Wait(() => session.Snapshot.Connection == "Lost");
            Assert.Equal(ExecutionState.Interrupted, store.History()[0].State); Assert.False(session.Snapshot.IsArmed);
        });
    }
    private static async Task Ready(DeviceSession session)
    {
        await Wait(() => session.Snapshot.Machine == "Idle"); await session.ReadConfiguration();
        await SessionTestReadiness.ConfiguredIdle(session); await session.Arm(true);
    }
    private static async Task WithDemo(Func<DeviceSession, DemoTransport, ExecutionStore, JobLibrary, Task> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            var transport = new DemoTransport(); var store = new ExecutionStore(Path.Combine(dir, "runs.sqlite"));
            await using var session = new DeviceSession(transport, store); session.Run();
            await body(session, transport, store, new JobLibrary(Path.Combine(dir, "jobs")));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
    }
    private static async Task Wait(Func<bool> condition)
    {
        for (int i = 0; i < 400; i++) { if (condition()) return; await Task.Delay(20); }
        Assert.True(condition());
    }
}
