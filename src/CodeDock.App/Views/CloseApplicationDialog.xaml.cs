using System.Windows;
using CodeDock.App.Services;

namespace CodeDock.App.Views;

public partial class CloseApplicationDialog : Window
{
    public CloseApplicationDialog(int openIdeCount)
    {
        InitializeComponent();
        DescriptionText.Text = openIdeCount == 1
            ? "One IDE window is still open. You can leave it open on the desktop or ask it to close before CodeDock exits. Unsaved changes will be handled by the IDE."
            : $"{openIdeCount} IDE windows are still open. You can leave them open on the desktop or ask them to close before CodeDock exits. Unsaved changes will be handled by each IDE.";
    }

    public ApplicationCloseChoice Choice { get; private set; } = ApplicationCloseChoice.Cancel;

    private void OnLeaveIdesOpenClick(object sender, RoutedEventArgs e)
    {
        Choice = ApplicationCloseChoice.LeaveIdesOpen;
        DialogResult = true;
    }

    private void OnCloseIdesClick(object sender, RoutedEventArgs e)
    {
        Choice = ApplicationCloseChoice.CloseIdes;
        DialogResult = true;
    }
}
