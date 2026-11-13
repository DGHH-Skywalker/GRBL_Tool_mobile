using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using LaserMobile.Domain;

namespace LaserMobile.Application;

/// <summary>In-process GRBL fixture with real asynchronous ACK, planner, hold and drain semantics.</summary>
public sealed partial class DemoTransport : IByteTransport
{
    private readonly Channel<byte[]> responses = Channel.CreateUnbounded<byte[]>();
    private readonly Queue<string> input = new(), planner = new();
    private readonly object sync = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task clock;
    private int buffered;
    private bool held, alarm, barrier, absolute = true, closed;
    private double x, y, offsetX, offsetY, units = 1;
    public const string Sample = "G21 G90 G54 G17 G94\nM5\nG0 X5 Y5\nM4 S100\nG1 X25 Y5 F600\nG1 X25 Y25\nG1 X5 Y25\nG1 X5 Y5\nM5\nG0 X0 Y0\n";
    [GeneratedRegex(@"([XY])([+-]?(?:\d+(?:\.\d*)?|\.\d+))")]
    private static partial Regex Axis();
    public DemoTransport()
    {
        Emit("Grbl 1.1h ['$' for help]");
        clock = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(180));
                while (await timer.WaitForNextTickAsync(stop.Token))
                {
                    lock (sync)
                    {
                        if (closed || held || alarm) continue;
                        if (planner.TryDequeue(out var line)) Apply(line);
                        if (planner.Count == 0 && barrier) { barrier = false; Emit("ok"); }
                        DrainInput();
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
    }
    private void Emit(string text) => responses.Writer.TryWrite(Encoding.ASCII.GetBytes(text + "\n"));
    private string State => alarm ? "Alarm" : held ? "Hold:0" : planner.Count > 0 || input.Count > 0 || barrier ? "Run" : "Idle";
    private void Status() => Emit(FormattableString.Invariant($"<{State}|WPos:{x - offsetX:0.###},{y - offsetY:0.###},0|WCO:{offsetX:0.###},{offsetY:0.###},0>"));
    public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (closed) throw new IOException("演示连接已断开");
            if (bytes.Length == 1 && bytes.Span[0] == 0x85) { held = false; planner.Clear(); Status(); return ValueTask.CompletedTask; }
            var text = Encoding.ASCII.GetString(bytes.Span);
            switch (text)
            {
                case "?": Status(); break;
                case "!": held = true; Status(); break;
                case "~": held = false; Status(); DrainInput(); break;
                case "\u0018": ResetCore(); break;
                case "\u0085": held = false; planner.Clear(); Status(); break;
                default:
                    // One complete line per write is the contract of DeviceSession.
                    buffered += text.Length;
                    if (buffered >= 128) throw new IOException("演示 RX 缓冲区溢出");
                    input.Enqueue(text.Trim()); DrainInput(); break;
            }
        }
        return ValueTask.CompletedTask;
    }
    private void DrainInput()
    {
        while (!barrier && planner.Count < 16 && input.TryPeek(out var line))
        {
            input.Dequeue(); buffered -= line.Length + 1;
            if (line == "$I") { Emit("[VER:1.1h:DEMO]"); Emit("[OPT:V,15,128]"); }
            else if (line == "$$") { Emit("$30=1000"); Emit("$32=1"); Emit("$22=0"); }
            else if (line == "$G") Emit("[GC: G0 G54 G17 G21 G90 G94 M5]");
            else if (line == "$X") { alarm = false; held = false; }
            else if (alarm) { Emit("error:9"); continue; }
            else if (line == "G4 P0" && planner.Count > 0) { barrier = true; continue; }
            else if (line.StartsWith("G10 L20", StringComparison.Ordinal)) { offsetX = x; offsetY = y; }
            else if (line.StartsWith("$J=", StringComparison.Ordinal))
            {
                foreach (Match m in Axis().Matches(line))
                { var value = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture); if (m.Groups[1].Value == "X") x += value; else y += value; }
            }
            else if (line != "G4 P0") planner.Enqueue(line);
            Emit("ok");
        }
    }
    private void Apply(string line)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Contains("G90")) absolute = true;
        if (words.Contains("G91")) absolute = false;
        if (words.Contains("G21")) units = 1;
        if (words.Contains("G20")) units = 25.4;
        foreach (Match m in Axis().Matches(line))
        {
            var value = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * units;
            if (m.Groups[1].Value == "X") x = absolute ? offsetX + value : x + value;
            else y = absolute ? offsetY + value : y + value;
        }
    }
    private void ResetCore()
    {
        input.Clear(); planner.Clear(); buffered = 0; held = alarm = barrier = false;
        absolute = true; units = 1; offsetX = offsetY = 0;
        Emit("Grbl 1.1h ['$' for help]"); Status();
    }
    public void InjectReset() { lock (sync) ResetCore(); }
    public void InjectAlarm() { lock (sync) { planner.Clear(); input.Clear(); buffered = 0; barrier = false; alarm = true; Emit("ALARM:1"); Status(); } }
    public void InjectDisconnect() { lock (sync) { closed = true; responses.Writer.TryComplete(new IOException("演示：USB 连接中断")); } }
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var data in responses.Reader.ReadAllAsync(cancellationToken)) yield return data;
    }
    public async ValueTask DisposeAsync()
    {
        lock (sync) { closed = true; stop.Cancel(); responses.Writer.TryComplete(); }
        await clock; stop.Dispose();
    }
}
