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
            Title = Services.LocalizationService.Instance["SelectBackgroundImage"],
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
                    var bgDir = Services.PathConfig.BackgroundsDir;

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
                    Services.AppLog.Error($"Failed to copy background image: {ex.Message}");
                }
            }
        }
    }
}
