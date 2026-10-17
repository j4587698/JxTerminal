using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JxTerminal.Models;
using JxTerminal.ViewModels;
using System;

namespace JxTerminal.Views;

public partial class HostListView : UserControl
{
    public HostListView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_hookedViewModel != null)
        {
            _hookedViewModel.SearchFocusRequested -= OnSearchFocusRequested;
            _hookedViewModel = null;
        }
        if (DataContext is HostListViewModel vm)
        {
            vm.SearchFocusRequested += OnSearchFocusRequested;
            _hookedViewModel = vm;
        }
    }

    private HostListViewModel? _hookedViewModel;

    private void OnSearchFocusRequested()
    {
        // The view may have just been swapped in; wait for layout before focusing.
        Dispatcher.UIThread.Post(() => HostSearch.Focus(), DispatcherPriority.Loaded);
    }

    private void HostCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // The card contains its own Connect button: a double-click landing on that
        // button would otherwise fire both the button command and this handler.
        if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<Button>() != null)
            return;

        if (sender is Control { DataContext: SavedConnection connection }
            && DataContext is HostListViewModel vm
            && vm.ConnectCommand.CanExecute(connection))
        {
            vm.ConnectCommand.Execute(connection);
        }
    }
}