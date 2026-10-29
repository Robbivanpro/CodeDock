using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IIdeWorkspaceService
{
    IReadOnlyList<string> GetVisualStudioSolutions(ProjectWorkspace project);

    Task<IdeOperationResult> OpenOrActivateAsync(
        ProjectWorkspace project,
        CancellationToken cancellationToken = default);

    IReadOnlyList<IdeWindowCandidate> GetCandidates(DevelopmentEnvironment environment);

    IdeOperationResult AssociateAndActivate(ProjectWorkspace project, IdeWindowCandidate candidate);

    long? GetAssociatedWindow(Guid projectId);

    IdeSessionSnapshot GetSession(Guid projectId, DevelopmentEnvironment environment);

    void BeginEmbedding(Guid projectId);

    void BeginDetaching(Guid projectId);

    void SetEmbeddingState(Guid projectId, bool embedded);

    Task<IdeCloseResult> CloseAsync(
        Guid projectId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    void ClearAssociation(Guid projectId);
}
