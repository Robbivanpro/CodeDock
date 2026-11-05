namespace CodeDock.App.Services;

using CodeDock.Core.Models;

public interface IUserDialogService
{
    bool ConfirmRemove(string projectName);

    bool ConfirmCloseIdeAndTab(string projectName);

    ApplicationCloseChoice ChooseApplicationCloseBehavior(int openIdeCount);

    void ShowWarning(string message);

    IdeWindowCandidate? ChooseIdeWindow(IReadOnlyList<IdeWindowCandidate> candidates);

    string? ChooseVisualStudioSolution(string workspaceFolder, IReadOnlyList<string> solutions);
}
