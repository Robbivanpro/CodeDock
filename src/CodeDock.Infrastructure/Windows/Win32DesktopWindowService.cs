using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodeDock.Infrastructure.Windows;

public sealed class Win32DesktopWindowService : IDesktopWindowService
{
    private const int SwRestore = 9;
    private const uint GwOwner = 4;
    private const uint WmClose = 0x0010;
    private const uint SmtoAbortIfHung = 0x0002;
    private readonly ILogger<Win32DesktopWindowService> _logger;

    public Win32DesktopWindowService(ILogger<Win32DesktopWindowService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<IdeWindowCandidate> EnumerateIdeWindows(DevelopmentEnvironment environment)
    {
        var candidates = new List<IdeWindowCandidate>();
        var expectedProcessName = GetExpectedProcessName(environment);

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.GetWindow(handle, GwOwner) != IntPtr.Zero)
            {
                return true;
            }

            var titleLength = NativeMethods.GetWindowTextLength(handle);
            if (titleLength <= 0)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            try
            {
                using var process = Process.GetProcessById(unchecked((int)processId));
                if (!string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var title = new StringBuilder(titleLength + 1);
                NativeMethods.GetWindowText(handle, title, title.Capacity);
                candidates.Add(new IdeWindowCandidate(
                    handle.ToInt64(),
                    unchecked((int)processId),
                    title.ToString(),
                    environment,
                    process.StartTime.ToUniversalTime().Ticks));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                _logger.LogDebug(exception, "Window {WindowHandle} disappeared during IDE enumeration", handle.ToInt64());
            }

            return true;
        }, IntPtr.Zero);

        return candidates;
    }

    public bool IsWindow(long handle) => handle != 0 && NativeMethods.IsWindow(new IntPtr(handle));

    public bool IsSameWindow(IdeWindowCandidate identity)
    {
        var window = new IntPtr(identity.Handle);
        if (!NativeMethods.IsWindow(window))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId != unchecked((uint)identity.ProcessId))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            return string.Equals(process.ProcessName, GetExpectedProcessName(identity.Environment), StringComparison.OrdinalIgnoreCase)
                && (identity.ProcessStartTimeUtcTicks == 0
                    || process.StartTime.ToUniversalTime().Ticks == identity.ProcessStartTimeUtcTicks);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            _logger.LogDebug(exception, "Could not validate IDE window {WindowHandle}", identity.Handle);
            return false;
        }
    }

    public bool TryActivate(long handle)
    {
        var window = new IntPtr(handle);
        if (!NativeMethods.IsWindow(window))
        {
            return false;
        }

        if (NativeMethods.IsIconic(window))
        {
            NativeMethods.ShowWindow(window, SwRestore);
        }

        return NativeMethods.SetForegroundWindow(window) && NativeMethods.GetForegroundWindow() == window;
    }

    public async Task<WindowCloseResult> RequestCloseAsync(
        IdeWindowCandidate identity,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsWindow(identity.Handle))
        {
            return new WindowCloseResult(WindowCloseStatus.AlreadyClosed, "The IDE window was already closed.");
        }

        if (!IsSameWindow(identity))
        {
            return new WindowCloseResult(WindowCloseStatus.IdentityMismatch, "The window handle no longer belongs to the associated IDE process.");
        }

        var sent = NativeMethods.SendMessageTimeout(
            new IntPtr(identity.Handle),
            WmClose,
            IntPtr.Zero,
            IntPtr.Zero,
            SmtoAbortIfHung,
            2000,
            out _);
        if (sent == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogWarning("WM_CLOSE failed for HWND {WindowHandle} with Win32 error {Error}", identity.Handle, error);
            return new WindowCloseResult(WindowCloseStatus.SendFailed, $"Windows could not deliver the close request. Error {error}.");
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            if (!IsWindow(identity.Handle))
            {
                return new WindowCloseResult(WindowCloseStatus.Closed, "The IDE window closed successfully.");
            }

            if (!IsSameWindow(identity))
            {
                return new WindowCloseResult(WindowCloseStatus.IdentityMismatch, "The original IDE window closed, but its handle was reused before confirmation.");
            }
        }

        return new WindowCloseResult(
            WindowCloseStatus.CancelledOrTimedOut,
            "The IDE is still open. A save dialog may be active or the close request may have been cancelled.");
    }

    private static string GetExpectedProcessName(DevelopmentEnvironment environment) =>
        environment == DevelopmentEnvironment.VisualStudioCode ? "Code" : "devenv";

    private static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr windowHandle);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SendMessageTimeout(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeoutMilliseconds,
            out IntPtr result);
    }
}
