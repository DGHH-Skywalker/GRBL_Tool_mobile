namespace LaserMobile.Domain;

public enum ExecutionState { Ready, Running, Pausing, Paused, Draining, Cancelling, Completed, Cancelled, Interrupted, Failed }
public sealed record JobPlan(Guid Id, string Sha256, IReadOnlyList<string> Lines);
public sealed record ExecutionSnapshot(Guid ExecutionId, ExecutionState State, int SentLines, int AcknowledgedLines, int TotalLines, string? MachineState, string? Error);
public sealed record DeviceIdentity(int VendorId, int ProductId, string? Firmware);
public sealed record DeviceProfile(string Id, string DisplayName, bool? Homing, double? TravelXMillimeters, double? TravelYMillimeters, bool VerifiedCompletionBarrier);

public interface IByteTransport : IAsyncDisposable
{
    ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken);
}
