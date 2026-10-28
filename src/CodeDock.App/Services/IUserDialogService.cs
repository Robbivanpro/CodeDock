namespace CodeDock.App.Services;

public interface IUserDialogService
{
    bool ConfirmRemove(string projectName);

    void ShowWarning(string message);
}
