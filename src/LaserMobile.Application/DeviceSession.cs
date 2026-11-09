using System.Text;
using System.Globalization;
using System.Diagnostics;
using LaserMobile.Domain;
using LaserMobile.Grbl;
using LaserMobile.Persistence;
namespace LaserMobile.Application;

/// <summary>One gate owns parser, writer, controller epoch and execution transitions.</summary>
public sealed class DeviceSession : IAsyncDisposable
{
    private readonly IByteTransport transport;
    private readonly ExecutionStore store;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly ResponseFramer framer = new();
    private readonly Guid id = Guid.NewGuid();
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly Queue<string> management = new();
    private readonly Queue<string> logs = new();
    private Task reader = Task.CompletedTask, ticker = Task.CompletedTask;
    private JobSender? sender;
    private string machine = "Unknown", firmware = "", position = "未知", connection = "Connected";
    private string? error, pending;
    private DateTimeOffset lastStatus = DateTimeOffset.MinValue, commandDeadline, cancelDeadline;
    private double? maxPower;
    private double[]? mpos, wco, wpos;
    private bool laserMode, disposed, running, homing, standardFrame;
    private long epoch, sequence;
    private string? savedExecution;
    private SessionSnapshot snapshot;
    private DateTimeOffset Now => DateTimeOffset.UnixEpoch + Stopwatch.GetElapsedTime(started);
    public SessionSnapshot Snapshot => Volatile.Read(ref snapshot);
    public string[] Log { get { lock (logs) return logs.ToArray(); } }
    public DeviceSession(IByteTransport transport, ExecutionStore store)
    {
        this.transport = transport; this.store = store;
        snapshot = new(id, 0, connection, firmware, machine, position, false, null, null, null, 0);
    }
    public void Run()
    {
        if (running) throw new InvalidOperationException("会话已启动");
        running = true; reader = Task.Run(ReadLoop); ticker = Task.Run(TickLoop);
    }
    private void AddLog(string s) { lock (logs) { logs.Enqueue(s); while (logs.Count > 300) logs.Dequeue(); } }
    private async Task ReadLoop()
    {
        try
        {
            await foreach (var bytes in transport.ReadAsync(stop.Token))
            {
                await gate.WaitAsync(stop.Token);
                try
                {
                    foreach (var response in framer.Feed(bytes.Span))
                    {
                        AddLog("< " + response);
                        if (response is Welcome welcome)
                        {
                            epoch++; firmware = welcome.Text; pending = null; management.Clear(); maxPower = null; laserMode = false; homing = false; standardFrame = false; mpos = wco = wpos = null;
                            if (sender is { IsTerminal: false }) sender.Receive(response, Now);
                        }
                        if (response is Message message)
                        {
                            if (message.Text.StartsWith("[VER:1.1", StringComparison.Ordinal)) firmware = "Grbl 1.1 " + message.Text;
                            if (message.Text.StartsWith("$30=", StringComparison.Ordinal) && double.TryParse(message.Text.AsSpan(4), CultureInfo.InvariantCulture, out var p)) maxPower = double.IsFinite(p) && p > 0 ? p : null;
                            if (message.Text.StartsWith("$32=", StringComparison.Ordinal)) laserMode = message.Text[4..] == "1";
                            if (message.Text.StartsWith("$22=", StringComparison.Ordinal)) homing = message.Text[4..] == "1";
                            if (message.Text.StartsWith("[GC:", StringComparison.Ordinal))
                            {
                                var modes = message.Text.Trim('[', ']').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                standardFrame = modes.Contains("G54") && modes.Contains("G17") && modes.Contains("G94");
                            }
                        }
                        if (response is Status status)
                        {
                            machine = status.State; lastStatus = Now;
                            double[]? Coordinates(string key)
                            {
                                if (!status.Fields.TryGetValue(key, out var text)) return null;
                                var parts = text.Split(','); if (parts.Length < 2) return null;
                                if (!double.TryParse(parts[0], CultureInfo.InvariantCulture, out var x) || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out var y) || !double.IsFinite(x) || !double.IsFinite(y)) return null;
                                return [x, y];
                            }
                            mpos = Coordinates("MPos") ?? mpos; wco = Coordinates("WCO") ?? wco;
                            wpos = Coordinates("WPos") ?? (mpos is not null && wco is not null ? [mpos[0] - wco[0], mpos[1] - wco[1]] : null);
                            position = string.Join(" · ", status.Fields.Where(x => x.Key is "MPos" or "WPos" or "WCO").Select(x => x.Key + ":" + x.Value));
                        }
                        if (pending is not null && response is Acknowledgement or CommandError)
                        {
                            if (response is CommandError ce) { error = $"设备命令错误 {ce.Code}"; management.Clear(); }
                            pending = null; await SendManagement();
                        }
                        else if (sender is not null) { sender.Receive(response, Now); await FlushSender(); }
                        if (response is Alarm) { management.Clear(); pending = null; }
                        Publish();
                    }
                }
                finally { gate.Release(); }
            }
            if (!stop.IsCancellationRequested) await Fail("串口读取结束");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception) when (stop.IsCancellationRequested) { }
        catch (Exception e) { await Fail(e.Message); }
    }
    private async Task TickLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                await gate.WaitAsync(stop.Token);
                try
                {
                    if (pending is not null && Now >= commandDeadline) throw new IOException("设备命令应答超时");
                    if (sender is { IsTerminal: false })
                    {
                        if (sender.State == ExecutionState.Cancelling && Now >= cancelDeadline) sender.TransportLost();
                        sender.Tick(Now); await FlushSender();
                    }
                    else await Write("?");
                    Publish();
                }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception) when (stop.IsCancellationRequested) { }
        catch (Exception e) { await Fail(e.Message); }
    }
    private void RequireIdle(bool allowAlarm = false)
    {
        if (connection != "Connected" || (machine != "Idle" && !(allowAlarm && machine == "Alarm")) || Now - lastStatus > TimeSpan.FromSeconds(2)) throw new InvalidOperationException("需要最新的设备 Idle 状态");
        if (sender is { IsTerminal: false } || pending is not null) throw new InvalidOperationException("设备正在处理任务或命令");
    }
    public Task ReadConfiguration() => UnderGate(async () =>
    {
        RequireIdle(true); maxPower = null; laserMode = false; standardFrame = false; management.Enqueue("$I"); management.Enqueue("$$"); management.Enqueue("$G"); await SendManagement();
    });
    public Task SelectStandardFrame() => UnderGate(async () =>
    {
        RequireIdle(); standardFrame = false; wpos = wco = null;
        management.Enqueue("G54 G17 G94"); management.Enqueue("$G"); await SendManagement();
    });
    public Task CancelJog() => UnderGate(async () =>
    {
        if (machine != "Jog" || sender is { IsTerminal: false }) throw new InvalidOperationException("当前没有可取消的点动");
        await transport.WriteAsync(new byte[] { 0x85 }, stop.Token); AddLog("> [JOG CANCEL]");
    });
    public bool HomingEnabled => homing;
    public Task Home(bool hardwareConfirmed) => UnderGate(async () =>
    {
        RequireIdle(true);
        if (!homing || !hardwareConfirmed) throw new InvalidOperationException("回零需 $22=1 和开关/方向确认");
        wpos = mpos = wco = null; pending = "home"; commandDeadline = Now + TimeSpan.FromMinutes(2); await Write("$H\n");
    });
    public Task Unlock(bool confirmed) => UnderGate(async () =>
    {
        RequireIdle(true);
        if (!confirmed || machine != "Alarm") throw new InvalidOperationException("仅在处理报警原因后解锁");
        pending = "unlock"; commandDeadline = Now + TimeSpan.FromSeconds(5); await Write("$X\n");
    });
    private async Task SendManagement()
    {
        if (pending is null && management.TryDequeue(out var next))
        { pending = next; commandDeadline = Now + TimeSpan.FromSeconds(5); await Write(next + "\n"); }
    }
    public Task Jog(double x, double y, double step, double feed) => UnderGate(async () =>
    {
        RequireIdle();
        if (!double.IsFinite(step) || step is <= 0 or > 10 || !double.IsFinite(feed) || feed is <= 0 or > 1200 || Math.Abs(x) + Math.Abs(y) != 1) throw new ArgumentException("点动参数超限");
        pending = "jog"; commandDeadline = Now + TimeSpan.FromSeconds(5);
        await Write(FormattableString.Invariant($"$J=G91 G21 X{x * step:0.###} Y{y * step:0.###} F{feed:0.###}\n"));
    });
    public Task SetOrigin() => UnderGate(async () =>
    {
        RequireIdle(); wpos = wco = null; pending = "origin"; commandDeadline = Now + TimeSpan.FromSeconds(5);
        await Write("G10 L20 P1 X0 Y0\n");
    });
    public Task Start(StoredJob job, JobLimits limits, Guid commandId, long expectedEpoch, bool operatorConfirmed) => UnderGate(async () =>
    {
        var fingerprint = FormattableString.Invariant($"{job.Id}:{job.Hash}:{limits.Width}:{limits.Height}:{limits.MaxFeed}:{limits.MaxPower}");
        if (store.IsDuplicateStart(commandId, fingerprint)) return;
        if (!operatorConfirmed || epoch != expectedEpoch) throw new InvalidOperationException("确认已失效，请重新检查");
        RequireIdle();
        if (!firmware.StartsWith("Grbl 1.1", StringComparison.Ordinal) || maxPower is null || maxPower < limits.MaxPower) throw new InvalidOperationException("请读取 GRBL 1.1 配置并匹配 $30 功率范围");
        if (wpos is null || wpos[0] < -0.001 || wpos[1] < -0.001 || wpos[0] > limits.Width || wpos[1] > limits.Height)
            throw new InvalidOperationException("需要有效且位于工作区域内的 WPos；请查询状态并设置工件原点");
        if (!standardFrame) throw new InvalidOperationException("请读取配置并确认 G54/G17/G94 模态");
        var analysis = GcodeProgram.Prepare(job.Content, limits);
        if (analysis.Plan.Sha256 != job.Hash) throw new InvalidDataException("任务内容已变化");
        if (analysis.EmitsLaser && !laserMode) throw new InvalidOperationException("激光加工需要设备 $32=1");
        // Keep UI policy outside protocol; profile confirmation acknowledges standard 1.1 barrier semantics.
        var candidate = new JobSender(analysis.Plan, TimeSpan.FromSeconds(Math.Max(30, analysis.EstimatedSeconds + 10)));
        candidate.Start(Now, true);
        store.RecordStart(commandId, fingerprint, candidate.Snapshot); // Atomic intent + dedup precede device writes.
        sender = candidate; error = null;
        await FlushSender(); Publish();
    });
    public Task Pause(Guid executionId) => UnderGate(async () => { RequireExecution(executionId); sender!.Pause(); await FlushSender(); Publish(); });
    public Task Resume(Guid executionId, bool confirmed) => UnderGate(async () =>
    {
        RequireExecution(executionId);
        if (!confirmed || machine != "Hold:0" || Now - lastStatus > TimeSpan.FromSeconds(2)) throw new InvalidOperationException("需要现场确认和最新 Hold:0");
        sender!.Resume(Now); await FlushSender(); Publish();
    });
    public Task Cancel(Guid executionId) => UnderGate(async () =>
    {
        RequireExecution(executionId); sender!.ConfirmedCancel(); cancelDeadline = Now + TimeSpan.FromSeconds(5);
        await FlushSender(); Publish();
    });
    private void RequireExecution(Guid executionId)
    { if (sender is null || sender.ExecutionId != executionId || sender.IsTerminal) throw new InvalidOperationException("执行记录已失效"); }
    private async Task UnderGate(Func<Task> operation)
    {
        await gate.WaitAsync(stop.Token);
        try { await operation(); }
        finally { gate.Release(); }
    }
    private async Task FlushSender()
    {
        if (sender is null) return;
        foreach (var write in sender.DrainWrites()) await Write(write);
        var value = sender.Snapshot;
        var signature = $"{value.ExecutionId}:{value.State}:{value.SentLines}:{value.AcknowledgedLines}:{value.Error}";
        if (signature != savedExecution) { store.Save(value); savedExecution = signature; }
    }
    private async Task Write(string text)
    { await transport.WriteAsync(Encoding.ASCII.GetBytes(text), stop.Token); AddLog("> " + text.Replace("\u0018", "[RESET]").Trim()); }
    private void Publish()
    {
        Volatile.Write(ref snapshot, new(id, epoch, connection, firmware, machine, position, laserMode, maxPower, sender?.Snapshot, error, ++sequence));
    }
    private async Task Fail(string reason)
    {
        await gate.WaitAsync();
        try
        {
            error = reason; connection = "Lost"; sender?.TransportLost(); management.Clear(); pending = null;
            if (sender is not null)
            {
                store.Save(sender.Snapshot);
                foreach (var write in sender.DrainWrites()) { try { await transport.WriteAsync(Encoding.ASCII.GetBytes(write), CancellationToken.None); } catch (Exception) { } }
            }
            stop.Cancel(); Publish();
        }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        await gate.WaitAsync();
        try
        {
            if (sender is { IsTerminal: false })
            {
                try { await Write("!"); } catch (Exception) { }
                sender.TransportLost(); store.Save(sender.Snapshot);
            }
            stop.Cancel();
        }
        finally { gate.Release(); }
        await transport.DisposeAsync();
        await Task.WhenAll(reader, ticker);
        if (sender is { IsTerminal: false }) { sender.TransportLost(); store.Save(sender.Snapshot); }
        connection = "Disconnected"; Publish(); stop.Dispose(); gate.Dispose();
    }
}
