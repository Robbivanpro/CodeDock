using CodeDock.Core.Models;

namespace CodeDock.Core.Interfaces;

public interface IProjectPathValidator
{
    OperationResult<string> ValidateFolder(string folderPath);
}
