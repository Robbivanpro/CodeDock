using System.Collections.ObjectModel;
using System.IO;
using CodeDock.App.Services;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodeDock.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IProjectRegistry _registry;
    private readonly ISessionStore _sessionStore;
    private readonly IFolderPicker _folderPicker;
    private readonly IUserDialogService _dialogService;
    private readonly IIdeWorkspaceService _ideWorkspaceService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly SemaphoreSlim _sessionSaveLock = new(1, 1);
    private SessionState _session = new();
    private bool _isInitialized;

    public MainWindowViewModel(
        IProjectRegistry registry,
        ISessionStore sessionStore,
        IFolderPicker folderPicker,
        IUserDialogService dialogService,
        IIdeWorkspaceService ideWorkspaceService,
        ILogger<MainWindowViewModel> logger)
    {
        _registry = registry;
        _sessionStore = sessionStore;
        _folderPicker = folderPicker;
        _dialogService = dialogService;
        _ideWorkspaceService = ideWorkspaceService;
        _logger = logger;
    }

    public ObservableCollection<ProjectTabViewModel> Projects { get; } = [];

    public IReadOnlyList<DevelopmentEnvironment> AvailableEnvironments { get; } = Enum.GetValues<DevelopmentEnvironment>();

    [ObservableProperty]
    private ProjectTabViewModel? _selectedProject;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddProjectCommand))]
    private bool _isBusy;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "Loading workspace…";
        try
        {
            await _registry.LoadProjectsAsync(cancellationToken);
            var sessionResult = await _sessionStore.LoadAsync(cancellationToken);
            _session = sessionResult.Value;

            foreach (var project in _registry.Projects)
            {
                Projects.Add(new ProjectTabViewModel(project));
            }

            SelectedProject = Projects.FirstOrDefault(project => project.Id == _session.SelectedProjectId)
                ?? Projects.FirstOrDefault();
            StatusMessage = Projects.Count == 0
                ? "Add a project folder to create your first tab."
                : $"{Projects.Count} project{(Projects.Count == 1 ? string.Empty : "s")} restored.";
            _isInitialized = true;
            if (SelectedProject is not null)
            {
                SelectedProject.IsSelected = true;
            }

            var warning = string.Join(
                Environment.NewLine + Environment.NewLine,
                new[] { _registry.LastLoadWarning, sessionResult.Warning }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(warning))
            {
                _dialogService.ShowWarning(warning);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddProject))]
    private async Task AddProjectAsync()
    {
        var folderPath = _folderPicker.PickProjectFolder();
        if (folderPath is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Adding project…";
        try
        {
            var result = await _registry.AddProjectAsync(folderPath);
            if (!result.IsSuccess || result.Value is null)
            {
                StatusMessage = result.Error ?? "The project could not be added.";
                _dialogService.ShowWarning(StatusMessage);
                return;
            }

            var tab = new ProjectTabViewModel(result.Value);
            Projects.Add(tab);
            SelectedProject = tab;
            StatusMessage = $"Added {tab.Name}.";
        }
        catch (Exception exception)
        {
            HandleFailure(exception, "The project could not be saved.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAddProject() => !IsBusy;

    public async Task<bool> RemoveTabOnlyAsync(ProjectTabViewModel project)
    {
        IsBusy = true;
        try
        {
            _ideWorkspaceService.ClearAssociation(project.Id);
            return await RemoveProjectRecordAsync(project);
        }
        catch (Exception exception)
        {
            HandleFailure(exception, "The project could not be removed.");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> CloseProjectAndIdeAsync(ProjectTabViewModel project)
    {
        if (project.IsIdeBusy)
        {
            _dialogService.ShowWarning("Wait for the current IDE operation to finish before closing this tab.");
            return false;
        }

        project.IsIdeBusy = true;
        project.IdeStatus = "Waiting for the IDE to close…";
        try
        {
            var closeResult = await _ideWorkspaceService.CloseAsync(project.Id, TimeSpan.FromSeconds(30));
            project.IdeStatus = closeResult.Message;
            StatusMessage = closeResult.Message;
            SyncSession(project);
            if (!closeResult.CanRemoveTab)
            {
                _dialogService.ShowWarning(closeResult.Message);
                return false;
            }

            return await RemoveProjectRecordAsync(project);
        }
        catch (Exception exception)
        {
            HandleFailure(exception, "The IDE and tab could not be closed safely.");
            return false;
        }
        finally
        {
            project.IsIdeBusy = false;
        }
    }

    public async Task CommitProjectNameAsync(ProjectTabViewModel project)
    {
        var result = await _registry.RenameProjectAsync(project.Id, project.Name);
        if (result.IsSuccess)
        {
            project.AcceptName(project.Name.Trim());
            StatusMessage = $"Renamed project to {project.Name}.";
            return;
        }

        project.RestoreName();
        StatusMessage = result.Error ?? "The project could not be renamed.";
        _dialogService.ShowWarning(StatusMessage);
    }

    public async Task MoveProjectAsync(ProjectTabViewModel project, ProjectTabViewModel target)
    {
        var oldIndex = Projects.IndexOf(project);
        var newIndex = Projects.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        var result = await _registry.ReorderProjectsAsync(project.Id, newIndex);
        if (!result.IsSuccess)
        {
            _dialogService.ShowWarning(result.Error ?? "The tabs could not be reordered.");
            return;
        }

        Projects.Move(oldIndex, newIndex);
        StatusMessage = "Tab order saved.";
    }

    public async Task SetPreferredEnvironmentAsync(ProjectTabViewModel project)
    {
        var model = _registry.Projects.FirstOrDefault(item => item.Id == project.Id);
        if (model is null)
        {
            return;
        }

        project.WindowHandle = _ideWorkspaceService.GetAssociatedWindow(project.Id);

        if (project.IsIdeBusy)
        {
            project.PreferredEnvironment = model.PreferredEnvironment;
            _dialogService.ShowWarning("Wait for the current IDE operation to finish before changing the environment.");
            return;
        }

        if (project.HasAssociatedWindow && model.PreferredEnvironment != project.PreferredEnvironment)
        {
            project.PreferredEnvironment = model.PreferredEnvironment;
            _dialogService.ShowWarning("Close the associated IDE or use 'Close tab only' before changing the environment.");
            return;
        }

        var result = await _registry.SetPreferredEnvironmentAsync(project.Id, project.PreferredEnvironment);
        if (result.IsSuccess)
        {
            StatusMessage = project.PreferredEnvironment == DevelopmentEnvironment.VisualStudio2022
                ? $"{project.Name} will open with Visual Studio 2022."
                : $"{project.Name} will open with VS Code.";
            return;
        }

        _dialogService.ShowWarning(result.Error ?? "The preferred development environment could not be saved.");
    }

    [RelayCommand]
    public async Task OpenWorkspaceIdeAsync(ProjectTabViewModel? project)
    {
        project ??= SelectedProject;
        if (project is null || project.IsIdeBusy)
        {
            return;
        }

        var model = _registry.Projects.FirstOrDefault(item => item.Id == project.Id);
        if (model is null)
        {
            _dialogService.ShowWarning("The project no longer exists.");
            return;
        }

        project.IsIdeBusy = true;
        project.IdeStatus = "Detecting and launching IDE…";
        StatusMessage = $"Opening {project.Name}…";

        try
        {
            var result = await _ideWorkspaceService.OpenOrActivateAsync(model);
            var discoveredLaunchTarget = result.LaunchTargetPath;
            if (result.Status is IdeOperationStatus.Ambiguous or IdeOperationStatus.LaunchedWithoutAssociation
                && result.Candidates is { Count: > 0 })
            {
                var selected = _dialogService.ChooseIdeWindow(result.Candidates);
                if (selected is not null)
                {
                    result = _ideWorkspaceService.AssociateAndActivate(model, selected) with
                    {
                        LaunchTargetPath = discoveredLaunchTarget
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(result.LaunchTargetPath)
                && File.Exists(result.LaunchTargetPath)
                && !string.Equals(
                    model.WorkspaceFilePath,
                    result.LaunchTargetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                model.WorkspaceFilePath = result.LaunchTargetPath;
                var saveTargetResult = await _registry.UpdateProjectAsync(model);
                if (!saveTargetResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "Could not persist launch target {LaunchTargetPath} for workspace {WorkspaceId}: {Error}",
                        result.LaunchTargetPath,
                        project.Id,
                        saveTargetResult.Error);
                }
                else
                {
                    _logger.LogInformation(
                        "Saved launch target {LaunchTargetPath} for workspace {WorkspaceId}",
                        result.LaunchTargetPath,
                        project.Id);
                }
            }

            project.WindowHandle = result.WindowHandle ?? _ideWorkspaceService.GetAssociatedWindow(project.Id);
            project.IdeStatus = result.Message;
            StatusMessage = result.Message;
            SyncSession(project);

            if (result.Status is IdeOperationStatus.Failed or IdeOperationStatus.NotInstalled)
            {
                _dialogService.ShowWarning(result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            project.IdeStatus = "IDE operation cancelled";
        }
        catch (Exception exception)
        {
            HandleFailure(exception, "The IDE operation failed.");
            project.IdeStatus = "IDE operation failed";
        }
        finally
        {
            project.IsIdeBusy = false;
        }
    }

    [RelayCommand]
    private void AssociateExistingIde(ProjectTabViewModel? project)
    {
        project ??= SelectedProject;
        if (project is null)
        {
            return;
        }

        var model = _registry.Projects.FirstOrDefault(item => item.Id == project.Id);
        if (model is null)
        {
            return;
        }

        var candidates = _ideWorkspaceService.GetCandidates(model.PreferredEnvironment);
        if (candidates.Count == 0)
        {
            _dialogService.ShowWarning("No visible windows were found for the selected development environment.");
            return;
        }

        var selected = _dialogService.ChooseIdeWindow(candidates);
        if (selected is null)
        {
            return;
        }

        var result = _ideWorkspaceService.AssociateAndActivate(model, selected);
        project.WindowHandle = result.WindowHandle;
        project.IdeStatus = result.Message;
        StatusMessage = result.Message;
        SyncSession(project);
    }

    public void SelectNextProject(bool backwards)
    {
        if (Projects.Count < 2)
        {
            return;
        }

        var currentIndex = Math.Max(0, Projects.IndexOf(SelectedProject!));
        var delta = backwards ? -1 : 1;
        SelectedProject = Projects[(currentIndex + delta + Projects.Count) % Projects.Count];
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _session.SelectedProjectId = SelectedProject?.Id;
        await _registry.SaveProjectsAsync(cancellationToken);
        await SaveSessionAsync(cancellationToken);
    }

    partial void OnSelectedProjectChanged(ProjectTabViewModel? oldValue, ProjectTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        if (!_isInitialized)
        {
            return;
        }

        _session.SelectedProjectId = newValue?.Id;
        _ = SaveSessionSafelyAsync();
    }

    public void SetEmbeddedState(ProjectTabViewModel project, bool embedded)
    {
        project.IsEmbedded = embedded;
        _ideWorkspaceService.SetEmbeddingState(project.Id, embedded);
        SyncSession(project);
    }

    public void BeginEmbedding(ProjectTabViewModel project) => _ideWorkspaceService.BeginEmbedding(project.Id);

    public void BeginDetaching(ProjectTabViewModel project) => _ideWorkspaceService.BeginDetaching(project.Id);

    private async Task<bool> RemoveProjectRecordAsync(ProjectTabViewModel project)
    {
        var index = Projects.IndexOf(project);
        var result = await _registry.RemoveProjectAsync(project.Id);
        if (!result.IsSuccess)
        {
            _dialogService.ShowWarning(result.Error ?? "The project could not be removed.");
            return false;
        }

        Projects.Remove(project);
        SelectedProject = Projects.Count == 0 ? null : Projects[Math.Min(index, Projects.Count - 1)];
        StatusMessage = $"Removed {project.Name} from CodeDock.";
        return true;
    }

    private void SyncSession(ProjectTabViewModel project)
    {
        var snapshot = _ideWorkspaceService.GetSession(project.Id, project.PreferredEnvironment);
        project.WindowHandle = snapshot.WindowHandle;
        project.ProcessId = snapshot.ProcessId;
        project.AssociationGeneration = snapshot.Generation;
        project.SessionState = snapshot.State;
    }

    private async Task SaveSessionSafelyAsync()
    {
        try
        {
            await SaveSessionAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save the CodeDock session");
            StatusMessage = "The current tab selection could not be saved.";
        }
    }

    private async Task SaveSessionAsync(CancellationToken cancellationToken = default)
    {
        await _sessionSaveLock.WaitAsync(cancellationToken);
        try
        {
            await _sessionStore.SaveAsync(_session, cancellationToken);
        }
        finally
        {
            _sessionSaveLock.Release();
        }
    }

    private void HandleFailure(Exception exception, string userMessage)
    {
        _logger.LogError(exception, "{UserMessage}", userMessage);
        StatusMessage = userMessage;
        _dialogService.ShowWarning($"{userMessage}\n\n{exception.Message}");
    }
}
