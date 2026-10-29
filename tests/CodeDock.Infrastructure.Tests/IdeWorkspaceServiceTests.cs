using System.Collections.Concurrent;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;
using CodeDock.Core.Services;
using CodeDock.Infrastructure.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeDock.Infrastructure.Tests;

public sealed class IdeWorkspaceServiceTests
{
    [Fact]
    public void AssociateAndActivate_RejectsHandleOwnedByAnotherWorkspace()
    {
        var windows = new FakeDesktopWindowService();
        var candidate = windows.AddWindow(101, 1001);
        var service = CreateService(windows, new FakeIdeLauncher());
        var first = CreateProject();
        var second = CreateProject();

        var firstResult = service.AssociateAndActivate(first, candidate);
        var secondResult = service.AssociateAndActivate(second, candidate);

        Assert.True(firstResult.IsSuccess);
        Assert.Equal(IdeOperationStatus.Failed, secondResult.Status);
        Assert.Equal(candidate.Handle, service.GetAssociatedWindow(first.Id));
        Assert.Null(service.GetAssociatedWindow(second.Id));
    }

    [Fact]
    public void AssociateAndActivate_KeepsSuccessfulAssociationWhenForegroundIsDenied()
    {
        var windows = new FakeDesktopWindowService { AllowActivation = false };
        var candidate = windows.AddWindow(111, 1101);
        var service = CreateService(windows, new FakeIdeLauncher());
        var project = CreateProject();

        var result = service.AssociateAndActivate(project, candidate);

        Assert.Equal(IdeOperationStatus.Associated, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(candidate.Handle, service.GetAssociatedWindow(project.Id));
    }

    [Fact]
    public async Task OpenOrActivateAsync_SerializesConcurrentWorkspaceDiscovery()
    {
        var windows = new FakeDesktopWindowService();
        var launcher = new FakeIdeLauncher(async project =>
        {
            await Task.Delay(40);
            var candidate = windows.AddWindow(100 + windows.Count + 1, 1000 + windows.Count + 1);
            return OperationResult<IdeLaunchReceipt>.Success(
                new IdeLaunchReceipt(candidate.ProcessId, candidate.ProcessStartTimeUtcTicks));
        });
        var service = CreateService(windows, launcher);
        var first = CreateProject();
        var second = CreateProject();

        var results = await Task.WhenAll(
            service.OpenOrActivateAsync(first),
            service.OpenOrActivateAsync(second));

        Assert.All(results, result => Assert.Equal(IdeOperationStatus.LaunchedAndAssociated, result.Status));
        Assert.NotEqual(service.GetAssociatedWindow(first.Id), service.GetAssociatedWindow(second.Id));
        Assert.Equal(1, launcher.MaximumConcurrency);
    }

    [Fact]
    public async Task OpenOrActivateAsync_RequiresManualSelectionWhenProcessIdentityDoesNotMatch()
    {
        var windows = new FakeDesktopWindowService();
        var launcher = new FakeIdeLauncher(project =>
        {
            windows.AddWindow(151, 1501);
            return Task.FromResult(OperationResult<IdeLaunchReceipt>.Success(new IdeLaunchReceipt(9999, 99990)));
        });
        var service = CreateService(windows, launcher);
        var project = CreateProject();

        var result = await service.OpenOrActivateAsync(project);

        Assert.Equal(IdeOperationStatus.Ambiguous, result.Status);
        Assert.Null(service.GetAssociatedWindow(project.Id));
        Assert.Single(result.Candidates!);
    }

    [Fact]
    public async Task CloseAsync_ClosesOnlyAssociatedWindowAndReleasesClaim()
    {
        var windows = new FakeDesktopWindowService();
        var firstCandidate = windows.AddWindow(201, 2001);
        var secondCandidate = windows.AddWindow(202, 2002);
        var service = CreateService(windows, new FakeIdeLauncher());
        var first = CreateProject();
        var second = CreateProject();
        service.AssociateAndActivate(first, firstCandidate);
        service.AssociateAndActivate(second, secondCandidate);

        var result = await service.CloseAsync(first.Id, TimeSpan.FromSeconds(1));

        Assert.Equal(IdeCloseStatus.Closed, result.Status);
        Assert.Equal([firstCandidate.Handle], windows.CloseRequests);
        Assert.Null(service.GetAssociatedWindow(first.Id));
        Assert.Equal(secondCandidate.Handle, service.GetAssociatedWindow(second.Id));
    }

    [Fact]
    public async Task CloseAsync_KeepsAssociationWhenCloseIsCancelledOrTimesOut()
    {
        var windows = new FakeDesktopWindowService
        {
            CloseResult = new WindowCloseResult(WindowCloseStatus.CancelledOrTimedOut, "Still open")
        };
        var candidate = windows.AddWindow(301, 3001);
        var service = CreateService(windows, new FakeIdeLauncher());
        var project = CreateProject();
        service.AssociateAndActivate(project, candidate);

        var result = await service.CloseAsync(project.Id, TimeSpan.FromMilliseconds(10));

        Assert.Equal(IdeCloseStatus.CancelledOrTimedOut, result.Status);
        Assert.Equal(candidate.Handle, service.GetAssociatedWindow(project.Id));
        Assert.Equal(IdeSessionState.Associated, service.GetSession(project.Id, project.PreferredEnvironment).State);
    }

    [Fact]
    public async Task CloseAsync_ReturnsAlreadyClosedForMissingWindow()
    {
        var windows = new FakeDesktopWindowService();
        var candidate = windows.AddWindow(401, 4001);
        var service = CreateService(windows, new FakeIdeLauncher());
        var project = CreateProject();
        service.AssociateAndActivate(project, candidate);
        windows.CloseResult = new WindowCloseResult(WindowCloseStatus.AlreadyClosed, "Already closed");
        windows.RemoveWindow(candidate.Handle);

        var result = await service.CloseAsync(project.Id, TimeSpan.FromMilliseconds(10));

        Assert.Equal(IdeCloseStatus.AlreadyClosed, result.Status);
        Assert.Null(service.GetAssociatedWindow(project.Id));
    }

    [Fact]
    public async Task CloseAsync_ReturnsBusyWhileLaunchIsInProgress()
    {
        var windows = new FakeDesktopWindowService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new FakeIdeLauncher(async project =>
        {
            entered.SetResult();
            await release.Task;
            var candidate = windows.AddWindow(501, 5001);
            return OperationResult<IdeLaunchReceipt>.Success(
                new IdeLaunchReceipt(candidate.ProcessId, candidate.ProcessStartTimeUtcTicks));
        });
        var service = CreateService(windows, launcher);
        var project = CreateProject();

        var opening = service.OpenOrActivateAsync(project);
        await entered.Task;
        var close = await service.CloseAsync(project.Id, TimeSpan.FromMilliseconds(10));
        release.SetResult();
        await opening;

        Assert.Equal(IdeCloseStatus.Busy, close.Status);
    }

    [Fact]
    public void ClearAssociation_ReleasesWindowForAnotherWorkspace()
    {
        var windows = new FakeDesktopWindowService();
        var candidate = windows.AddWindow(601, 6001);
        var service = CreateService(windows, new FakeIdeLauncher());
        var first = CreateProject();
        var second = CreateProject();
        service.AssociateAndActivate(first, candidate);

        service.ClearAssociation(first.Id);
        var result = service.AssociateAndActivate(second, candidate);

        Assert.True(result.IsSuccess);
        Assert.Null(service.GetAssociatedWindow(first.Id));
        Assert.Equal(candidate.Handle, service.GetAssociatedWindow(second.Id));
    }

    [Fact]
    public void EmbeddingTransitions_PreserveAssociationIdentity()
    {
        var windows = new FakeDesktopWindowService();
        var candidate = windows.AddWindow(701, 7001);
        var service = CreateService(windows, new FakeIdeLauncher());
        var project = CreateProject();
        service.AssociateAndActivate(project, candidate);

        service.BeginEmbedding(project.Id);
        Assert.Equal(IdeSessionState.Embedding, service.GetSession(project.Id, project.PreferredEnvironment).State);
        service.SetEmbeddingState(project.Id, true);
        Assert.Equal(IdeSessionState.Embedded, service.GetSession(project.Id, project.PreferredEnvironment).State);
        service.BeginDetaching(project.Id);
        Assert.Equal(IdeSessionState.Detaching, service.GetSession(project.Id, project.PreferredEnvironment).State);
        service.SetEmbeddingState(project.Id, false);

        var snapshot = service.GetSession(project.Id, project.PreferredEnvironment);
        Assert.Equal(IdeSessionState.Associated, snapshot.State);
        Assert.Equal(candidate.Handle, snapshot.WindowHandle);
    }

    [Fact]
    public async Task CloseAsync_PreservesClaimWhenIdentityCannotBeConfirmed()
    {
        var windows = new FakeDesktopWindowService
        {
            CloseResult = new WindowCloseResult(WindowCloseStatus.IdentityMismatch, "Identity changed")
        };
        var candidate = windows.AddWindow(801, 8001);
        var service = CreateService(windows, new FakeIdeLauncher());
        var project = CreateProject();
        service.AssociateAndActivate(project, candidate);

        var result = await service.CloseAsync(project.Id, TimeSpan.FromMilliseconds(10));

        Assert.Equal(IdeCloseStatus.IdentityMismatch, result.Status);
        Assert.Equal(candidate.Handle, service.GetSession(project.Id, project.PreferredEnvironment).WindowHandle);
        Assert.Equal(IdeSessionState.Failed, service.GetSession(project.Id, project.PreferredEnvironment).State);
    }

    [Fact]
    public async Task OpenOrActivateAsync_UsesEnvironmentSnapshotDuringLaunch()
    {
        var windows = new FakeDesktopWindowService();
        DevelopmentEnvironment? launchedEnvironment = null;
        var launcher = new FakeIdeLauncher(project =>
        {
            launchedEnvironment = project.PreferredEnvironment;
            var candidate = windows.AddWindow(901, 9001);
            return Task.FromResult(OperationResult<IdeLaunchReceipt>.Success(
                new IdeLaunchReceipt(candidate.ProcessId, candidate.ProcessStartTimeUtcTicks)));
        });
        var service = CreateService(windows, launcher);
        var project = CreateProject();
        var opening = service.OpenOrActivateAsync(project);
        project.PreferredEnvironment = DevelopmentEnvironment.VisualStudio2022;

        await opening;

        Assert.Equal(DevelopmentEnvironment.VisualStudioCode, launchedEnvironment);
    }

    [Fact]
    public async Task OpenOrActivateAsync_ReturnsResolvedLaunchTargetForPersistence()
    {
        var windows = new FakeDesktopWindowService();
        const string launchTarget = @"C:\work\Product.sln";
        var launcher = new FakeIdeLauncher(project =>
        {
            var candidate = windows.AddWindow(951, 9501);
            return Task.FromResult(OperationResult<IdeLaunchReceipt>.Success(
                new IdeLaunchReceipt(
                    candidate.ProcessId,
                    candidate.ProcessStartTimeUtcTicks,
                    launchTarget)));
        });
        var service = CreateService(windows, launcher);

        var result = await service.OpenOrActivateAsync(CreateProject());

        Assert.Equal(IdeOperationStatus.LaunchedAndAssociated, result.Status);
        Assert.Equal(launchTarget, result.LaunchTargetPath);
    }

    [Fact]
    public async Task OpenOrActivateAsync_WaitsForStableResponsiveVisualStudioWindow()
    {
        var windows = new FakeDesktopWindowService();
        var launcher = new FakeIdeLauncher(project =>
        {
            var candidate = windows.AddWindow(
                991,
                9901,
                DevelopmentEnvironment.VisualStudio2022);
            return Task.FromResult(OperationResult<IdeLaunchReceipt>.Success(
                new IdeLaunchReceipt(candidate.ProcessId, candidate.ProcessStartTimeUtcTicks)));
        });
        var service = CreateService(windows, launcher);
        var project = CreateProject();
        project.PreferredEnvironment = DevelopmentEnvironment.VisualStudio2022;

        var result = await service.OpenOrActivateAsync(project);

        Assert.Equal(IdeOperationStatus.LaunchedAndAssociated, result.Status);
        Assert.True(windows.ResponsivenessChecks >= 30);
    }

    private static IdeWorkspaceService CreateService(FakeDesktopWindowService windows, FakeIdeLauncher launcher) =>
        new(
            new FakeIdeLocator(),
            launcher,
            windows,
            new WindowAssociationResolver(),
            NullLogger<IdeWorkspaceService>.Instance);

    private static ProjectWorkspace CreateProject() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Workspace",
        FolderPath = Environment.CurrentDirectory,
        PreferredEnvironment = DevelopmentEnvironment.VisualStudioCode
    };

    private sealed class FakeIdeLocator : IIdeLocator
    {
        public Task<IdeInstallation?> LocateAsync(
            DevelopmentEnvironment environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdeInstallation?>(new IdeInstallation(environment, "Fake", "fake.exe", "1"));
    }

    private sealed class FakeIdeLauncher : IIdeLauncher
    {
        private readonly Func<ProjectWorkspace, Task<OperationResult<IdeLaunchReceipt>>> _handler;
        private int _concurrency;
        private int _maximumConcurrency;

        public FakeIdeLauncher(Func<ProjectWorkspace, Task<OperationResult<IdeLaunchReceipt>>>? handler = null)
        {
            _handler = handler ?? (_ => Task.FromResult(
                OperationResult<IdeLaunchReceipt>.Success(new IdeLaunchReceipt(1, 10))));
        }

        public int MaximumConcurrency => _maximumConcurrency;

        public async Task<OperationResult<IdeLaunchReceipt>> LaunchAsync(
            ProjectWorkspace project,
            IdeInstallation installation,
            CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _concurrency);
            InterlockedExtensions.Max(ref _maximumConcurrency, current);
            try
            {
                return await _handler(project);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }
    }

    private sealed class FakeDesktopWindowService : IDesktopWindowService
    {
        private readonly ConcurrentDictionary<long, IdeWindowCandidate> _windows = new();

        public int Count => _windows.Count;

        public List<long> CloseRequests { get; } = [];

        public WindowCloseResult CloseResult { get; set; } = new(WindowCloseStatus.Closed, "Closed");

        public bool AllowActivation { get; set; } = true;

        public int ResponsivenessChecks { get; private set; }

        public IdeWindowCandidate AddWindow(
            long handle,
            int processId,
            DevelopmentEnvironment environment = DevelopmentEnvironment.VisualStudioCode)
        {
            var candidate = new IdeWindowCandidate(
                handle,
                processId,
                $"Window {handle}",
                environment,
                processId * 10L);
            _windows[handle] = candidate;
            return candidate;
        }

        public void RemoveWindow(long handle) => _windows.TryRemove(handle, out _);

        public IReadOnlyList<IdeWindowCandidate> EnumerateIdeWindows(DevelopmentEnvironment environment) =>
            _windows.Values.Where(window => window.Environment == environment).OrderBy(window => window.Handle).ToList();

        public bool IsWindow(long handle) => _windows.ContainsKey(handle);

        public bool IsSameWindow(IdeWindowCandidate identity) =>
            _windows.TryGetValue(identity.Handle, out var current) && current == identity;

        public bool IsWindowResponsive(long handle)
        {
            ResponsivenessChecks++;
            return IsWindow(handle);
        }

        public bool TryActivate(long handle) => AllowActivation && IsWindow(handle);

        public Task<WindowCloseResult> RequestCloseAsync(
            IdeWindowCandidate identity,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            CloseRequests.Add(identity.Handle);
            if (CloseResult.Status == WindowCloseStatus.Closed)
            {
                RemoveWindow(identity.Handle);
            }

            return Task.FromResult(CloseResult);
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
