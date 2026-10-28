using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;
using CodeDock.Core.Services;

namespace CodeDock.Core.Tests;

public sealed class ProjectRegistryTests
{
    [Fact]
    public async Task LoadProjectsAsync_OrdersAndNormalizesStoredProjects()
    {
        var store = new MemoryProjectStore(
        [
            Project("Second", "C:\\Repos\\Second", 4),
            Project("First", "C:\\Repos\\First", 2)
        ]);
        var sut = new ProjectRegistry(store, new AcceptingPathValidator());

        await sut.LoadProjectsAsync();

        Assert.Collection(
            sut.Projects,
            project => Assert.Equal(("First", 0), (project.Name, project.SortOrder)),
            project => Assert.Equal(("Second", 1), (project.Name, project.SortOrder)));
    }

    [Fact]
    public async Task AddProjectAsync_RejectsDuplicatePathsIgnoringCase()
    {
        var sut = new ProjectRegistry(new MemoryProjectStore(), new AcceptingPathValidator());

        var first = await sut.AddProjectAsync("C:\\Repos\\Backend");
        var duplicate = await sut.AddProjectAsync("c:\\repos\\BACKEND");

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Single(sut.Projects);
    }

    [Fact]
    public async Task RenameProjectAsync_RejectsEmptyNamesWithoutChangingProject()
    {
        var project = Project("Backend", "C:\\Repos\\Backend", 0);
        var sut = new ProjectRegistry(new MemoryProjectStore([project]), new AcceptingPathValidator());
        await sut.LoadProjectsAsync();

        var result = await sut.RenameProjectAsync(project.Id, "   ");

        Assert.False(result.IsSuccess);
        Assert.Equal("Backend", project.Name);
    }

    [Fact]
    public async Task ReorderProjectsAsync_MovesProjectAndPersistsNormalizedOrder()
    {
        var first = Project("First", "C:\\First", 0);
        var second = Project("Second", "C:\\Second", 1);
        var third = Project("Third", "C:\\Third", 2);
        var store = new MemoryProjectStore([first, second, third]);
        var sut = new ProjectRegistry(store, new AcceptingPathValidator());
        await sut.LoadProjectsAsync();

        var result = await sut.ReorderProjectsAsync(first.Id, 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Second", "Third", "First"], sut.Projects.Select(project => project.Name));
        Assert.Equal([0, 1, 2], store.SavedProjects.Select(project => project.SortOrder));
    }

    [Fact]
    public async Task RemoveProjectAsync_DoesNotAffectUnknownProjects()
    {
        var project = Project("Backend", "C:\\Backend", 0);
        var sut = new ProjectRegistry(new MemoryProjectStore([project]), new AcceptingPathValidator());
        await sut.LoadProjectsAsync();

        var result = await sut.RemoveProjectAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Single(sut.Projects);
    }

    [Fact]
    public async Task SetPreferredEnvironmentAsync_PersistsVisualStudio2022Selection()
    {
        var project = Project("Backend", "C:\\Backend", 0);
        var store = new MemoryProjectStore([project]);
        var sut = new ProjectRegistry(store, new AcceptingPathValidator());
        await sut.LoadProjectsAsync();

        var result = await sut.SetPreferredEnvironmentAsync(project.Id, DevelopmentEnvironment.VisualStudio2022);

        Assert.True(result.IsSuccess);
        Assert.Equal(DevelopmentEnvironment.VisualStudio2022, project.PreferredEnvironment);
        Assert.Equal(DevelopmentEnvironment.VisualStudio2022, Assert.Single(store.SavedProjects).PreferredEnvironment);
    }

    private static ProjectWorkspace Project(string name, string path, int order) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        FolderPath = path,
        SortOrder = order
    };

    private sealed class AcceptingPathValidator : IProjectPathValidator
    {
        public OperationResult<string> ValidateFolder(string folderPath) =>
            OperationResult<string>.Success(Path.TrimEndingDirectorySeparator(folderPath));
    }

    private sealed class MemoryProjectStore : IProjectStore
    {
        private readonly IReadOnlyList<ProjectWorkspace> _initialProjects;

        public MemoryProjectStore(IReadOnlyList<ProjectWorkspace>? initialProjects = null)
        {
            _initialProjects = initialProjects ?? [];
        }

        public IReadOnlyList<ProjectWorkspace> SavedProjects { get; private set; } = [];

        public Task<StorageLoadResult<IReadOnlyList<ProjectWorkspace>>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageLoadResult<IReadOnlyList<ProjectWorkspace>>(_initialProjects));

        public Task SaveAsync(IReadOnlyCollection<ProjectWorkspace> projects, CancellationToken cancellationToken = default)
        {
            SavedProjects = projects.ToList();
            return Task.CompletedTask;
        }
    }
}
