using CodeDock.Core.Models;
using CodeDock.Core.Services;

namespace CodeDock.Core.Tests;

public sealed class WindowAssociationResolverTests
{
    [Fact]
    public void Resolve_AssociatesTheOnlyNewWindow()
    {
        var existing = Candidate(10, "Existing");
        var created = Candidate(20, "Created");

        var result = new WindowAssociationResolver().Resolve([existing], [existing, created]);

        Assert.Equal(IdeOperationStatus.LaunchedAndAssociated, result.Status);
        Assert.Equal(20, result.WindowHandle);
    }

    [Fact]
    public void Resolve_ReportsAmbiguityWithoutChoosingAWindow()
    {
        var result = new WindowAssociationResolver().Resolve(
            [],
            [Candidate(20, "First"), Candidate(30, "Second")]);

        Assert.Equal(IdeOperationStatus.Ambiguous, result.Status);
        Assert.Null(result.WindowHandle);
        Assert.Equal(2, result.Candidates?.Count);
    }

    [Fact]
    public void Resolve_DoesNotAssociateAnExistingWindowByTitle()
    {
        var existing = Candidate(10, "Backend - Visual Studio Code");

        var result = new WindowAssociationResolver().Resolve([existing], [existing]);

        Assert.Equal(IdeOperationStatus.LaunchedWithoutAssociation, result.Status);
        Assert.Null(result.WindowHandle);
    }

    private static IdeWindowCandidate Candidate(long handle, string title) => new(
        handle,
        (int)handle,
        title,
        DevelopmentEnvironment.VisualStudioCode);
}
