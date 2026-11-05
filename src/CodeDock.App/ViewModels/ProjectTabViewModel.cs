using CodeDock.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace CodeDock.App.ViewModels;

public sealed partial class ProjectTabViewModel : ObservableObject
{
    private readonly ProjectWorkspace _project;

    public ProjectTabViewModel(ProjectWorkspace project)
    {
        _project = project;
        _name = project.Name;
        _preferredEnvironment = project.PreferredEnvironment;
        _launchTargetUrl = project.WorkspaceFilePath ?? string.Empty;
    }

    public Guid Id => _project.Id;

    public string FolderPath => _project.FolderPath;

    public string? GitBranch => _project.GitBranch;

    public bool IsFolderAvailable => !IsCodeWorkspace || Directory.Exists(_project.FolderPath);

    public bool IsCodeWorkspace => PreferredEnvironment.IsCodeEnvironment();

    public bool IsAppOrBrowserWorkspace => !PreferredEnvironment.IsCodeEnvironment();

    public bool IsBrowserWorkspace => PreferredEnvironment.IsBrowserEnvironment();

    public string EnvironmentDisplayName => PreferredEnvironment.GetDisplayName();

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _launchTargetUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SolutionDisplayName))]
    [NotifyPropertyChangedFor(nameof(IsCodeWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsAppOrBrowserWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsBrowserWorkspace))]
    [NotifyPropertyChangedFor(nameof(EnvironmentDisplayName))]
    [NotifyPropertyChangedFor(nameof(IsFolderAvailable))]
    private DevelopmentEnvironment _preferredEnvironment;

    [ObservableProperty]
    private bool _isVisualStudioCodeAvailable = true;

    [ObservableProperty]
    private bool _isVisualStudioAvailable = true;

    [ObservableProperty]
    private bool _isAntigravityAvailable = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAssociatedWindow))]
    [NotifyPropertyChangedFor(nameof(EmbeddedWindowHandle))]
    private long? _windowHandle;

    [ObservableProperty]
    private bool _isIdeBusy;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _hasUnreadNotification;

    public string? LastKnownWindowTitle { get; set; }

    [ObservableProperty]
    private string _ideStatus = "Ready to launch";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmbeddedWindowHandle))]
    private bool _isEmbedded;

    [ObservableProperty]
    private IdeSessionState _sessionState = IdeSessionState.Idle;

    [ObservableProperty]
    private int? _processId;

    [ObservableProperty]
    private long _associationGeneration;

    public bool HasAssociatedWindow => WindowHandle is not null;

    public long? EmbeddedWindowHandle => IsEmbedded ? WindowHandle : null;

    public string SolutionDisplayName => PreferredEnvironment != DevelopmentEnvironment.VisualStudio2022
        ? "Folder workspace"
        : string.IsNullOrWhiteSpace(_project.WorkspaceFilePath)
            ? "Choose on first open"
            : Path.GetFileName(_project.WorkspaceFilePath);

    public void RefreshLaunchTarget()
    {
        LaunchTargetUrl = _project.WorkspaceFilePath ?? string.Empty;
        OnPropertyChanged(nameof(SolutionDisplayName));
    }

    public void RestoreName() => Name = _project.Name;

    public void AcceptName(string name)
    {
        _project.Name = name;
        Name = name;
    }
}
