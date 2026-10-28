namespace CodeDock.Core.Models;

public enum IdeOperationStatus
{
    Activated,
    LaunchedAndAssociated,
    LaunchedWithoutAssociation,
    Ambiguous,
    NotInstalled,
    Failed
}

public sealed record IdeOperationResult(
    IdeOperationStatus Status,
    string Message,
    long? WindowHandle = null,
    IReadOnlyList<IdeWindowCandidate>? Candidates = null)
{
    public bool IsSuccess => Status is IdeOperationStatus.Activated or IdeOperationStatus.LaunchedAndAssociated;
}
