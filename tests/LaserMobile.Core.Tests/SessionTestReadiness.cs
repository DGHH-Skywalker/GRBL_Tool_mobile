using LaserMobile.Application;
using Xunit;
namespace LaserMobile.Core.Tests;

internal static class SessionTestReadiness
{
    // Observe the protocol boundary, rather than assuming a CI worker completed it in 300/450 ms.
    public static async Task ConfiguredIdle(DeviceSession session)
    {
        for (int attempt = 0; attempt < 400; attempt++)
        {
            var log = session.Log;
            int identity = Array.FindLastIndex(log, line => line == "> $I");
            int modes = Array.FindLastIndex(log, line => line == "> $G");
            int ack = Array.FindLastIndex(log, line => line.StartsWith("< Acknowledgement", StringComparison.Ordinal));
            int idle = Array.FindLastIndex(log, line => line.StartsWith("< Status { State = Idle", StringComparison.Ordinal));
            if (identity >= 0 && modes > identity && ack > modes && idle > ack && session.Snapshot.WorkX is not null) return;
            await Task.Delay(20);
        }
        Assert.Fail("Configuration ACK and a subsequent Idle with coordinates were not observed.\n" + string.Join('\n', session.Log));
    }
}
