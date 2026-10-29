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
    private readonly ConcurrentDictionary<Guid, SessionRuntime> _sessions = new();
    private readonly ConcurrentDictionary<long, Guid> _windowOwners = new();
    private readonly SemaphoreSlim _launchAssociationLock = new(1, 1);

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
        var environment = project.PreferredEnvironment;
        var launchProject = SnapshotProject(project, environment);
        var session = GetOrCreateSession(project.Id, environment);
        if (!await session.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new IdeOperationResult(IdeOperationStatus.Failed, "Another IDE operation is already in progress for this workspace.");
        }

        try
        {
            if (session.Environment != environment && session.Identity is not null)
            {
                return Fail(session, "Detach or close the associated IDE before changing the preferred environment.");
            }

            session.Environment = environment;
            if (session.Identity is { } identity)
            {
                if (_windowService.IsSameWindow(identity))
                {
                    Transition(session, IdeSessionState.Associated, null);
                    return _windowService.TryActivate(identity.Handle)
                        ? new IdeOperationResult(IdeOperationStatus.Activated, "The associated IDE window was activated.", identity.Handle)
                        : new IdeOperationResult(
                            IdeOperationStatus.Associated,
                            "The IDE window is associated. Windows did not grant foreground activation, but embedding remains available.",
                            identity.Handle);
                }

                ReleaseAssociation(session);
            }

            Transition(session, IdeSessionState.Opening, null);
            await _launchAssociationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var installation = await _locator.LocateAsync(environment, cancellationToken).ConfigureAwait(false);
                if (installation is null)
                {
                    Transition(session, IdeSessionState.Failed, "The selected development environment was not found on this computer.");
                    return new IdeOperationResult(IdeOperationStatus.NotInstalled, session.LastError!);
                }

                var before = _windowService.EnumerateIdeWindows(environment);
                var launchResult = await _launcher.LaunchAsync(launchProject, installation, cancellationToken).ConfigureAwait(false);
                if (!launchResult.IsSuccess)
                {
                    return Fail(session, launchResult.Error ?? "The IDE could not be launched.");
                }

                var after = await WaitForWindowChangeAsync(environment, before, cancellationToken).ConfigureAwait(false);
                var association = _associationResolver.Resolve(before, after);
                string? claimError = null;
                if (association.Status == IdeOperationStatus.LaunchedAndAssociated
                    && association.Candidates is [var detected]
                    && (launchResult.Value is null || !launchResult.Value.Matches(detected)))
                {
                    association = new IdeOperationResult(
                        IdeOperationStatus.Ambiguous,
                        "A new IDE window appeared, but its process identity does not match the launched process. Select it manually only if it belongs to this workspace.",
                        Candidates: [detected]);
                }

                association = association with { LaunchTargetPath = launchResult.Value?.LaunchTargetPath };

                if (association.Status == IdeOperationStatus.LaunchedAndAssociated
                    && association.Candidates is [var candidate]
                    && TryClaim(session, candidate, out claimError))
                {
                    _windowService.TryActivate(candidate.Handle);
                    return association;
                }

                if (association.Status == IdeOperationStatus.LaunchedAndAssociated)
                {
                    var available = after.Where(candidate => !_windowOwners.ContainsKey(candidate.Handle)).ToList();
                    Transition(session, IdeSessionState.Failed, claimError);
                    return new IdeOperationResult(
                        IdeOperationStatus.Ambiguous,
                        claimError ?? "The detected window is already associated with another workspace.",
                        Candidates: available);
                }

                Transition(session, IdeSessionState.Failed, association.Message);
                return association with
                {
                    Candidates = association.Candidates?.Where(candidate => !_windowOwners.ContainsKey(candidate.Handle)).ToList()
                };
            }
            finally
            {
                _launchAssociationLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            Transition(session, IdeSessionState.Failed, "The IDE operation was cancelled.");
            throw;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public IReadOnlyList<IdeWindowCandidate> GetCandidates(DevelopmentEnvironment environment) =>
        _windowService.EnumerateIdeWindows(environment)
            .Where(candidate => !_windowOwners.ContainsKey(candidate.Handle))
            .ToList();

    public IdeOperationResult AssociateAndActivate(ProjectWorkspace project, IdeWindowCandidate candidate)
    {
        var session = GetOrCreateSession(project.Id, project.PreferredEnvironment);
        if (!session.Gate.Wait(0))
        {
            return new IdeOperationResult(IdeOperationStatus.Failed, "Another IDE operation is already in progress for this workspace.");
        }

        try
        {
            if (candidate.Environment != project.PreferredEnvironment || !_windowService.IsSameWindow(candidate))
            {
                return Fail(session, "The selected IDE window is no longer the same valid candidate.");
            }

            if (!TryClaim(session, candidate, out var error))
            {
                return Fail(session, error ?? "The selected IDE window belongs to another workspace.");
            }

            return _windowService.TryActivate(candidate.Handle)
                ? new IdeOperationResult(IdeOperationStatus.Activated, "The selected IDE window was associated and activated.", candidate.Handle)
                : new IdeOperationResult(
                    IdeOperationStatus.Associated,
                    "The IDE window was associated. Windows did not grant foreground activation, but embedding remains available.",
                    candidate.Handle);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public long? GetAssociatedWindow(Guid projectId)
    {
        if (!_sessions.TryGetValue(projectId, out var session) || session.Identity is not { } identity)
        {
            return null;
        }

        if (_windowService.IsSameWindow(identity))
        {
            return identity.Handle;
        }

        if (session.Gate.Wait(0))
        {
            try
            {
                ReleaseAssociation(session);
                Transition(session, IdeSessionState.Failed, "The associated IDE window is no longer available.");
            }
            finally
            {
                session.Gate.Release();
            }
        }

        return null;
    }

    public IdeSessionSnapshot GetSession(Guid projectId, DevelopmentEnvironment environment)
    {
        var session = GetOrCreateSession(projectId, environment);
        var identity = session.Identity;
        return new IdeSessionSnapshot(
            projectId,
            session.Environment,
            identity?.ProcessId,
            identity?.Handle,
            identity?.ProcessStartTimeUtcTicks ?? 0,
            session.Generation,
            session.State,
            session.LastError);
    }

    public void SetEmbeddingState(Guid projectId, bool embedded)
    {
        if (!_sessions.TryGetValue(projectId, out var session))
        {
            return;
        }

        Transition(session, embedded ? IdeSessionState.Embedded : IdeSessionState.Associated, null);
    }

    public void BeginEmbedding(Guid projectId)
    {
        if (_sessions.TryGetValue(projectId, out var session) && session.Identity is not null)
        {
            Transition(session, IdeSessionState.Embedding, null);
        }
    }

    public void BeginDetaching(Guid projectId)
    {
        if (_sessions.TryGetValue(projectId, out var session) && session.Identity is not null)
        {
            Transition(session, IdeSessionState.Detaching, null);
        }
    }

    public async Task<IdeCloseResult> CloseAsync(
        Guid projectId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(projectId, out var session))
        {
            return new IdeCloseResult(IdeCloseStatus.NotAssociated, "No IDE window is associated with this workspace.");
        }

        if (!await session.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new IdeCloseResult(IdeCloseStatus.Busy, "The workspace is busy. Wait for the current IDE operation to finish.");
        }

        try
        {
            if (session.Identity is not { } identity)
            {
                Transition(session, IdeSessionState.Closed, null);
                return new IdeCloseResult(IdeCloseStatus.NotAssociated, "No IDE window is associated with this workspace.");
            }

            Transition(session, IdeSessionState.Closing, null);
            var result = await _windowService.RequestCloseAsync(identity, timeout, cancellationToken).ConfigureAwait(false);
            switch (result.Status)
            {
                case WindowCloseStatus.Closed:
                case WindowCloseStatus.AlreadyClosed:
                    ReleaseAssociation(session);
                    Transition(session, IdeSessionState.Closed, null);
                    return new IdeCloseResult(
                        result.Status == WindowCloseStatus.Closed ? IdeCloseStatus.Closed : IdeCloseStatus.AlreadyClosed,
                        result.Message);
                case WindowCloseStatus.CancelledOrTimedOut:
                    Transition(session, IdeSessionState.Associated, result.Message);
                    return new IdeCloseResult(IdeCloseStatus.CancelledOrTimedOut, result.Message);
                case WindowCloseStatus.IdentityMismatch:
                    Transition(session, IdeSessionState.Failed, result.Message);
                    return new IdeCloseResult(IdeCloseStatus.IdentityMismatch, result.Message);
                default:
                    Transition(session, IdeSessionState.Failed, result.Message);
                    return new IdeCloseResult(IdeCloseStatus.Failed, result.Message);
            }
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public void ClearAssociation(Guid projectId)
    {
        if (!_sessions.TryGetValue(projectId, out var session) || !session.Gate.Wait(0))
        {
            return;
        }

        try
        {
            ReleaseAssociation(session);
            Transition(session, IdeSessionState.Idle, null);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private SessionRuntime GetOrCreateSession(Guid projectId, DevelopmentEnvironment environment) =>
        _sessions.GetOrAdd(projectId, id => new SessionRuntime(id, environment));

    private static ProjectWorkspace SnapshotProject(ProjectWorkspace project, DevelopmentEnvironment environment) => new()
    {
        Id = project.Id,
        Name = project.Name,
        FolderPath = project.FolderPath,
        WorkspaceFilePath = project.WorkspaceFilePath,
        GitBranch = project.GitBranch,
        SortOrder = project.SortOrder,
        IsPinned = project.IsPinned,
        PreferredEnvironment = environment
    };

    private bool TryClaim(SessionRuntime session, IdeWindowCandidate candidate, out string? error)
    {
        error = null;
        if (!_windowService.IsSameWindow(candidate))
        {
            error = "The detected IDE window changed before it could be associated.";
            return false;
        }

        if (_windowOwners.TryGetValue(candidate.Handle, out var owner) && owner != session.WorkspaceId)
        {
            error = "This IDE window is already associated with another workspace.";
            return false;
        }

        if (!_windowOwners.TryAdd(candidate.Handle, session.WorkspaceId)
            && (!_windowOwners.TryGetValue(candidate.Handle, out owner) || owner != session.WorkspaceId))
        {
            error = "This IDE window was claimed by another workspace.";
            return false;
        }

        ReleaseAssociation(session, candidate.Handle);
        session.Identity = candidate;
        session.Environment = candidate.Environment;
        session.Generation++;
        Transition(session, IdeSessionState.Associated, null);
        return true;
    }

    private void ReleaseAssociation(SessionRuntime session, long exceptHandle = 0)
    {
        if (session.Identity is not { } current)
        {
            return;
        }

        if (current.Handle != exceptHandle)
        {
            _windowOwners.TryRemove(new KeyValuePair<long, Guid>(current.Handle, session.WorkspaceId));
        }

        session.Identity = null;
    }

    private IdeOperationResult Fail(SessionRuntime session, string message, long? handle = null)
    {
        Transition(session, IdeSessionState.Failed, message);
        return new IdeOperationResult(IdeOperationStatus.Failed, message, handle);
    }

    private void Transition(SessionRuntime session, IdeSessionState next, string? error)
    {
        var previous = session.State;
        session.State = next;
        session.LastError = error;
        _logger.LogInformation(
            "Workspace {WorkspaceId} IDE state {PreviousState} -> {NextState}; PID {ProcessId}; HWND {WindowHandle}; generation {Generation}; error {Error}",
            session.WorkspaceId,
            previous,
            next,
            session.Identity?.ProcessId,
            session.Identity?.Handle,
            session.Generation,
            error);
    }

    private async Task<IReadOnlyList<IdeWindowCandidate>> WaitForWindowChangeAsync(
        DevelopmentEnvironment environment,
        IReadOnlyCollection<IdeWindowCandidate> before,
        CancellationToken cancellationToken)
    {
        var existingHandles = before.Select(window => window.Handle).ToHashSet();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        HashSet<long>? previousNewHandles = null;
        var stableChecks = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = _windowService.EnumerateIdeWindows(environment);
            var newWindows = current.Where(window => !existingHandles.Contains(window.Handle)).ToList();
            if (newWindows.Count > 0)
            {
                if (environment == DevelopmentEnvironment.VisualStudioCode)
                {
                    // Chromium can continue initializing after its top-level
                    // HWND has already been moved into the CodeDock host.
                    return current;
                }

                var newHandles = newWindows.Select(window => window.Handle).ToHashSet();
                var sameWindows = previousNewHandles is not null && previousNewHandles.SetEquals(newHandles);
                var allResponsive = newWindows.All(window => _windowService.IsWindowResponsive(window.Handle));
                stableChecks = sameWindows && allResponsive ? stableChecks + 1 : allResponsive ? 1 : 0;
                previousNewHandles = newHandles;

                // Visual Studio starts answering window messages before its
                // WPF shell has finished building the top-level composition.
                // The first reparent can therefore produce a blank surface,
                // while detaching and embedding the same HWND a few seconds
                // later works. Keep the initial HWND top-level and responsive
                // for about 4.5 seconds before the first association/embed.
                if (stableChecks >= 30)
                {
                    _logger.LogInformation(
                        "Visual Studio window became stable before association; HWNDs {WindowHandles}",
                        string.Join(", ", newHandles));
                    return current;
                }
            }

            await Task.Delay(
                environment == DevelopmentEnvironment.VisualStudioCode ? 75 : 150,
                cancellationToken).ConfigureAwait(false);
        }

        return _windowService.EnumerateIdeWindows(environment);
    }

    private sealed class SessionRuntime
    {
        public SessionRuntime(Guid workspaceId, DevelopmentEnvironment environment)
        {
            WorkspaceId = workspaceId;
            Environment = environment;
        }

        public Guid WorkspaceId { get; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public DevelopmentEnvironment Environment { get; set; }

        public IdeWindowCandidate? Identity { get; set; }

        public long Generation { get; set; }

        public IdeSessionState State { get; set; }

        public string? LastError { get; set; }
    }
}
