using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;

namespace CodeDock.Core.Services;

public sealed class ProjectRegistry : IProjectRegistry
{
    private readonly IProjectStore _store;
    private readonly IProjectPathValidator _pathValidator;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly List<ProjectWorkspace> _projects = [];

    public ProjectRegistry(IProjectStore store, IProjectPathValidator pathValidator)
    {
        _store = store;
        _pathValidator = pathValidator;
    }

    public IReadOnlyList<ProjectWorkspace> Projects => _projects;

    public string? LastLoadWarning { get; private set; }

    public async Task LoadProjectsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        _projects.Clear();
        _projects.AddRange(result.Value.OrderBy(project => project.SortOrder));
        NormalizeSortOrder();
        LastLoadWarning = result.Warning;
    }

    public async Task<OperationResult<ProjectWorkspace>> AddProjectAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        var validation = _pathValidator.ValidateFolder(folderPath);
        if (!validation.IsSuccess || validation.Value is null)
        {
            return OperationResult<ProjectWorkspace>.Failure(validation.Error ?? "The project path is invalid.");
        }

        if (_projects.Any(project => string.Equals(project.FolderPath, validation.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<ProjectWorkspace>.Failure("This project is already in CodeDock.");
        }

        var project = new ProjectWorkspace
        {
            Id = Guid.NewGuid(),
            Name = Path.GetFileName(validation.Value),
            FolderPath = validation.Value,
            SortOrder = _projects.Count
        };

        _projects.Add(project);
        await SaveProjectsAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<ProjectWorkspace>.Success(project);
    }

    public async Task<OperationResult> RemoveProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = Find(projectId);
        if (project is null)
        {
            return OperationResult.Failure("The project no longer exists.");
        }

        _projects.Remove(project);
        NormalizeSortOrder();
        await SaveProjectsAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Success();
    }

    public async Task<OperationResult> RenameProjectAsync(
        Guid projectId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var project = Find(projectId);
        if (project is null)
        {
            return OperationResult.Failure("The project no longer exists.");
        }

        var normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return OperationResult.Failure("Project name cannot be empty.");
        }

        project.Name = normalizedName;
        await SaveProjectsAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Success();
    }

    public async Task<OperationResult> UpdateProjectAsync(
        ProjectWorkspace project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var existing = Find(project.Id);
        if (existing is null)
        {
            return OperationResult.Failure("The project no longer exists.");
        }

        var pathValidation = _pathValidator.ValidateFolder(project.FolderPath);
        if (!pathValidation.IsSuccess || pathValidation.Value is null)
        {
            return OperationResult.Failure(pathValidation.Error ?? "The project path is invalid.");
        }

        if (string.IsNullOrWhiteSpace(project.Name))
        {
            return OperationResult.Failure("Project name cannot be empty.");
        }

        existing.Name = project.Name.Trim();
        existing.FolderPath = pathValidation.Value;
        existing.WorkspaceFilePath = project.WorkspaceFilePath;
        existing.GitBranch = project.GitBranch;
        existing.IsPinned = project.IsPinned;
        existing.PreferredEnvironment = project.PreferredEnvironment;
        await SaveProjectsAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Success();
    }

    public async Task<OperationResult> SetPreferredEnvironmentAsync(
        Guid projectId,
        DevelopmentEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var project = Find(projectId);
        if (project is null)
        {
            return OperationResult.Failure("The project no longer exists.");
        }

        if (!Enum.IsDefined(environment))
        {
            return OperationResult.Failure("The selected development environment is not supported.");
        }

        if (project.PreferredEnvironment != environment)
        {
            project.PreferredEnvironment = environment;
            await SaveProjectsAsync(cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Success();
    }

    public async Task<OperationResult> ReorderProjectsAsync(
        Guid projectId,
        int targetIndex,
        CancellationToken cancellationToken = default)
    {
        var project = Find(projectId);
        if (project is null)
        {
            return OperationResult.Failure("The project no longer exists.");
        }

        if (targetIndex < 0 || targetIndex >= _projects.Count)
        {
            return OperationResult.Failure("The target tab position is invalid.");
        }

        var currentIndex = _projects.IndexOf(project);
        if (currentIndex != targetIndex)
        {
            _projects.RemoveAt(currentIndex);
            _projects.Insert(targetIndex, project);
            NormalizeSortOrder();
            await SaveProjectsAsync(cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Success();
    }

    public async Task SaveProjectsAsync(CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.SaveAsync(_projects, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private ProjectWorkspace? Find(Guid projectId) => _projects.FirstOrDefault(project => project.Id == projectId);

    private void NormalizeSortOrder()
    {
        for (var index = 0; index < _projects.Count; index++)
        {
            _projects[index].SortOrder = index;
        }
    }
}
