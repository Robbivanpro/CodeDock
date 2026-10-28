using System.Windows;
using CodeDock.App.Views;
using CodeDock.Core.Models;

namespace CodeDock.App.Services;

public sealed class UserDialogService : IUserDialogService
{
    public bool ConfirmRemove(string projectName) => MessageBox.Show(
        $"Close only the '{projectName}' tab?\n\nThe associated IDE window will remain open.",
        "Close tab only",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool ConfirmCloseIdeAndTab(string projectName) => MessageBox.Show(
        $"Close the IDE window associated with '{projectName}' and then remove its tab?\n\nUnsaved changes will be handled by the IDE.",
        "Close IDE and tab",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowWarning(string message) => MessageBox.Show(
        message,
        "CodeDock",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);

    public IdeWindowCandidate? ChooseIdeWindow(IReadOnlyList<IdeWindowCandidate> candidates)
    {
        var dialog = new WindowPickerDialog(candidates)
        {
            Owner = Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.SelectedCandidate : null;
    }
}
