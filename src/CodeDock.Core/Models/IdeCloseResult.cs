namespace CodeDock.Core.Models;

public enum IdeCloseStatus
{
    Closed,
    AlreadyClosed,
    CancelledOrTimedOut,
    NotAssociated,
    IdentityMismatch,
    Busy,
    Failed
}

public sealed record IdeCloseResult(IdeCloseStatus Status, string Message)
{
    public bool CanRemoveTab => Status is IdeCloseStatus.Closed or IdeCloseStatus.AlreadyClosed or IdeCloseStatus.NotAssociated;
}
