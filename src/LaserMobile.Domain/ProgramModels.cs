namespace LaserMobile.Domain;

public sealed record JobLimits(double Width, double Height, double MaxFeed, double MaxPower)
{
    public static JobLimits Analysis { get; } = new(100000, 100000, 1000000, 1000000);
    public void Validate()
    {
        if (new[] { Width, Height, MaxFeed, MaxPower }.Any(x => !double.IsFinite(x) || x <= 0))
            throw new ArgumentException("尺寸、速度和功率上限必须为正数");
    }
}
public sealed record ToolSegment(double X1, double Y1, double X2, double Y2, bool Rapid, double Power);
public sealed record ProgramAnalysis(JobPlan Plan, IReadOnlyList<ToolSegment> Segments, double MinX, double MinY, double MaxX, double MaxY, double EstimatedSeconds, bool EmitsLaser);
public sealed record WorkPosition(double X, double Y);
public sealed record SessionSnapshot(Guid SessionId, long ControllerEpoch, string Connection, string Firmware, string Machine, string Position, bool LaserMode, double? MaxPower, ExecutionSnapshot? Execution, string? Error, long Sequence)
{
    public bool IsArmed { get; init; }
    public double? WorkX { get; init; }
    public double? WorkY { get; init; }
    public int? ReceiveBufferBytes { get; init; }
}
public sealed record StoredJob(Guid Id, string Name, string Hash, string Content, DateTimeOffset CreatedAt)
{
    public JobLimits? Limits { get; init; }
    public string? Parameters { get; init; }
}
public sealed record HistoryEntry(Guid Id, ExecutionState State, string? Error)
{
    public string? JobName { get; init; }
    public string? Hash { get; init; }
    public string? Parameters { get; init; }
    public string? StartedAt { get; init; }
    public string? UpdatedAt { get; init; }
    public int AcknowledgedLines { get; init; }
    public int TotalLines { get; init; }
}
