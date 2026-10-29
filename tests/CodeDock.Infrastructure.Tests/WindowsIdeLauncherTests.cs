using CodeDock.Core.Models;
using CodeDock.Infrastructure.VSCode;

namespace CodeDock.Infrastructure.Tests;

public sealed class WindowsIdeLauncherTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"CodeDock-Solutions-{Guid.NewGuid():N}");

    [Fact]
    public void FindVisualStudioSolutions_ReturnsAllSolutionsAtNearestDepth()
    {
        var mainDirectory = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "Main"));
        var testsDirectory = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "Tests"));
        var mainSolution = Path.Combine(mainDirectory.FullName, "Product.sln");
        var testsSolution = Path.Combine(testsDirectory.FullName, "Product.Tests.slnx");
        File.WriteAllText(mainSolution, string.Empty);
        File.WriteAllText(testsSolution, string.Empty);
        var project = new ProjectWorkspace
        {
            Id = Guid.NewGuid(),
            Name = "Product",
            FolderPath = _temporaryDirectory,
            PreferredEnvironment = DevelopmentEnvironment.VisualStudio2022
        };

        var solutions = new WindowsIdeLauncher().FindVisualStudioSolutions(project);

        Assert.Equal(2, solutions.Count);
        Assert.Contains(mainSolution, solutions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(testsSolution, solutions, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
