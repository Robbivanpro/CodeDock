using CodeDock.Core.Models;

namespace CodeDock.Infrastructure.Persistence;

internal sealed class ProjectDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<ProjectWorkspace> Projects { get; set; } = [];
}

internal sealed class SessionDocument
{
    public int SchemaVersion { get; set; } = 1;

    public SessionState Session { get; set; } = new();
}
