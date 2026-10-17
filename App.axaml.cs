using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using JxTerminal.ViewModels;
using JxTerminal.Views;
using System.Linq;

namespace JxTerminal;

public partial class App : Application
{
    public override void Initialize()
    {
        // Set default culture to use dot as decimal separator
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
        
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Pre-load sync configuration & decrypt sync key so SecurityService can decrypt
            // any credentials that were synced with the account's sync key.
            try
            {
                Services.Sync.SyncService.Instance.LoadConfigAsync().GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                Services.AppLog.Warn($"Sync config pre-load failed: {ex.Message}");
            }

            // Settings are loaded synchronously BEFORE the window is created: the main
            // window must exist before this method returns or the app may exit silently.
            var vm = MainWindowViewModel.Create();

            var mainWindow = new MainWindow
            {
                DataContext = vm,
            };
            desktop.MainWindow = mainWindow;

            // Trigger startup auto-sync in background if enabled
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    if (Services.Sync.SyncService.Instance.IsAuthenticated && Services.Sync.SyncService.Instance.Config.AutoSyncEnabled)
                    {
                        await Services.Sync.SyncService.Instance.SyncNowAsync();
                    }
                }
                catch (System.Exception ex)
                {
                    Services.AppLog.Warn($"Startup auto-sync failed: {ex.Message}");
                }
            });

            mainWindow.Closing += (_, _) => vm.FlushSettings();

            // Dispose terminal sessions and stop IPC server on exit
            desktop.Exit += (_, _) =>
            {
                vm.FlushSettings();
                Services.CommandLine.SingleInstanceService.StopServer();
                foreach (var tab in vm.TerminalList.Tabs.ToArray())
                {
                    try { tab.Dispose(); } catch { /* shutting down */ }
                }
            };

            // Start single-instance IPC server so subsequent CLI launches (bastion, terminal)
            // can add tabs to this window and bring it to front.
            Services.CommandLine.SingleInstanceService.StartServer(async incomingArgs =>
            {
                try
                {
                    var incomingOptions = Services.CommandLine.CommandLineParser.Parse(incomingArgs);

                    // Restore & activate window to front
                    Services.CommandLine.SingleInstanceService.BringWindowToFront(mainWindow);

                    if (incomingOptions.HasConnection)
                    {
                        var groups = await Services.DataStore.LoadConnectionsAsync();
                        var target = Services.CommandLine.CommandLineParser.ResolveConnection(incomingOptions, groups);
                        if (target != null)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await vm.Connect(target, incomingOptions.InitialCommand);
                            });
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Services.AppLog.Error($"SingleInstance command handling failed: {ex.Message}");
                }
            });

            // Launch connection requested via initial CLI arguments (PuTTY / OpenSSH syntax)
            if (desktop.Args is { Length: > 0 })
            {
                var launchOptions = Services.CommandLine.CommandLineParser.Parse(desktop.Args);
                if (launchOptions.HasConnection)
                {
                    mainWindow.Opened += async (_, _) =>
                    {
                        try
                        {
                            var groups = await Services.DataStore.LoadConnectionsAsync();
                            var target = Services.CommandLine.CommandLineParser.ResolveConnection(launchOptions, groups);
                            if (target != null)
                            {
                                await vm.Connect(target, launchOptions.InitialCommand);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Services.AppLog.Error($"CLI launch failed: {ex.Message}");
                        }
                    };
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
