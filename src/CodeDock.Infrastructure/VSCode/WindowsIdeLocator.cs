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
        return Task.FromResult(environment == DevelopmentEnvironment.VisualStudioCode
            ? LocateVisualStudioCode()
            : LocateVisualStudio());
    }

    private static IdeInstallation? LocateVisualStudioCode()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe")
        };

        var executable = candidates.FirstOrDefault(File.Exists);
        return executable is null
            ? null
            : new IdeInstallation(
                DevelopmentEnvironment.VisualStudioCode,
                "Visual Studio Code",
                executable,
                FileVersionInfo.GetVersionInfo(executable).ProductVersion);
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
