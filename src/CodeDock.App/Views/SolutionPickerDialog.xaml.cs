using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace CodeDock.App.Views;

public partial class SolutionPickerDialog : Window
{
    public SolutionPickerDialog(string workspaceFolder, IReadOnlyList<string> solutions)
    {
        Solutions = new ObservableCollection<SolutionChoice>(solutions.Select(path => new SolutionChoice(
            Path.GetFileName(path),
            Path.GetRelativePath(workspaceFolder, path),
            path)));
        SelectedSolution = Solutions.FirstOrDefault();
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<SolutionChoice> Solutions { get; }

    public SolutionChoice? SelectedSolution { get; set; }

    private void OnUseSolutionClick(object sender, RoutedEventArgs e) => AcceptSelection();

    private void OnSolutionDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
        if (SelectedSolution is null)
        {
            MessageBox.Show(this, "Select a solution first.", "CodeDock", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    public sealed record SolutionChoice(string Name, string RelativePath, string FullPath);
}
