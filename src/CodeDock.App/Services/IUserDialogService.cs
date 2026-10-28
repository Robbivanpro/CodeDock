namespace CodeDock.App.Services;

using CodeDock.Core.Models;

public interface IUserDialogService
{
    bool ConfirmRemove(string projectName);

    void ShowWarning(string message);

    IdeWindowCandidate? ChooseIdeWindow(IReadOnlyList<IdeWindowCandidate> candidates);
}
