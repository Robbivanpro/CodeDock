using CodeDock.Core.Models;

namespace CodeDock.Core.Services;

public sealed class WindowAssociationResolver
{
    public IdeOperationResult Resolve(
        IReadOnlyCollection<IdeWindowCandidate> before,
        IReadOnlyCollection<IdeWindowCandidate> after)
    {
        var existingHandles = before.Select(window => window.Handle).ToHashSet();
        var newCandidates = after.Where(window => !existingHandles.Contains(window.Handle)).ToList();

        return newCandidates.Count switch
        {
            1 => new IdeOperationResult(
                IdeOperationStatus.LaunchedAndAssociated,
                "The newly created IDE window was associated with the workspace.",
                newCandidates[0].Handle,
                newCandidates),
            > 1 => new IdeOperationResult(
                IdeOperationStatus.Ambiguous,
                "Multiple IDE windows appeared after launch. Select the correct window manually.",
                Candidates: newCandidates),
            _ => new IdeOperationResult(
                IdeOperationStatus.LaunchedWithoutAssociation,
                "The IDE was launched, but no new top-level window could be identified with certainty.",
                Candidates: after.ToList())
        };
    }
}
