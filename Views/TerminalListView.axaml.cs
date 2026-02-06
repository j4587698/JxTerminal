using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using JxTerminal.Models;
using JxTerminal.ViewModels;

namespace JxTerminal.Views;

public partial class TerminalListView : UserControl
{
    public TerminalListView()
    {
        InitializeComponent();
    }

    private TerminalViewModel? FindViewModel(Control control)
    {
        // Walk up visual tree to find parent with TerminalViewModel DataContext
        foreach (var ancestor in control.GetVisualAncestors())
        {
            if (ancestor is Control c && c.DataContext is TerminalViewModel vm)
                return vm;
        }
        return null;
    }

    private void SftpPathTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            var vm = FindViewModel(textBox);
            vm?.Sftp?.RefreshFilesCommand.Execute(null);
        }
    }

    private void SftpFileList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is SftpFileItem item)
        {
            var vm = FindViewModel(listBox);
            vm?.Sftp?.NavigateCommand.Execute(item);
        }
    }
}
