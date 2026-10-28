namespace CodeDock.Core.Models;

public enum WindowCloseStatus
{
    Closed,
    AlreadyClosed,
    CancelledOrTimedOut,
    IdentityMismatch,
    SendFailed
}

public sealed record WindowCloseResult(WindowCloseStatus Status, string Message);
