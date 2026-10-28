using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IIdeLauncher
{
    Task<OperationResult<IdeLaunchReceipt>> LaunchAsync(
        ProjectWorkspace project,
        IdeInstallation installation,
        CancellationToken cancellationToken = default);
}
