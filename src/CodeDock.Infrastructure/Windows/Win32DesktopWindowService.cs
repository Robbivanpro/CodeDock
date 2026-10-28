using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;

namespace CodeDock.Infrastructure.Windows;

public sealed class Win32DesktopWindowService : IDesktopWindowService
{
    private const int SwRestore = 9;
    private const uint GwOwner = 4;

    public IReadOnlyList<IdeWindowCandidate> EnumerateIdeWindows(DevelopmentEnvironment environment)
    {
        var candidates = new List<IdeWindowCandidate>();
        var expectedProcessName = environment == DevelopmentEnvironment.VisualStudioCode ? "Code" : "devenv";

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle)
                || NativeMethods.GetWindow(handle, GwOwner) != IntPtr.Zero)
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
                    environment));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
            }

            return true;
        }, IntPtr.Zero);

        return candidates;
    }

    public bool IsWindow(long handle) => handle != 0 && NativeMethods.IsWindow(new IntPtr(handle));

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

        if (!NativeMethods.SetForegroundWindow(window))
        {
            return false;
        }

        return NativeMethods.GetForegroundWindow() == window;
    }

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
    }
}
