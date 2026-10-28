namespace CodeDock.Core.Models;

public sealed record IdeLaunchReceipt(int ProcessId, long ProcessStartTimeUtcTicks)
{
    public bool Matches(IdeWindowCandidate candidate) =>
        ProcessId > 0
        && ProcessId == candidate.ProcessId
        && (ProcessStartTimeUtcTicks == 0
            || candidate.ProcessStartTimeUtcTicks == 0
            || ProcessStartTimeUtcTicks == candidate.ProcessStartTimeUtcTicks);
}
