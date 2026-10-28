using System.Text.Json;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodeDock.Infrastructure.Persistence;

public sealed class ProjectJsonStore : IProjectStore
{
    private readonly CodeDockDataPaths _paths;
    private readonly AtomicJsonFile _jsonFile;
    private readonly ILogger<ProjectJsonStore> _logger;

    public ProjectJsonStore(CodeDockDataPaths paths, ILogger<ProjectJsonStore> logger)
    {
        _paths = paths;
        _logger = logger;
        _jsonFile = new AtomicJsonFile(logger);
    }

    public async Task<StorageLoadResult<IReadOnlyList<ProjectWorkspace>>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.ProjectsFile))
        {
            return new StorageLoadResult<IReadOnlyList<ProjectWorkspace>>([]);
        }

        try
        {
            var document = await ReadValidatedDocumentAsync(_paths.ProjectsFile, cancellationToken).ConfigureAwait(false);
            return new StorageLoadResult<IReadOnlyList<ProjectWorkspace>>(document.Projects);
        }
        catch (Exception exception) when (IsRecoverableReadFailure(exception))
        {
            _logger.LogError(exception, "Could not load the CodeDock project registry from {Path}", _paths.ProjectsFile);
            var quarantinedPath = _jsonFile.QuarantineCorruptFile(_paths.ProjectsFile);
            var backupPath = $"{_paths.ProjectsFile}.bak";

            if (File.Exists(backupPath))
            {
                try
                {
                    var backup = await ReadValidatedDocumentAsync(backupPath, cancellationToken).ConfigureAwait(false);
                    return new StorageLoadResult<IReadOnlyList<ProjectWorkspace>>(
                        backup.Projects,
                        $"The project registry was corrupt. CodeDock recovered the backup and preserved the damaged file at {quarantinedPath}.");
                }
                catch (Exception backupException) when (IsRecoverableReadFailure(backupException))
                {
                    _logger.LogError(backupException, "Could not recover the project registry backup from {Path}", backupPath);
                }
            }

            return new StorageLoadResult<IReadOnlyList<ProjectWorkspace>>(
                [],
                $"The project registry was corrupt and no usable backup was available. The damaged file was preserved at {quarantinedPath}.");
        }
    }

    public Task SaveAsync(IReadOnlyCollection<ProjectWorkspace> projects, CancellationToken cancellationToken = default)
    {
        var document = new ProjectDocument
        {
            Projects = projects.OrderBy(project => project.SortOrder).ToList()
        };
        return _jsonFile.WriteAsync(_paths.ProjectsFile, document, cancellationToken);
    }

    private async Task<ProjectDocument> ReadValidatedDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var document = await _jsonFile.ReadAsync<ProjectDocument>(path, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The project registry is empty.");
        if (document.SchemaVersion != 1)
        {
            throw new JsonException($"Unsupported project schema version {document.SchemaVersion}.");
        }

        if (document.Projects.Any(project => project.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(project.Name)
            || string.IsNullOrWhiteSpace(project.FolderPath)))
        {
            throw new JsonException("The project registry contains incomplete entries.");
        }

        return document;
    }

    private static bool IsRecoverableReadFailure(Exception exception) =>
        exception is JsonException or IOException or UnauthorizedAccessException;
}
