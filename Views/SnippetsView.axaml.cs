using Avalonia.Controls;
using Avalonia.Input;
using JxTerminal.Models;
using JxTerminal.ViewModels;

namespace JxTerminal.Views;

public partial class SnippetsView : UserControl
{
    public SnippetsView()
    {
        InitializeComponent();
    }

    private void SnippetList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox
            && listBox.SelectedItem is SnippetItem item
            && DataContext is SnippetsViewModel vm)
        {
            vm.SendToTerminalCommand.Execute(item);
        }
    }

    private void HistoryList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox
            && listBox.SelectedItem is TerminalHistoryItem item
            && DataContext is SnippetsViewModel vm)
        {
            vm.SendToTerminalCommand.Execute(item);
        }
    }
}
