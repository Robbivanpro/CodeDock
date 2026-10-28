using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IIdeLocator
{
    Task<IdeInstallation?> LocateAsync(
        DevelopmentEnvironment environment,
        CancellationToken cancellationToken = default);
}
