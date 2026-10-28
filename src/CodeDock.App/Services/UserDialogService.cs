using System.Windows;

namespace CodeDock.App.Services;

public sealed class UserDialogService : IUserDialogService
{
    public bool ConfirmRemove(string projectName) => MessageBox.Show(
        $"Remove '{projectName}' from CodeDock?\n\nThe project folder and any VS Code window will not be changed.",
        "Remove project",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowWarning(string message) => MessageBox.Show(
        message,
        "CodeDock",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
}
