using System.Collections.ObjectModel;
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
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly SemaphoreSlim _sessionSaveLock = new(1, 1);
    private SessionState _session = new();
    private bool _isInitialized;

    public MainWindowViewModel(
        IProjectRegistry registry,
        ISessionStore sessionStore,
        IFolderPicker folderPicker,
        IUserDialogService dialogService,
        ILogger<MainWindowViewModel> logger)
    {
        _registry = registry;
        _sessionStore = sessionStore;
        _folderPicker = folderPicker;
        _dialogService = dialogService;
        _logger = logger;
    }

    public ObservableCollection<ProjectTabViewModel> Projects { get; } = [];

    public IReadOnlyList<DevelopmentEnvironment> AvailableEnvironments { get; } = Enum.GetValues<DevelopmentEnvironment>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveProjectCommand))]
    private ProjectTabViewModel? _selectedProject;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveProjectCommand))]
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

    [RelayCommand(CanExecute = nameof(CanRemoveProject))]
    private async Task RemoveProjectAsync(ProjectTabViewModel? project)
    {
        project ??= SelectedProject;
        if (project is null || !_dialogService.ConfirmRemove(project.Name))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var index = Projects.IndexOf(project);
            var result = await _registry.RemoveProjectAsync(project.Id);
            if (!result.IsSuccess)
            {
                _dialogService.ShowWarning(result.Error ?? "The project could not be removed.");
                return;
            }

            Projects.Remove(project);
            SelectedProject = Projects.Count == 0 ? null : Projects[Math.Min(index, Projects.Count - 1)];
            StatusMessage = $"Removed {project.Name} from CodeDock.";
        }
        catch (Exception exception)
        {
            HandleFailure(exception, "The project could not be removed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRemoveProject(ProjectTabViewModel? project) => !IsBusy && (project is not null || SelectedProject is not null);

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

    partial void OnSelectedProjectChanged(ProjectTabViewModel? value)
    {
        if (!_isInitialized)
        {
            return;
        }

        _session.SelectedProjectId = value?.Id;
        _ = SaveSessionSafelyAsync();
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
