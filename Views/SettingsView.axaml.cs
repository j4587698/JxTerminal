using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using JxTerminal.Models;
using JxTerminal.ViewModels;
using System.Linq;

namespace JxTerminal.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void BrowseFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Background Image",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        if (files.Count >= 1)
        {
            if (DataContext is SettingsViewModel vm)
            {
                try
                {
                    var sourcePath = files[0].Path.LocalPath;
                    var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                    var bgDir = System.IO.Path.Combine(appData, "JxTerminal", "Backgrounds");
                    
                    if (!System.IO.Directory.Exists(bgDir))
                        System.IO.Directory.CreateDirectory(bgDir);

                    var ext = System.IO.Path.GetExtension(sourcePath);
                    var destPath = System.IO.Path.Combine(bgDir, $"background{ext}");

                    // Ensure we are not trying to copy onto itself
                    if (string.Compare(sourcePath, destPath, System.StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        System.IO.File.Copy(sourcePath, destPath, true);
                    }

                    // Save as relative path for portability
                    vm.Settings.BackgroundImagePath = JxTerminal.Services.PathConfig.GetRelativePath(destPath);
                }
                catch (System.Exception ex)
                {
                    // In a real app, show an error dialog/notification
                    System.Diagnostics.Debug.WriteLine($"Failed to copy image: {ex.Message}");
                }
            }
        }
    }
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is ListBoxItem item && item.Tag is string sectionName)
        {
            var control = this.FindControl<StackPanel>(sectionName);
            control?.BringIntoView();
        }
    }
}
