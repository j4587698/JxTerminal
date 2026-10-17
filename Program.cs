using Avalonia;
using System;
using System.Threading.Tasks;
#if DEBUG
using LuminaUI.Diagnostics;
#endif

namespace JxTerminal;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Enable legacy code pages (GBK, GB18030, Big5, Shift-JIS, ...) used by the
        // configurable session encodings; without this they silently fall back to UTF-8.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // Surface background failures in the log instead of losing them silently.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            JxTerminal.Services.AppLog.Error($"Unhandled: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            JxTerminal.Services.AppLog.Error($"Unobserved task: {e.Exception}");
            e.SetObserved();
        };

        // If invoked with arguments (e.g. from bastion host or CLI), attempt to forward
        // them to an existing running instance so they open as a new tab instead of a new window.
        if (args is { Length: > 0 })
        {
            var options = JxTerminal.Services.CommandLine.CommandLineParser.Parse(args);
            if (options.HasConnection && !options.ForceNewWindow)
            {
                bool shouldReuse = options.ForceReuseWindow;
                if (!shouldReuse)
                {
                    try
                    {
                        var settings = JxTerminal.Services.DataStore.LoadSettings();
                        shouldReuse = settings.ReuseWindowForCommandLine;
                    }
                    catch
                    {
                        shouldReuse = true;
                    }
                }

                if (shouldReuse)
                {
                    bool sent = JxTerminal.Services.CommandLine.SingleInstanceService
                        .TrySendArgsToExistingInstanceAsync(args, timeoutMs: 1000)
                        .GetAwaiter()
                        .GetResult();

                    if (sent)
                    {
                        // Successfully forwarded connection request to active instance.
                        return;
                    }
                }
            }
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Crash log belongs in the app data root, next to the other diagnostics.
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(JxTerminal.Services.PathConfig.AppDataRoot, "crash.log"),
                    ex.ToString());
            }
            catch { /* nothing more we can do */ }
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
#if DEBUG
            .WithDeveloperTools()
            .UseLuminaUIDiagnostics()
#endif
            .LogToTrace();
}
