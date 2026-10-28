using CodeDock.Core.Interfaces;
using CodeDock.Core.Models;

namespace CodeDock.Core.Services;

public sealed class ProjectPathValidator : IProjectPathValidator
{
    public OperationResult<string> ValidateFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return OperationResult<string>.Failure("Select a project folder.");
        }

        try
        {
            var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath.Trim()));
            return Directory.Exists(normalizedPath)
                ? OperationResult<string>.Success(normalizedPath)
                : OperationResult<string>.Failure("The selected folder is no longer available.");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<string>.Failure("The selected path is not valid.");
        }
    }
}
