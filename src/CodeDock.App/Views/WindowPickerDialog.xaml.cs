using System.Collections.ObjectModel;
using System.Windows;
using CodeDock.Core.Models;

namespace CodeDock.App.Views;

public partial class WindowPickerDialog : Window
{
    public WindowPickerDialog(IReadOnlyList<IdeWindowCandidate> candidates)
    {
        Candidates = new ObservableCollection<IdeWindowCandidate>(candidates);
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<IdeWindowCandidate> Candidates { get; }

    public IdeWindowCandidate? SelectedCandidate { get; set; }

    private void OnAssociateClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidate is null)
        {
            MessageBox.Show(this, "Select an IDE window first.", "CodeDock", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
