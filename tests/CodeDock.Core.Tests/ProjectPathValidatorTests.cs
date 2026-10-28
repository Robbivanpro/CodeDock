using CodeDock.Core.Services;

namespace CodeDock.Core.Tests;

public sealed class ProjectPathValidatorTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"CodeDock-{Guid.NewGuid():N}");

    [Fact]
    public void ValidateFolder_NormalizesAnExistingFolder()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var sut = new ProjectPathValidator();

        var result = sut.ValidateFolder($"  {_temporaryDirectory}{Path.DirectorySeparatorChar}  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_temporaryDirectory)), result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateFolder_RejectsEmptyPaths(string path)
    {
        var result = new ProjectPathValidator().ValidateFolder(path);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateFolder_RejectsMissingFolders()
    {
        var result = new ProjectPathValidator().ValidateFolder(_temporaryDirectory);

        Assert.False(result.IsSuccess);
        Assert.Contains("no longer available", result.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}
