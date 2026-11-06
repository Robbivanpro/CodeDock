using System.Diagnostics;
using System.Text.Json;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;

namespace CodeDock.Infrastructure.VSCode;

public sealed class WindowsIdeLocator : IIdeLocator
{
    public Task<IdeInstallation?> LocateAsync(
        DevelopmentEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(environment switch
        {
            DevelopmentEnvironment.VisualStudioCode => LocateVisualStudioCode(),
            DevelopmentEnvironment.VisualStudio2022 => LocateVisualStudio(),
            DevelopmentEnvironment.Antigravity => LocateAntigravity(),
            DevelopmentEnvironment.MicrosoftTeams => LocateMicrosoftTeams(),
            DevelopmentEnvironment.MicrosoftOutlook => LocateMicrosoftOutlook(),
            DevelopmentEnvironment.WhatsApp => LocateWhatsApp(),
            DevelopmentEnvironment.Spotify => LocateSpotify(),
            DevelopmentEnvironment.Obsidian => LocateObsidian(),
            DevelopmentEnvironment.MicrosoftEdge => LocateMicrosoftEdge(),
            DevelopmentEnvironment.GoogleChrome => LocateGoogleChrome(),
            _ => null
        });
    }

    private static IdeInstallation? LocateVisualStudioCode() =>
        LocateFromCandidates(
            DevelopmentEnvironment.VisualStudioCode,
            "Visual Studio Code",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe")
            ]);

    private static IdeInstallation? LocateAntigravity() =>
        LocateFromCandidates(
            DevelopmentEnvironment.Antigravity,
            "Antigravity",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "antigravity", "Antigravity.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Antigravity", "Antigravity.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Antigravity", "Antigravity.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Antigravity", "Antigravity.exe")
            ]);

    private static IdeInstallation? LocateMicrosoftTeams() =>
        LocateFromCandidates(
            DevelopmentEnvironment.MicrosoftTeams,
            "Microsoft Teams",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "ms-teams.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Teams", "current", "Teams.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Teams", "current", "Teams.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Teams", "current", "Teams.exe")
            ]);

    private static IdeInstallation? LocateMicrosoftOutlook()
    {
        var fileCandidate = LocateFromCandidates(
            DevelopmentEnvironment.MicrosoftOutlook,
            "Microsoft Outlook",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "olk.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Office", "root", "Office16", "OUTLOOK.EXE"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Office", "root", "Office16", "OUTLOOK.EXE")
            ]);
        if (fileCandidate is not null)
        {
            return fileCandidate;
        }

        var appxPackageFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages",
            "Microsoft.OutlookForWindows_8wekyb3d8bbwe");
        return Directory.Exists(appxPackageFolder)
            ? new IdeInstallation(
                DevelopmentEnvironment.MicrosoftOutlook,
                "Microsoft Outlook",
                @"shell:AppsFolder\Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookForWindows",
                null)
            : null;
    }

    private static IdeInstallation? LocateWhatsApp()
    {
        var fileCandidate = LocateFromCandidates(
            DevelopmentEnvironment.WhatsApp,
            "WhatsApp",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "WhatsApp.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhatsApp", "WhatsApp.exe")
            ]);
        if (fileCandidate is not null)
        {
            return fileCandidate;
        }

        var appxPackageFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages",
            "5319275A.WhatsAppDesktop_cv1g1gvanyjgm");
        return Directory.Exists(appxPackageFolder)
            ? new IdeInstallation(
                DevelopmentEnvironment.WhatsApp,
                "WhatsApp",
                @"shell:AppsFolder\5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App",
                null)
            : null;
    }

    private static IdeInstallation? LocateSpotify() =>
        LocateFromCandidates(
            DevelopmentEnvironment.Spotify,
            "Spotify",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "Spotify.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Spotify", "Spotify.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Spotify", "Spotify.exe")
            ]);

    private static IdeInstallation? LocateObsidian() =>
        LocateFromCandidates(
            DevelopmentEnvironment.Obsidian,
            "Obsidian",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "obsidian", "Obsidian.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Obsidian", "Obsidian.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Obsidian", "Obsidian.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Obsidian", "Obsidian.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Obsidian", "Obsidian.exe")
            ]);

    private static IdeInstallation? LocateMicrosoftEdge() =>
        LocateFromCandidates(
            DevelopmentEnvironment.MicrosoftEdge,
            "Microsoft Edge",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
            ]);

    private static IdeInstallation? LocateGoogleChrome() =>
        LocateFromCandidates(
            DevelopmentEnvironment.GoogleChrome,
            "Google Chrome",
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
            ]);

    private static IdeInstallation? LocateFromCandidates(
        DevelopmentEnvironment environment,
        string displayName,
        IEnumerable<string> candidates)
    {
        var executable = candidates.FirstOrDefault(File.Exists);
        return executable is null
            ? null
            : new IdeInstallation(
                environment,
                displayName,
                executable,
                TryGetProductVersion(executable));
    }

    private static string? TryGetProductVersion(string executable)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(executable).ProductVersion;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static IdeInstallation? LocateVisualStudio()
    {
        var installerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");

        var fromSetup = File.Exists(installerPath) ? LocateWithVsWhere(installerPath) : null;
        if (fromSetup is not null)
        {
            return fromSetup;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[] { "Enterprise", "Professional", "Community", "BuildTools" }
            .Select(edition => Path.Combine(programFiles, "Microsoft Visual Studio", "2022", edition, "Common7", "IDE", "devenv.exe"));
        var executable = candidates.FirstOrDefault(File.Exists);
        return executable is null
            ? null
            : new IdeInstallation(
                DevelopmentEnvironment.VisualStudio2022,
                "Visual Studio 2022",
                executable,
                FileVersionInfo.GetVersionInfo(executable).ProductVersion);
    }

    private static IdeInstallation? LocateWithVsWhere(string vsWherePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = vsWherePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-latest");
            startInfo.ArgumentList.Add("-products");
            startInfo.ArgumentList.Add("*");
            startInfo.ArgumentList.Add("-version");
            startInfo.ArgumentList.Add("[17.0,18.0)");
            startInfo.ArgumentList.Add("-requires");
            startInfo.ArgumentList.Add("Microsoft.Component.MSBuild");
            startInfo.ArgumentList.Add("-format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("-utf8");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                return null;
            }

            using var document = JsonDocument.Parse(output);
            var instance = document.RootElement.EnumerateArray().FirstOrDefault();
            if (instance.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            var executable = instance.GetProperty("productPath").GetString();
            if (executable is null || !File.Exists(executable))
            {
                return null;
            }

            return new IdeInstallation(
                DevelopmentEnvironment.VisualStudio2022,
                instance.GetProperty("displayName").GetString() ?? "Visual Studio 2022",
                executable,
                instance.GetProperty("installationVersion").GetString());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }
}
