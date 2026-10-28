using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IIdeWorkspaceService
{
    Task<IdeOperationResult> OpenOrActivateAsync(
        ProjectWorkspace project,
        CancellationToken cancellationToken = default);

    IReadOnlyList<IdeWindowCandidate> GetCandidates(DevelopmentEnvironment environment);

    IdeOperationResult AssociateAndActivate(ProjectWorkspace project, IdeWindowCandidate candidate);

    long? GetAssociatedWindow(Guid projectId);

    void ClearAssociation(Guid projectId);
}
