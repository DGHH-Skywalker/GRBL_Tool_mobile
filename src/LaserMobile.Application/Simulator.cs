using System.Text;
using LaserMobile.Domain;
using LaserMobile.Grbl;
namespace LaserMobile.Application;

/// <summary>Bounded fixture controller: ACK precedes execution. No physical transport.</summary>
public sealed class SimulatedController
{
    private readonly Queue<string> planner = new();
    public string State { get; private set; } = "Idle";
    public int ExecutedLines { get; private set; }
    public bool DropAcknowledgements { get; set; }
    private bool barrier;
    public IReadOnlyList<GrblResponse> Write(string text)
    {
        if (text == "?") return [new Status(State, new Dictionary<string, string>())];
        if (text == "!") { State = "Hold:0"; return []; }
        if (text == "~") { State = planner.Count > 0 ? "Run" : "Idle"; return []; }
        if (text == "G4 P0\n")
        {
            barrier = true;
            return planner.Count == 0 ? FinishBarrier() : [];
        }
        if (planner.Count >= 16) return [new CommandError(24)];
        planner.Enqueue(text); State = "Run";
        return DropAcknowledgements ? [] : [new Acknowledgement()];
    }
    public IReadOnlyList<GrblResponse> Advance()
    {
        if (State == "Hold:0") return [];
        if (planner.TryDequeue(out _)) ExecutedLines++;
        if (planner.Count == 0)
        {
            State = "Idle";
            if (barrier) return FinishBarrier();
        }
        return [];
    }
    private IReadOnlyList<GrblResponse> FinishBarrier()
    { barrier = false; return DropAcknowledgements ? [] : [new Acknowledgement()]; }
}

public sealed class SimulationSession
{
    public const string Sample = "G21\nG90\nM5\nG1 X10 Y10 F300\nG1 X0 Y0 F300\nM5\n";
    public JobSender Sender { get; } = new(DryRunJob.Parse(Sample));
    public SimulatedController Controller { get; } = new();
    private DateTimeOffset now = DateTimeOffset.UnixEpoch;
    public void Start() => Sender.Start(now, verifiedBarrier: true);
    public void Step()
    {
        now += TimeSpan.FromMilliseconds(100);
        foreach (var response in Controller.Advance()) Sender.Receive(response, now);
        foreach (var write in Sender.DrainWrites())
            foreach (var response in Controller.Write(write)) Sender.Receive(response, now);
        Sender.Tick(now);
    }
}
