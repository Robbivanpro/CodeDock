using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IDesktopWindowService
{
    IReadOnlyList<IdeWindowCandidate> EnumerateIdeWindows(DevelopmentEnvironment environment);

    bool IsWindow(long handle);

    bool TryActivate(long handle);
}
