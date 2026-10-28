using System.ComponentModel;
using System.Diagnostics;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;

namespace CodeDock.Infrastructure.VSCode;

public sealed class WindowsIdeLauncher : IIdeLauncher
{
    public Task<OperationResult<IdeLaunchReceipt>> LaunchAsync(
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
                return Task.FromResult(OperationResult<IdeLaunchReceipt>.Failure(targetResult.Error ?? "No launch target is available."));
            }

            if (project.PreferredEnvironment == DevelopmentEnvironment.VisualStudioCode)
            {
                startInfo.ArgumentList.Add("--new-window");
            }

            startInfo.ArgumentList.Add(targetResult.Value);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Task.FromResult(OperationResult<IdeLaunchReceipt>.Failure("Windows did not create an IDE process."));
            }

            long processStartTimeUtcTicks;
            try
            {
                processStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (InvalidOperationException)
            {
                processStartTimeUtcTicks = 0;
            }

            return Task.FromResult(OperationResult<IdeLaunchReceipt>.Success(
                new IdeLaunchReceipt(process.Id, processStartTimeUtcTicks)));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return Task.FromResult(OperationResult<IdeLaunchReceipt>.Failure($"The IDE could not be started: {exception.Message}"));
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

        var solutions = FindNearestSolutions(project.FolderPath);

        return solutions.Count switch
        {
            1 => OperationResult<string>.Success(solutions[0]),
            0 => OperationResult<string>.Failure(
                "Visual Studio requires a .sln or .slnx file in the workspace, a nearby parent folder, or a subfolder."),
            _ => OperationResult<string>.Failure(
                $"Multiple Visual Studio solutions were found near this workspace: {string.Join(", ", solutions.Select(Path.GetFileName))}.")
        };
    }

    private static IReadOnlyList<string> FindNearestSolutions(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        var workspaceDirectory = new DirectoryInfo(folderPath);
        var directory = workspaceDirectory;
        for (var depth = 0; depth <= 3 && directory is not null; depth++)
        {
            var solutions = EnumerateSolutions(directory.FullName);
            if (solutions.Count > 0)
            {
                return solutions;
            }

            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                break;
            }

            directory = directory.Parent;
        }

        return FindSolutionsInSubfolders(workspaceDirectory);
    }

    private static IReadOnlyList<string> FindSolutionsInSubfolders(DirectoryInfo workspaceDirectory)
    {
        var ignoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", "bin", "obj", "node_modules", "packages"
        };
        var pending = new Queue<(DirectoryInfo Directory, int Depth)>();
        pending.Enqueue((workspaceDirectory, 0));
        var nearestDepth = int.MaxValue;
        var solutions = new List<string>();

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Dequeue();
            if (depth > 3 || depth > nearestDepth)
            {
                continue;
            }

            var currentSolutions = EnumerateSolutions(current.FullName);
            if (currentSolutions.Count > 0)
            {
                nearestDepth = depth;
                solutions.AddRange(currentSolutions);
                if (solutions.Count >= 3)
                {
                    break;
                }

                continue;
            }

            if (depth == 3)
            {
                continue;
            }

            try
            {
                foreach (var child in current.EnumerateDirectories())
                {
                    if (!ignoredDirectories.Contains(child.Name)
                        && !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        pending.Enqueue((child, depth + 1));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An inaccessible generated folder must not prevent the IDE launch.
            }
        }

        return solutions;
    }

    private static IReadOnlyList<string> EnumerateSolutions(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.sln", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsVisualStudioSolution(string path) =>
        string.Equals(Path.GetExtension(path), ".sln", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(path), ".slnx", StringComparison.OrdinalIgnoreCase);
}
