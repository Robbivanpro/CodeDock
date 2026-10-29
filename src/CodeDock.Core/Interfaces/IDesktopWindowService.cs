using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IDesktopWindowService
{
    IReadOnlyList<IdeWindowCandidate> EnumerateIdeWindows(DevelopmentEnvironment environment);

    bool IsWindow(long handle);

    bool IsSameWindow(IdeWindowCandidate identity);

    bool IsWindowResponsive(long handle);

    bool TryActivate(long handle);

    Task<WindowCloseResult> RequestCloseAsync(
        IdeWindowCandidate identity,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
