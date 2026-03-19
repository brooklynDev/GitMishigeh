using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System;
using Avalonia.Markup.Xaml;
using GitMishigeh.Services;
using GitMishigeh.ViewModels;
using GitMishigeh.Views;

namespace GitMishigeh;

public partial class App : Application
{
    private LocalAutomationServer? _automationServer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var gitService = new GitService();
            var folderPickerService = new FolderPickerService();
            var recentRepositoryStore = new RecentRepositoryStore();
            var viewModel = new MainWindowViewModel(gitService, folderPickerService, recentRepositoryStore);
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.MainWindow = mainWindow;

            if (IsAutomationEnabled())
            {
                var automationPort = GetAutomationPort();
                _automationServer = new LocalAutomationServer(mainWindow, viewModel, automationPort);
            }

            desktop.Exit += async (_, _) =>
            {
                if (_automationServer is not null)
                {
                    await _automationServer.DisposeAsync();
                    _automationServer = null;
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool IsAutomationEnabled()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static int GetAutomationPort()
    {
        var rawPort = Environment.GetEnvironmentVariable("GITMISHIGEH_AUTOMATION_PORT");
        return int.TryParse(rawPort, out var parsedPort) && parsedPort is > 0 and < 65536
            ? parsedPort
            : 38457;
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
