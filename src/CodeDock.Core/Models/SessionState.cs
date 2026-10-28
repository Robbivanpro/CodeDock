namespace CodeDock.Core.Models;

public sealed class SessionState
{
    public Guid? SelectedProjectId { get; set; }

    public AppPreferences Preferences { get; set; } = new();
}

public sealed class AppPreferences
{
    public string Theme { get; set; } = "Dark";

    public string WorkspaceMode { get; set; } = "WindowManager";

    public bool OpenProjectsOnStartup { get; set; }
}
