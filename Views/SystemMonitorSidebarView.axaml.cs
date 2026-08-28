using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using JxTerminal.Models;
using JxTerminal.ViewModels;

namespace JxTerminal.Views;

public partial class SystemMonitorSidebarView : UserControl
{
    public SystemMonitorSidebarView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            // Hand the platform file dialogs to the SFTP view model (parameterless commands).
            if (TopLevel.GetTopLevel(this)?.StorageProvider is { } storage
                && DataContext is TerminalViewModel { Sftp: { } sftp })
            {
                sftp.SetStorageProvider(storage);
            }

            // Drag & drop upload target.
            var list = this.FindControl<ListBox>("SftpFileList");
            if (list != null)
            {
                DragDrop.SetAllowDrop(list, true);
                list.AddHandler(DragDrop.DragOverEvent, SftpList_DragOver);
                list.AddHandler(DragDrop.DropEvent, SftpList_Drop);
            }
        };
    }

    private TerminalViewModel? FindViewModel(Control control)
    {
        foreach (var ancestor in control.GetVisualAncestors())
        {
            if (ancestor is Control c && c.DataContext is TerminalViewModel vm)
            {
                return vm;
            }
        }

        return DataContext as TerminalViewModel;
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

    private void SftpList_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void SftpList_Drop(object? sender, DragEventArgs e)
    {
        if (sender is not Control control) return;
        var sftp = FindViewModel(control)?.Sftp;
        if (sftp == null) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files == null || files.Length == 0) return;

        e.Handled = true;
        _ = sftp.UploadDroppedAsync(files);
    }
}
