namespace CodeDock.Core.Models;

public enum IdeOperationStatus
{
    Associated,
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
    IReadOnlyList<IdeWindowCandidate>? Candidates = null,
    string? LaunchTargetPath = null)
{
    public bool IsSuccess => Status is IdeOperationStatus.Associated or IdeOperationStatus.Activated or IdeOperationStatus.LaunchedAndAssociated;
}
