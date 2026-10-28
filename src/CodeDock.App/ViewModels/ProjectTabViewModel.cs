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
    }

    public Guid Id => _project.Id;

    public string FolderPath => _project.FolderPath;

    public string? GitBranch => _project.GitBranch;

    public bool IsFolderAvailable => Directory.Exists(_project.FolderPath);

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private DevelopmentEnvironment _preferredEnvironment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAssociatedWindow))]
    [NotifyPropertyChangedFor(nameof(EmbeddedWindowHandle))]
    private long? _windowHandle;

    [ObservableProperty]
    private bool _isIdeBusy;

    [ObservableProperty]
    private bool _isSelected;

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

    public void RestoreName() => Name = _project.Name;

    public void AcceptName(string name)
    {
        _project.Name = name;
        Name = name;
    }
}
