using Avalonia.Controls;
using JxTerminal.ViewModels;

namespace JxTerminal.Views;

public partial class KeyManagerView : UserControl
{
    public KeyManagerView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is { } storage
                && DataContext is KeyManagerViewModel vm)
            {
                vm.SetStorageProvider(storage);
            }
        };
    }
}
