using Avalonia.Controls;
using Avalonia.Input;
using LuminaUI.Controls;

namespace JxTerminal.Views;

public partial class MainWindow : LuminaWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // F11 toggles fullscreen; the terminal never consumes it. The base Window is
        // addressed explicitly because LuminaWindow shadows WindowState.
        if (e.Key == Key.F11)
        {
            var window = (Window)this;
            var state = window.WindowState;
            window.WindowState = state == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
            e.Handled = true;
        }
    }
}
