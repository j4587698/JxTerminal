using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Platform;

namespace JxTerminal.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private Dictionary<string, string> _resources = new();
        private string _cultureCode = "en-US";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string this[string key]
        {
            get
            {
                if (_resources.TryGetValue(key, out var value))
                {
                    return value;
                }
                return key;
            }
        }

        private LocalizationService()
        {
            // Default to English or try to detect system language in a real app
            // For now default to en-US
            SetLanguage("en-US");
        }

        public void SetLanguage(string cultureCode)
        {
            _cultureCode = cultureCode;
            LoadResources(cultureCode);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // Refresh all properties
        }

        public string CultureCode => _cultureCode;

        private void LoadResources(string cultureCode)
        {
            var uri = new Uri($"avares://JxTerminal/Assets/Localization/{cultureCode}.json");
            try
            {
                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    using var reader = new StreamReader(stream);
                    var json = reader.ReadToEnd();
                    _resources = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Localization file not found: {uri}");
                    _resources.Clear();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading localization resources: {ex.Message}");
                _resources.Clear();
            }
        }
    }
}
