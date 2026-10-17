using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Dialog;
using JxTerminal.Services.KeyStore;

namespace JxTerminal.ViewModels;

public partial class KeyManagerViewModel : ViewModelBase
{
    private readonly IKeyStoreService _keyStore;
    private readonly IDialogService _dialog;
    private IStorageProvider? _storageProvider;

    public ObservableCollection<KeyItem> Keys => _keyStore.Keys;

    [ObservableProperty]
    private KeyItem? _selectedKey;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public ObservableCollection<KeyItem> FilteredKeys { get; } = new();

    public bool HasNoKeys => FilteredKeys.Count == 0;

    public KeyManagerViewModel(IKeyStoreService? keyStore = null, IDialogService? dialog = null)
    {
        _keyStore = keyStore ?? KeyStoreService.Instance;
        _dialog = dialog ?? DialogService.Instance;

        _ = InitAsync();
    }

    public void SetStorageProvider(IStorageProvider storageProvider) => _storageProvider = storageProvider;

    private async Task InitAsync()
    {
        await _keyStore.LoadKeysAsync();
        UpdateFilteredKeys();
    }

    partial void OnSearchTextChanged(string value) => UpdateFilteredKeys();

    private void UpdateFilteredKeys()
    {
        FilteredKeys.Clear();
        var search = SearchText.Trim().ToLowerInvariant();

        foreach (var key in Keys)
        {
            if (string.IsNullOrEmpty(search) ||
                key.Name.ToLowerInvariant().Contains(search) ||
                key.Fingerprint.ToLowerInvariant().Contains(search) ||
                key.DisplayType.ToLowerInvariant().Contains(search))
            {
                FilteredKeys.Add(key);
            }
        }

        if (SelectedKey == null || !FilteredKeys.Contains(SelectedKey))
        {
            SelectedKey = FilteredKeys.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasNoKeys));
    }

    [RelayCommand]
    public async Task GenerateKey()
    {
        var l = LocalizationService.Instance;
        var name = await _dialog.ShowInputAsync(l["GenerateKeyTitle"], l["PromptKeyName"], defaultValue: "id_ecdsa");
        if (string.IsNullOrWhiteSpace(name)) return;

        // Primary one-click generation uses ECDSA P-256: modern, compact, and fully
        // supported by the built-in crypto stack and SSH.NET.
        var key = await _keyStore.GenerateKeyAsync(name.Trim(), KeyType.ECDSA);
        UpdateFilteredKeys();
        SelectedKey = key;
    }

    [RelayCommand]
    public async Task GenerateRsaKey()
    {
        var l = LocalizationService.Instance;
        var name = await _dialog.ShowInputAsync(l["GenerateKeyTitle"], l["PromptKeyName"], defaultValue: "id_rsa_4096");
        if (string.IsNullOrWhiteSpace(name)) return;

        var key = await _keyStore.GenerateKeyAsync(name.Trim(), KeyType.RSA4096);
        UpdateFilteredKeys();
        SelectedKey = key;
    }

    [RelayCommand]
    public async Task ImportKey()
    {
        var l = LocalizationService.Instance;

        if (_storageProvider != null)
        {
            var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = l["ImportKeyTitle"],
                AllowMultiple = false
            });

            if (files.Count > 0)
            {
                using var stream = await files[0].OpenReadAsync();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                var keyName = Path.GetFileNameWithoutExtension(files[0].Name);

                var key = await _keyStore.ImportKeyAsync(keyName, content);
                UpdateFilteredKeys();
                SelectedKey = key;
                return;
            }
        }

        // Fallback to text prompt
        var pem = await _dialog.ShowInputAsync(l["ImportKeyTitle"], l["PastePrivateKeyPrompt"]);
        if (!string.IsNullOrWhiteSpace(pem))
        {
            var key = await _keyStore.ImportKeyAsync("imported_key", pem);
            UpdateFilteredKeys();
            SelectedKey = key;
        }
    }

    [RelayCommand]
    public async Task CopyPublicKey()
    {
        if (SelectedKey == null || string.IsNullOrEmpty(SelectedKey.PublicKeyOpenSsh)) return;

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(SelectedKey.PublicKeyOpenSsh);
            _dialog.ShowMessage(LocalizationService.Instance["Copied"], LocalizationService.Instance["PublicKeyCopied"]);
        }
    }

    [RelayCommand]
    public async Task CopyFingerprint()
    {
        if (SelectedKey == null || string.IsNullOrEmpty(SelectedKey.Fingerprint)) return;

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(SelectedKey.Fingerprint);
            _dialog.ShowMessage(LocalizationService.Instance["Copied"], LocalizationService.Instance["FingerprintCopied"]);
        }
    }

    [RelayCommand]
    public async Task ExportPrivateKey()
    {
        if (SelectedKey == null || string.IsNullOrEmpty(SelectedKey.PrivateKeyPem)) return;
        var l = LocalizationService.Instance;

        if (!await _dialog.ShowConfirmAsync(l["ExportPrivateKeyTitle"], l["ExportPrivateKeyWarning"], l["Confirm"], danger: true))
            return;

        if (_storageProvider != null)
        {
            var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = l["SavePrivateKeyTitle"],
                SuggestedFileName = $"{SelectedKey.Name}.pem"
            });

            if (file != null)
            {
                using var stream = await file.OpenWriteAsync();
                using var writer = new StreamWriter(stream);
                await writer.WriteAsync(SelectedKey.PrivateKeyPem);
                _dialog.ShowMessage(l["Success"], l["PrivateKeySaved"]);
                return;
            }
        }

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(SelectedKey.PrivateKeyPem);
            _dialog.ShowMessage(l["Copied"], l["PrivateKeyCopied"]);
        }
    }

    [RelayCommand]
    public async Task DeleteKey(KeyItem? key)
    {
        var target = key ?? SelectedKey;
        if (target == null) return;
        var l = LocalizationService.Instance;

        if (!await _dialog.ShowConfirmAsync(l["DeleteKeyTitle"], string.Format(l["DeleteKeyMessage"], target.Name), l["Delete"], danger: true))
            return;

        await _keyStore.DeleteKeyAsync(target.Id);
        UpdateFilteredKeys();
    }
}
