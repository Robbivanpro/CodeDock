using System.IO;
using System.Windows;
using CodeDock.App.Services;
using CodeDock.App.ViewModels;
using CodeDock.App.Views;
using CodeDock.Core.Interfaces;
using CodeDock.Core.Services;
using CodeDock.Infrastructure.Persistence;
using CodeDock.Infrastructure.VSCode;
using CodeDock.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodeDock.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = ConfigureServices();

        try
        {
            var viewModel = _services.GetRequiredService<MainWindowViewModel>();
            await viewModel.InitializeAsync();
            var window = _services.GetRequiredService<MainWindow>();
            window.DataContext = viewModel;
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            _services.GetRequiredService<ILogger<App>>().LogCritical(exception, "CodeDock failed to start");
            MessageBox.Show(
                $"CodeDock could not start.\n\n{exception.Message}",
                "CodeDock startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodeDock",
            "logs",
            "codedock.log");
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddProvider(new CodeDockFileLoggerProvider(logPath));
        });
        services.AddSingleton<CodeDockDataPaths>();
        services.AddSingleton<IProjectStore, ProjectJsonStore>();
        services.AddSingleton<ISessionStore, SessionJsonStore>();
        services.AddSingleton<IProjectPathValidator, ProjectPathValidator>();
        services.AddSingleton<IProjectRegistry, ProjectRegistry>();
        services.AddSingleton<WindowAssociationResolver>();
        services.AddSingleton<IIdeLocator, WindowsIdeLocator>();
        services.AddSingleton<IIdeLauncher, WindowsIdeLauncher>();
        services.AddSingleton<IDesktopWindowService, Win32DesktopWindowService>();
        services.AddSingleton<IIdeWorkspaceService, IdeWorkspaceService>();
        services.AddSingleton<IFolderPicker, FolderPicker>();
        services.AddSingleton<IUserDialogService, UserDialogService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
