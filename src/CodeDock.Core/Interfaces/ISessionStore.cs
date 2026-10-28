using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface ISessionStore
{
    Task<StorageLoadResult<SessionState>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SessionState session, CancellationToken cancellationToken = default);
}
