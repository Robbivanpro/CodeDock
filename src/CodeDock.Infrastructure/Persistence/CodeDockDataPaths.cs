namespace CodeDock.Infrastructure.Persistence;

public sealed class CodeDockDataPaths
{
    public CodeDockDataPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodeDock");
    }

    public string RootDirectory { get; }

    public string ProjectsFile => Path.Combine(RootDirectory, "projects.json");

    public string SessionFile => Path.Combine(RootDirectory, "session.json");
}
