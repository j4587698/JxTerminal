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
        // English strings double as the fallback so a missing key in another language
        // degrades to readable text instead of the raw key name.
        private Dictionary<string, string> _fallbackResources = new();
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
                if (_fallbackResources.TryGetValue(key, out var fallback))
                {
                    return fallback;
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
            // The fallback (en-US) is loaded once and never cleared.
            if (_fallbackResources.Count == 0 && cultureCode != "en-US")
            {
                _fallbackResources = TryLoad("en-US");
            }

            _resources = TryLoad(cultureCode);

            if (cultureCode == "en-US")
            {
                _fallbackResources = _resources;
            }
        }

        private static Dictionary<string, string> TryLoad(string cultureCode)
        {
            var uri = new Uri($"avares://JxTerminal/Assets/Localization/{cultureCode}.json");
            try
            {
                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    using var reader = new StreamReader(stream);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
                           ?? new Dictionary<string, string>();
                }

                System.Diagnostics.Debug.WriteLine($"Localization file not found: {uri}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading localization resources: {ex.Message}");
            }
            return new Dictionary<string, string>();
        }
    }
}
