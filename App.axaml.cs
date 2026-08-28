using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using JxTerminal.ViewModels;
using JxTerminal.Views;

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
            // Settings are loaded synchronously BEFORE the window is created: the main
            // window must exist before this method returns or the app may exit silently.
            var vm = MainWindowViewModel.Create();

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
