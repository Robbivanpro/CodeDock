using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodeDock.App.ViewModels;
using CodeDock.App.Controls;
using CodeDock.App.Services;
using CodeDock.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodeDock.App.Views;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly IUserDialogService _dialogService;
    private bool _shutdownCompleted;

    public MainWindow(ILogger<MainWindow> logger, IUserDialogService dialogService)
    {
        _logger = logger;
        _dialogService = dialogService;
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
        if (!TryDetachAllEmbedded(out var detachError))
        {
            MessageBox.Show(
                this,
                detachError,
                "CodeDock detach error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

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
            if (project.IsIdeBusy || ViewModel.IsBusy)
            {
                return;
            }

            var closeIde = project.HasAssociatedWindow;
            _logger.LogInformation(
                "Workspace {WorkspaceId} tab close requested; close IDE {CloseIde}; embedded {Embedded}",
                project.Id,
                closeIde,
                project.IsEmbedded);
            var confirmed = closeIde
                ? _dialogService.ConfirmCloseIdeAndTab(project.Name)
                : _dialogService.ConfirmRemove(project.Name);
            if (!confirmed)
            {
                return;
            }

            var wasEmbedded = project.IsEmbedded;
            if (!TryDetachProject(project, out var error))
            {
                MessageBox.Show(this, error, "CodeDock detach error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ViewModel.SetEmbeddedState(project, false);
            var removed = closeIde
                ? await ViewModel.CloseProjectAndIdeAsync(project)
                : await ViewModel.RemoveTabOnlyAsync(project);
            if (!removed && wasEmbedded && project.HasAssociatedWindow)
            {
                ViewModel.SetEmbeddedState(project, true);
            }

            e.Handled = true;
        }
    }

    private async void OnCloseTabOnlyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ProjectTabViewModel project }
            || project.IsIdeBusy
            || ViewModel.IsBusy
            || !_dialogService.ConfirmRemove(project.Name))
        {
            return;
        }

        if (!TryDetachProject(project, out var error))
        {
            MessageBox.Show(this, error, "CodeDock detach error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ViewModel.SetEmbeddedState(project, false);
        await ViewModel.RemoveTabOnlyAsync(project);
        e.Handled = true;
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

    private async void OnEmbedIdeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProjectTabViewModel project })
        {
            return;
        }

        if (project.WindowHandle is null)
        {
            MessageBox.Show(
                this,
                "Open or associate the IDE window before enabling experimental embedding.",
                "CodeDock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ViewModel.BeginEmbedding(project);
        project.IsEmbedded = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var host = FindProjectHost(project);
        if (host is null || !host.IsAttached)
        {
            project.IsEmbedded = false;
            ViewModel.SetEmbeddedState(project, false);
            MessageBox.Show(
                this,
                host?.LastError ?? "The IDE window could not be attached to its workspace host.",
                "CodeDock embedding error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _logger.LogWarning(
                "Workspace {WorkspaceId} embedding failed: {Error}",
                project.Id,
                host?.LastError ?? "Host was not created or attached");
            return;
        }

        ViewModel.SetEmbeddedState(project, true);
        _logger.LogInformation("Workspace {WorkspaceId} embedded successfully", project.Id);
    }

    private void OnExitEmbeddedClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { SelectedProject: { } project })
        {
            if (!TryDetachProject(project, out var error))
            {
                MessageBox.Show(this, error, "CodeDock detach error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ViewModel.SetEmbeddedState(project, false);
        }
    }

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

    private bool TryDetachAllEmbedded(out string? error)
    {
        foreach (var project in ViewModel.Projects.Where(project => project.IsEmbedded))
        {
            if (!TryDetachProject(project, out error))
            {
                return false;
            }

            ViewModel.SetEmbeddedState(project, false);
        }

        error = null;
        return true;
    }

    private bool TryDetachProject(ProjectTabViewModel project, out string? error)
    {
        var host = FindProjectHost(project);
        if (host is null || !host.IsAttached)
        {
            error = null;
            return true;
        }

        ViewModel.BeginDetaching(project);
        var detached = host.TryDetach(out error);
        if (detached)
        {
            _logger.LogInformation("Workspace {WorkspaceId} detached successfully", project.Id);
        }
        else
        {
            _logger.LogWarning("Workspace {WorkspaceId} detach failed: {Error}", project.Id, error);
            ViewModel.SetEmbeddedState(project, true);
        }

        return detached;
    }

    private ExternalWindowHost? FindProjectHost(ProjectTabViewModel project) =>
        FindVisualChildren<ExternalWindowHost>(EmbeddedHosts)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, project));

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
