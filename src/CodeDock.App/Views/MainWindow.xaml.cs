using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodeDock.App.ViewModels;
using CodeDock.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodeDock.App.Views;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private bool _shutdownCompleted;

    public MainWindow(ILogger<MainWindow> logger)
    {
        _logger = logger;
        InitializeComponent();
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        IsEnabled = false;
        try
        {
            await ViewModel.ShutdownAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save all CodeDock data during shutdown");
            MessageBox.Show(
                $"CodeDock could not save all changes.\n\n{exception.Message}",
                "CodeDock save error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _shutdownCompleted = true;
            Close();
        }
    }

    private async void OnProjectNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: ProjectTabViewModel project })
        {
            await ViewModel.CommitProjectNameAsync(project);
        }
    }

    private void OnProjectNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && sender is TextBox { DataContext: ProjectTabViewModel project })
        {
            project.RestoreName();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnTabPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed
            && FindVisualParent<Button>(e.OriginalSource as DependencyObject) is null
            && sender is ListBoxItem { DataContext: ProjectTabViewModel project })
        {
            DragDrop.DoDragDrop((DependencyObject)sender, project, DragDropEffects.Move);
        }
    }

    private async void OnRemoveProjectClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ProjectTabViewModel project })
        {
            await ViewModel.RemoveProjectCommand.ExecuteAsync(project);
            e.Handled = true;
        }
    }

    private async void OnTabDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: ProjectTabViewModel target }
            || e.Data.GetData(typeof(ProjectTabViewModel)) is not ProjectTabViewModel project)
        {
            return;
        }

        await ViewModel.MoveProjectAsync(project, target);
        e.Handled = true;
    }

    private async void OnSelectVSCodeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ProjectTabViewModel project })
        {
            project.PreferredEnvironment = DevelopmentEnvironment.VisualStudioCode;
            await ViewModel.SetPreferredEnvironmentAsync(project);
        }
    }

    private async void OnSelectVisualStudioClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ProjectTabViewModel project })
        {
            project.PreferredEnvironment = DevelopmentEnvironment.VisualStudio2022;
            await ViewModel.SetPreferredEnvironmentAsync(project);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ViewModel.SelectNextProject(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
