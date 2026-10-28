using CodeDock.Core.Models;
using CodeDock.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeDock.Infrastructure.Tests;

public sealed class ProjectJsonStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"CodeDock-Store-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsVersionedProjectData()
    {
        var sut = CreateStore();
        var project = new ProjectWorkspace
        {
            Id = Guid.NewGuid(),
            Name = "Backend API",
            FolderPath = "C:\\Repos\\Backend.API",
            GitBranch = "main",
            SortOrder = 0,
            IsPinned = true,
            PreferredEnvironment = DevelopmentEnvironment.VisualStudio2022
        };

        await sut.SaveAsync([project]);
        var result = await sut.LoadAsync();

        var loaded = Assert.Single(result.Value);
        Assert.Equal(project.Id, loaded.Id);
        Assert.Equal("Backend API", loaded.Name);
        Assert.Equal("main", loaded.GitBranch);
        Assert.True(loaded.IsPinned);
        Assert.Equal(DevelopmentEnvironment.VisualStudio2022, loaded.PreferredEnvironment);
        Assert.Null(result.Warning);
        Assert.Contains("\"schemaVersion\": 1", await File.ReadAllTextAsync(Path.Combine(_temporaryDirectory, "projects.json")));
        Assert.Contains("\"preferredEnvironment\": \"VisualStudio2022\"", await File.ReadAllTextAsync(Path.Combine(_temporaryDirectory, "projects.json")));
    }

    [Fact]
    public async Task LoadAsync_QuarantinesCorruptJsonAndReturnsVisibleWarning()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "projects.json"), "{ definitely not json");
        var sut = CreateStore();

        var result = await sut.LoadAsync();

        Assert.Empty(result.Value);
        Assert.Contains("corrupt", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(_temporaryDirectory, "projects.json.corrupt-*"));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "projects.json")));
    }

    [Fact]
    public async Task LoadAsync_RecoversBackupWhenPrimaryIsCorrupt()
    {
        var sut = CreateStore();
        var first = Project("First");
        var second = Project("Second");
        await sut.SaveAsync([first]);
        await sut.SaveAsync([second]);
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "projects.json"), "broken");

        var result = await sut.LoadAsync();

        Assert.Equal("First", Assert.Single(result.Value).Name);
        Assert.Contains("recovered", result.Warning, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }

    private ProjectJsonStore CreateStore() => new(
        new CodeDockDataPaths(_temporaryDirectory),
        NullLogger<ProjectJsonStore>.Instance);

    private static ProjectWorkspace Project(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        FolderPath = $"C:\\Repos\\{name}"
    };
}
