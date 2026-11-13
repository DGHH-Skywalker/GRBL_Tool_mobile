using LaserMobile.Domain;
namespace LaserMobile.Grbl;

/// <summary>Single-owner deterministic sender; caller serializes all events and writes.</summary>
public sealed class JobSender
{
    private readonly JobPlan job;
    private readonly Queue<string> output = new();
    private readonly TimeSpan acknowledgementTimeout;
    private readonly TimeSpan statusTimeout;
    private DateTimeOffset deadline;
    private DateTimeOffset statusDeadline;
    private DateTimeOffset nextQuery;
    private readonly Queue<int> inFlight = new();
    private readonly int? receiveWindowBytes;
    private int occupiedBytes;
    private bool barrierPending;
    private bool barrierAcknowledged;
    private bool completionQueryIssued;
    private int sent;
    private int acknowledged;
    private string? machine;
    private string? error;
    public ExecutionState State { get; private set; } = ExecutionState.Ready;
    public Guid ExecutionId { get; } = Guid.NewGuid();
    public ExecutionSnapshot Snapshot => new(ExecutionId, State, sent, acknowledged, job.Lines.Count, machine, error);
    public bool IsTerminal => State is ExecutionState.Completed or ExecutionState.Cancelled or ExecutionState.Interrupted or ExecutionState.Failed;

    public JobSender(JobPlan job, TimeSpan? acknowledgementTimeout = null, TimeSpan? statusTimeout = null, int? receiveWindowBytes = null)
        : this(GcodeProgram.Prepare(string.Join('\n', job.Lines), JobLimits.Analysis), acknowledgementTimeout, statusTimeout, receiveWindowBytes)
    {
        if (this.job.Sha256 != job.Sha256) throw new ArgumentException("Job hash mismatch.", nameof(job));
    }
    // Application already analyzed against the actual start coordinates; freeze the exact bytes once.
    public JobSender(ProgramAnalysis analysis, TimeSpan? acknowledgementTimeout = null, TimeSpan? statusTimeout = null, int? receiveWindowBytes = null)
    {
        var lines = analysis.Plan.Lines.ToArray();
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(string.Join('\n', lines) + "\n")));
        if (hash != analysis.Plan.Sha256) throw new ArgumentException("Job hash mismatch.", nameof(analysis));
        if (receiveWindowBytes is not null && receiveWindowBytes is < 80 or > 4096) throw new ArgumentOutOfRangeException(nameof(receiveWindowBytes));
        if (receiveWindowBytes is { } window && lines.Any(line => line.Length + 1 >= window)) throw new ArgumentException("Line exceeds reported RX window.", nameof(analysis));
        job = analysis.Plan with { Lines = Array.AsReadOnly(lines) };
        this.receiveWindowBytes = receiveWindowBytes;
        this.acknowledgementTimeout = acknowledgementTimeout ?? TimeSpan.FromSeconds(10);
        this.statusTimeout = statusTimeout ?? TimeSpan.FromSeconds(3);
    }
    public void Start(DateTimeOffset now, bool verifiedBarrier)
    {
        if (State != ExecutionState.Ready) throw new InvalidOperationException("Execution already started.");
        if (!verifiedBarrier) throw new InvalidOperationException("Completion barrier must be verified.");
        State = ExecutionState.Running;
        statusDeadline = now + statusTimeout;
        nextQuery = now;
        SendNext(now);
    }
    public IReadOnlyList<string> DrainWrites()
    {
        var writes = output.ToArray(); output.Clear(); return writes;
    }
    public void Tick(DateTimeOffset now)
    {
        if (IsTerminal || State == ExecutionState.Ready) return;
        if (now >= statusDeadline) { Interrupt("Controller status timeout."); return; }
        if ((inFlight.Count > 0 || barrierPending) && now >= deadline && State is not (ExecutionState.Paused or ExecutionState.Pausing))
        { Interrupt("Acknowledgement timeout; movement is not replayed."); return; }
        if (now >= nextQuery)
        {
            output.Enqueue("?");
            if (barrierAcknowledged) completionQueryIssued = true;
            nextQuery = now + TimeSpan.FromMilliseconds(200);
        }
    }
    public void Receive(GrblResponse response, DateTimeOffset now)
    {
        if (IsTerminal || State == ExecutionState.Ready) return;
        if (State == ExecutionState.Cancelling && response is Acknowledgement) return;
        switch (response)
        {
            case Welcome:
                if (State == ExecutionState.Cancelling) { State = ExecutionState.Cancelled; output.Clear(); }
                else Interrupt("Controller reset detected.");
                break;
            case Alarm a: Interrupt($"ALARM:{a.Code}"); break;
            case CommandError e: Interrupt($"error:{e.Code}"); break;
            case Status s:
                machine = s.State; statusDeadline = now + statusTimeout;
                if (s.State.StartsWith("Alarm", StringComparison.Ordinal) || s.State.StartsWith("Door", StringComparison.Ordinal))
                { Interrupt(s.State); break; }
                if (State == ExecutionState.Pausing && s.State == "Hold:0") State = ExecutionState.Paused;
                if (State == ExecutionState.Draining && barrierAcknowledged && completionQueryIssued && s.State == "Idle") State = ExecutionState.Completed;
                break;
            case Acknowledgement:
                if (inFlight.TryDequeue(out var bytes)) { occupiedBytes -= bytes; acknowledged++; deadline = now + acknowledgementTimeout; }
                else if (barrierPending) { barrierPending = false; barrierAcknowledged = true; completionQueryIssued = false; nextQuery = now; }
                else { Interrupt("Unexpected acknowledgement."); break; }
                if (State is ExecutionState.Running or ExecutionState.Draining) SendNext(now);
                break;
        }
    }
    public void Pause()
    {
        if (State is not (ExecutionState.Running or ExecutionState.Draining)) throw new InvalidOperationException("Execution is not running.");
        if (output.Count != 0) throw new InvalidOperationException("Dispatch queued writes before pause.");
        output.Enqueue("!"); State = ExecutionState.Pausing;
    }
    public void Resume(DateTimeOffset now)
    {
        if (State != ExecutionState.Paused) throw new InvalidOperationException("Controller has not confirmed hold.");
        output.Enqueue("~"); State = sent == job.Lines.Count ? ExecutionState.Draining : ExecutionState.Running;
        deadline = now + acknowledgementTimeout;
        SendNext(now);
    }
    public void Cancel()
    {
        if (IsTerminal || State == ExecutionState.Ready) throw new InvalidOperationException("No active execution.");
        // No verified reset/queue-clear adapter yet: report uncertainty rather than Cancelled.
        Interrupt("Cancellation requested; queue clearance requires device verification.");
    }
    public void TransportLost() => Interrupt("Transport lost.");
    public void ConfirmedCancel()
    {
        if (IsTerminal || State == ExecutionState.Ready) throw new InvalidOperationException("No active execution.");
        output.Clear(); output.Enqueue("!"); output.Enqueue("\u0018"); State = ExecutionState.Cancelling;
    }
    private void SendNext(DateTimeOffset now)
    {
        if (barrierPending || barrierAcknowledged) return;
        while (sent < job.Lines.Count)
        {
            int bytes = job.Lines[sent].Length + 1;
            if (receiveWindowBytes is null ? inFlight.Count != 0 : occupiedBytes + bytes >= receiveWindowBytes.Value) break;
            if (inFlight.Count == 0) deadline = now + acknowledgementTimeout;
            output.Enqueue(job.Lines[sent++] + "\n"); inFlight.Enqueue(bytes); occupiedBytes += bytes;
        }
        if (sent == job.Lines.Count && inFlight.Count == 0)
        {
            State = ExecutionState.Draining; output.Enqueue("G4 P0\n"); barrierPending = true;
            deadline = now + acknowledgementTimeout;
        }
    }
    private void Interrupt(string reason)
    {
        if (IsTerminal) return;
        error = reason; State = ExecutionState.Interrupted; output.Clear(); output.Enqueue("!");
    }
}
