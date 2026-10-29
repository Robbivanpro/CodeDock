using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IIdeLauncher
{
    IReadOnlyList<string> FindVisualStudioSolutions(ProjectWorkspace project);

    Task<OperationResult<IdeLaunchReceipt>> LaunchAsync(
        ProjectWorkspace project,
        IdeInstallation installation,
        CancellationToken cancellationToken = default);
}
