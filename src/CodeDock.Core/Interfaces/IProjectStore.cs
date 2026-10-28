using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IProjectStore
{
    Task<StorageLoadResult<IReadOnlyList<ProjectWorkspace>>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<ProjectWorkspace> projects, CancellationToken cancellationToken = default);
}
