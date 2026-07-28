using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using JxTerminal.Models;
using JxTerminal.ViewModels;

namespace JxTerminal.Views;

public partial class HostListView : UserControl
{
    public HostListView()
    {
        InitializeComponent();
    }

    private void HostCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SavedConnection connection }
            && DataContext is HostListViewModel vm
            && vm.ConnectCommand.CanExecute(connection))
        {
            vm.ConnectCommand.Execute(connection);
        }
    }
}