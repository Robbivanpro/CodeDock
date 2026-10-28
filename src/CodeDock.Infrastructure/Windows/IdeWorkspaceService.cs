using System.Collections.Concurrent;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;
using CodeDock.Core.Services;
using Microsoft.Extensions.Logging;

namespace CodeDock.Infrastructure.Windows;

public sealed class IdeWorkspaceService : IIdeWorkspaceService
{
    private readonly IIdeLocator _locator;
    private readonly IIdeLauncher _launcher;
    private readonly IDesktopWindowService _windowService;
    private readonly WindowAssociationResolver _associationResolver;
    private readonly ILogger<IdeWorkspaceService> _logger;
    private readonly ConcurrentDictionary<Guid, long> _associations = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _launchLocks = new();

    public IdeWorkspaceService(
        IIdeLocator locator,
        IIdeLauncher launcher,
        IDesktopWindowService windowService,
        WindowAssociationResolver associationResolver,
        ILogger<IdeWorkspaceService> logger)
    {
        _locator = locator;
        _launcher = launcher;
        _windowService = windowService;
        _associationResolver = associationResolver;
        _logger = logger;
    }

    public async Task<IdeOperationResult> OpenOrActivateAsync(
        ProjectWorkspace project,
        CancellationToken cancellationToken = default)
    {
        if (_associations.TryGetValue(project.Id, out var associatedHandle))
        {
            if (_windowService.IsWindow(associatedHandle))
            {
                return _windowService.TryActivate(associatedHandle)
                    ? new IdeOperationResult(IdeOperationStatus.Activated, "The associated IDE window was activated.", associatedHandle)
                    : new IdeOperationResult(IdeOperationStatus.Failed, "Windows prevented CodeDock from bringing the IDE window to the foreground.", associatedHandle);
            }

            _associations.TryRemove(project.Id, out _);
        }

        var launchLock = _launchLocks.GetOrAdd(project.Id, _ => new SemaphoreSlim(1, 1));
        if (!await launchLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new IdeOperationResult(IdeOperationStatus.Failed, "An IDE launch is already in progress for this workspace.");
        }

        try
        {
            var installation = await _locator.LocateAsync(project.PreferredEnvironment, cancellationToken).ConfigureAwait(false);
            if (installation is null)
            {
                return new IdeOperationResult(IdeOperationStatus.NotInstalled, "The selected development environment was not found on this computer.");
            }

            var before = _windowService.EnumerateIdeWindows(project.PreferredEnvironment);
            var launchResult = await _launcher.LaunchAsync(project, installation, cancellationToken).ConfigureAwait(false);
            if (!launchResult.IsSuccess)
            {
                return new IdeOperationResult(IdeOperationStatus.Failed, launchResult.Error ?? "The IDE could not be launched.");
            }

            var after = await WaitForWindowChangeAsync(project.PreferredEnvironment, before, cancellationToken).ConfigureAwait(false);
            var association = _associationResolver.Resolve(before, after);
            if (association.Status == IdeOperationStatus.LaunchedAndAssociated && association.WindowHandle is long handle)
            {
                _associations[project.Id] = handle;
                _windowService.TryActivate(handle);
            }

            _logger.LogInformation(
                "IDE operation for workspace {WorkspaceId} completed with status {Status}",
                project.Id,
                association.Status);
            return association;
        }
        finally
        {
            launchLock.Release();
        }
    }

    public IReadOnlyList<IdeWindowCandidate> GetCandidates(DevelopmentEnvironment environment) =>
        _windowService.EnumerateIdeWindows(environment);

    public IdeOperationResult AssociateAndActivate(ProjectWorkspace project, IdeWindowCandidate candidate)
    {
        if (candidate.Environment != project.PreferredEnvironment || !_windowService.IsWindow(candidate.Handle))
        {
            return new IdeOperationResult(IdeOperationStatus.Failed, "The selected IDE window is no longer a valid candidate.");
        }

        _associations[project.Id] = candidate.Handle;
        return _windowService.TryActivate(candidate.Handle)
            ? new IdeOperationResult(IdeOperationStatus.Activated, "The selected IDE window was associated and activated.", candidate.Handle)
            : new IdeOperationResult(IdeOperationStatus.Failed, "The window was associated, but Windows prevented foreground activation.", candidate.Handle);
    }

    public long? GetAssociatedWindow(Guid projectId) =>
        _associations.TryGetValue(projectId, out var handle) && _windowService.IsWindow(handle) ? handle : null;

    public void ClearAssociation(Guid projectId) => _associations.TryRemove(projectId, out _);

    private async Task<IReadOnlyList<IdeWindowCandidate>> WaitForWindowChangeAsync(
        DevelopmentEnvironment environment,
        IReadOnlyCollection<IdeWindowCandidate> before,
        CancellationToken cancellationToken)
    {
        var existingHandles = before.Select(window => window.Handle).ToHashSet();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            var current = _windowService.EnumerateIdeWindows(environment);
            if (current.Any(window => !existingHandles.Contains(window.Handle)))
            {
                await Task.Delay(600, cancellationToken).ConfigureAwait(false);
                return _windowService.EnumerateIdeWindows(environment);
            }
        }

        return _windowService.EnumerateIdeWindows(environment);
    }
}
