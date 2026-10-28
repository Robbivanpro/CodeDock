using CodeDock.Core.Models;
using CodeDock.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeDock.Infrastructure.Tests;

public sealed class SessionJsonStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"CodeDock-Session-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadAsync_RestoresSelectionAndPreferences()
    {
        var sut = new SessionJsonStore(
            new CodeDockDataPaths(_temporaryDirectory),
            NullLogger<SessionJsonStore>.Instance);
        var projectId = Guid.NewGuid();

        await sut.SaveAsync(new SessionState
        {
            SelectedProjectId = projectId,
            Preferences = new AppPreferences
            {
                Theme = "Dark",
                WorkspaceMode = "WindowManager",
                OpenProjectsOnStartup = false
            }
        });
        var result = await sut.LoadAsync();

        Assert.Equal(projectId, result.Value.SelectedProjectId);
        Assert.Equal("WindowManager", result.Value.Preferences.WorkspaceMode);
        Assert.False(result.Value.Preferences.OpenProjectsOnStartup);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}
