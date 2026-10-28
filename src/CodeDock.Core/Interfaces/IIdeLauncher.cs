using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IIdeLauncher
{
    Task<OperationResult> LaunchAsync(
        ProjectWorkspace project,
        IdeInstallation installation,
        CancellationToken cancellationToken = default);
}
