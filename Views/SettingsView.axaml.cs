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
                vm.Settings.BackgroundImagePath = files[0].Path.LocalPath;
            }
        }
    }
    private void LanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox &&
            comboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string cultureCode)
        {
            JxTerminal.Services.LocalizationService.Instance.SetLanguage(cultureCode);
        }
    }
}
