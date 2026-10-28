using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IProjectRegistry
{
    IReadOnlyList<ProjectWorkspace> Projects { get; }

    string? LastLoadWarning { get; }

    Task LoadProjectsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<ProjectWorkspace>> AddProjectAsync(string folderPath, CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<OperationResult> RenameProjectAsync(Guid projectId, string name, CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateProjectAsync(ProjectWorkspace project, CancellationToken cancellationToken = default);

    Task<OperationResult> SetPreferredEnvironmentAsync(
        Guid projectId,
        DevelopmentEnvironment environment,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ReorderProjectsAsync(Guid projectId, int targetIndex, CancellationToken cancellationToken = default);

    Task SaveProjectsAsync(CancellationToken cancellationToken = default);
}
