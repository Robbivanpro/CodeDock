using System.ComponentModel;
using System.Diagnostics;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;

namespace CodeDock.Infrastructure.VSCode;

public sealed class WindowsIdeLauncher : IIdeLauncher
{
    public Task<OperationResult> LaunchAsync(
        ProjectWorkspace project,
        IdeInstallation installation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installation.ExecutablePath,
                UseShellExecute = false
            };

            var targetResult = ResolveTarget(project);
            if (!targetResult.IsSuccess || targetResult.Value is null)
            {
                return Task.FromResult(OperationResult.Failure(targetResult.Error ?? "No launch target is available."));
            }

            if (project.PreferredEnvironment == DevelopmentEnvironment.VisualStudioCode)
            {
                startInfo.ArgumentList.Add("--new-window");
            }

            startInfo.ArgumentList.Add(targetResult.Value);
            Process.Start(startInfo);
            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return Task.FromResult(OperationResult.Failure($"The IDE could not be started: {exception.Message}"));
        }
    }

    private static OperationResult<string> ResolveTarget(ProjectWorkspace project)
    {
        if (project.PreferredEnvironment == DevelopmentEnvironment.VisualStudioCode)
        {
            var target = !string.IsNullOrWhiteSpace(project.WorkspaceFilePath) && File.Exists(project.WorkspaceFilePath)
                ? project.WorkspaceFilePath
                : project.FolderPath;
            return OperationResult<string>.Success(target);
        }

        if (!string.IsNullOrWhiteSpace(project.WorkspaceFilePath)
            && File.Exists(project.WorkspaceFilePath)
            && IsVisualStudioSolution(project.WorkspaceFilePath))
        {
            return OperationResult<string>.Success(project.WorkspaceFilePath);
        }

        var solutions = Directory.Exists(project.FolderPath)
            ? Directory.EnumerateFiles(project.FolderPath, "*.sln", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(project.FolderPath, "*.slnx", SearchOption.TopDirectoryOnly))
                .Take(2)
                .ToList()
            : [];

        return solutions.Count switch
        {
            1 => OperationResult<string>.Success(solutions[0]),
            0 => OperationResult<string>.Failure("Visual Studio requires a .sln or .slnx file in the project root."),
            _ => OperationResult<string>.Failure("Multiple Visual Studio solutions were found. Configure the workspace file explicitly.")
        };
    }

    private static bool IsVisualStudioSolution(string path) =>
        string.Equals(Path.GetExtension(path), ".sln", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(path), ".slnx", StringComparison.OrdinalIgnoreCase);
}
