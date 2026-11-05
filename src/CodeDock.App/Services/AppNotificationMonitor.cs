using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CodeDock.App.ViewModels;
using CodeDock.Core.Models;

namespace CodeDock.App.Services;

public sealed partial class AppNotificationMonitor
{
    private readonly string _walFilePath;
    private DateTime _lastWalWriteTimeUtc;
    private byte[] _previousWalSnapshot = [];
    private bool _walInitialized;

    public AppNotificationMonitor()
    {
        _walFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Windows",
            "Notifications",
            "wpndatabase.db-wal");
        InitializeWalBaseline();
    }

    public bool PollNotifications(IEnumerable<ProjectTabViewModel> appWorkspaces)
    {
        var anyChanged = false;
        var workspaceList = appWorkspaces.ToList();
        var newlyNotifiedEnvironments = CheckWalForNewNotifications();

        foreach (var app in workspaceList)
        {
            if (app.IsSelected)
            {
                if (app.HasUnreadNotification)
                {
                    app.HasUnreadNotification = false;
                    anyChanged = true;
                }

                if (app.WindowHandle is { } selectedHandle)
                {
                    var currentTitle = GetWindowTitle(selectedHandle);
                    if (!string.IsNullOrWhiteSpace(currentTitle))
                    {
                        app.LastKnownWindowTitle = currentTitle;
                    }
                }

                continue;
            }

            if (newlyNotifiedEnvironments.Contains(app.PreferredEnvironment) && !app.HasUnreadNotification)
            {
                app.HasUnreadNotification = true;
                anyChanged = true;
            }

            if (app.WindowHandle is not { } handle)
            {
                continue;
            }

            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (HasUnreadCountInTitle(title))
            {
                if (!app.HasUnreadNotification)
                {
                    app.HasUnreadNotification = true;
                    anyChanged = true;
                }
            }
            else if (app.LastKnownWindowTitle is not null
                && !string.Equals(app.LastKnownWindowTitle, title, StringComparison.Ordinal)
                && app.PreferredEnvironment is DevelopmentEnvironment.MicrosoftTeams
                    or DevelopmentEnvironment.MicrosoftOutlook
                    or DevelopmentEnvironment.WhatsApp)
            {
                if (!app.HasUnreadNotification)
                {
                    app.HasUnreadNotification = true;
                    anyChanged = true;
                }
            }

            app.LastKnownWindowTitle = title;
        }

        return anyChanged;
    }

    private void InitializeWalBaseline()
    {
        try
        {
            if (!File.Exists(_walFilePath))
            {
                _walInitialized = true;
                return;
            }

            _lastWalWriteTimeUtc = File.GetLastWriteTimeUtc(_walFilePath);
            _previousWalSnapshot = ReadWalBytes();
            _walInitialized = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _walInitialized = true;
        }
    }

    private HashSet<DevelopmentEnvironment> CheckWalForNewNotifications()
    {
        var triggered = new HashSet<DevelopmentEnvironment>();
        if (!_walInitialized || !File.Exists(_walFilePath))
        {
            return triggered;
        }

        try
        {
            var writeTimeUtc = File.GetLastWriteTimeUtc(_walFilePath);
            if (writeTimeUtc <= _lastWalWriteTimeUtc)
            {
                return triggered;
            }

            _lastWalWriteTimeUtc = writeTimeUtc;
            var currentBytes = ReadWalBytes();
            if (currentBytes.Length == 0)
            {
                return triggered;
            }

            var diffBuilder = new StringBuilder();
            const int blockSize = 4096;
            for (var offset = 0; offset < currentBytes.Length; offset += blockSize)
            {
                var length = Math.Min(blockSize, currentBytes.Length - offset);
                if (!BlockEquals(currentBytes, _previousWalSnapshot, offset, length))
                {
                    diffBuilder.Append(Encoding.UTF8.GetString(currentBytes, offset, length));
                }
            }

            _previousWalSnapshot = currentBytes;
            var diffText = diffBuilder.ToString();
            if (string.IsNullOrEmpty(diffText))
            {
                return triggered;
            }

            if (diffText.Contains("ms-teams", StringComparison.OrdinalIgnoreCase)
                || diffText.Contains("MSTeams", StringComparison.OrdinalIgnoreCase))
            {
                triggered.Add(DevelopmentEnvironment.MicrosoftTeams);
            }

            if (diffText.Contains("olk.exe", StringComparison.OrdinalIgnoreCase)
                || diffText.Contains("OutlookForWindows", StringComparison.OrdinalIgnoreCase)
                || diffText.Contains("OUTLOOK.EXE", StringComparison.OrdinalIgnoreCase))
            {
                triggered.Add(DevelopmentEnvironment.MicrosoftOutlook);
            }

            if (diffText.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)
                || diffText.Contains("5319275A", StringComparison.OrdinalIgnoreCase))
            {
                triggered.Add(DevelopmentEnvironment.WhatsApp);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Ignore transient notification DB lock contention.
        }

        return triggered;
    }

    private byte[] ReadWalBytes()
    {
        using var stream = new FileStream(
            _walFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[stream.Length];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read <= 0)
            {
                break;
            }

            totalRead += read;
        }

        return buffer;
    }

    private static bool BlockEquals(byte[] current, byte[] previous, int offset, int length)
    {
        if (offset + length > previous.Length)
        {
            return false;
        }

        return current.AsSpan(offset, length).SequenceEqual(previous.AsSpan(offset, length));
    }

    private static bool HasUnreadCountInTitle(string title) =>
        UnreadBadgeRegex().IsMatch(title);

    private static string? GetWindowTitle(long handle)
    {
        var hwnd = new IntPtr(handle);
        if (!NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return null;
        }

        var builder = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    [GeneratedRegex(@"(?:^|\s)[\(\[][1-9]\d*[\)\]]")]
    private static partial Regex UnreadBadgeRegex();

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr windowHandle);
    }
}
