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
        if (DataContext is AddConnectionViewModel vm)
        {
            vm.RequestPickKeyFile -= PickKeyFile;
            vm.RequestPickKeyFile += PickKeyFile;
        }
    }

    private async Task<string?> PickKeyFile()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Private Key",
            AllowMultiple = false
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
