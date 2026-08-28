using Avalonia;
using System;
using System.Threading.Tasks;
using LuminaUI.Diagnostics;

namespace JxTerminal;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Surface background failures in the log instead of losing them silently.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            JxTerminal.Services.AppLog.Error($"Unhandled: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            JxTerminal.Services.AppLog.Error($"Unobserved task: {e.Exception}");
            e.SetObserved();
        };

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
