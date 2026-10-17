using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using JxTerminal.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace JxTerminal.Views;

public partial class AddConnectionView : UserControl
{
    public AddConnectionView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unhook from the PREVIOUS view model first; otherwise every swap leaks the
        // subscription and the old VM can still raise the picker.
        if (_hookedViewModel != null)
        {
            _hookedViewModel.RequestPickKeyFile -= PickKeyFile;
            _hookedViewModel = null;
        }

        if (DataContext is AddConnectionViewModel vm)
        {
            vm.RequestPickKeyFile += PickKeyFile;
            _hookedViewModel = vm;
        }
    }

    private AddConnectionViewModel? _hookedViewModel;

    private async Task<string?> PickKeyFile()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Services.LocalizationService.Instance["SelectPrivateKey"],
            AllowMultiple = false
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
