namespace CodeDock.Core.Models;

public sealed record IdeSessionSnapshot(
    Guid WorkspaceId,
    DevelopmentEnvironment Environment,
    int? ProcessId,
    long? WindowHandle,
    long ProcessStartTimeUtcTicks,
    long Generation,
    IdeSessionState State,
    string? LastError)
{
    public bool HasAssociation => ProcessId is not null && WindowHandle is not null;
}
