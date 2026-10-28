using System.Text.Json;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodeDock.Infrastructure.Persistence;

public sealed class SessionJsonStore : ISessionStore
{
    private readonly CodeDockDataPaths _paths;
    private readonly AtomicJsonFile _jsonFile;
    private readonly ILogger<SessionJsonStore> _logger;

    public SessionJsonStore(CodeDockDataPaths paths, ILogger<SessionJsonStore> logger)
    {
        _paths = paths;
        _logger = logger;
        _jsonFile = new AtomicJsonFile(logger);
    }

    public async Task<StorageLoadResult<SessionState>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.SessionFile))
        {
            return new StorageLoadResult<SessionState>(new SessionState());
        }

        try
        {
            var document = await ReadValidatedDocumentAsync(_paths.SessionFile, cancellationToken).ConfigureAwait(false);
            return new StorageLoadResult<SessionState>(document.Session);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not load the CodeDock session from {Path}", _paths.SessionFile);
            var quarantinedPath = _jsonFile.QuarantineCorruptFile(_paths.SessionFile);
            return new StorageLoadResult<SessionState>(
                new SessionState(),
                $"The previous session was corrupt and was preserved at {quarantinedPath}.");
        }
    }

    public Task SaveAsync(SessionState session, CancellationToken cancellationToken = default) =>
        _jsonFile.WriteAsync(_paths.SessionFile, new SessionDocument { Session = session }, cancellationToken);

    private async Task<SessionDocument> ReadValidatedDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var document = await _jsonFile.ReadAsync<SessionDocument>(path, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The session file is empty.");
        return document.SchemaVersion == 1
            ? document
            : throw new JsonException($"Unsupported session schema version {document.SchemaVersion}.");
    }
}
