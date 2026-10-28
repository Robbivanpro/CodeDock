namespace CodeDock.Core.Models;

public sealed class ProjectWorkspace
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public string? WorkspaceFilePath { get; set; }

    public string? GitBranch { get; set; }

    public int SortOrder { get; set; }

    public bool IsPinned { get; set; }

    public DevelopmentEnvironment PreferredEnvironment { get; set; } = DevelopmentEnvironment.VisualStudioCode;
}
