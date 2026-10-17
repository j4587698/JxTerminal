using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace JxTerminal.Models;

public partial class TerminalSettings : ObservableObject
{
    private static TerminalColorScheme CreateDefaultColorScheme()
    {
        var scheme = (TerminalColorScheme)TerminalColorSchemes.Default.Clone();
        scheme.IsBuiltIn = true;
        return scheme;
    }

    private TerminalColorScheme _colorScheme = CreateDefaultColorScheme();

    /// <summary>
    /// The active color scheme. Contains all terminal colors including foreground, background, ANSI 16, selection, etc.
    /// </summary>
    [JsonIgnore]
    public TerminalColorScheme ColorScheme
    {
        get => _colorScheme;
        set
        {
            var old = _colorScheme;
            if (SetProperty(ref _colorScheme, value))
            {
                OnColorSchemeChanged(old, value);
            }
        }
    }

    private string _colorSchemeName = "Default";

    /// <summary>
    /// Name of the active color scheme for serialization. 
    /// If it matches a built-in preset name, the preset will be loaded on deserialization.
    /// </summary>
    public string ColorSchemeName
    {
        get => _colorSchemeName;
        set
        {
            if (SetProperty(ref _colorSchemeName, value))
            {
                if (ColorScheme == null || ColorScheme.Name != value)
                {
                    var preset = TerminalColorSchemes.GetAllPresets().Find(p => p.Name == value);
                    if (preset != null)
                    {
                        ColorScheme = (TerminalColorScheme)preset.Clone();
                        ColorScheme.IsBuiltIn = true;
                    }
                }
            }
        }
    }

    private System.Collections.Generic.List<TerminalColorScheme> _customColorSchemes = new();

    /// <summary>
    /// All saved custom color schemes. Persisted across sessions so custom schemes are never lost.
    /// </summary>
    public System.Collections.Generic.List<TerminalColorScheme> CustomColorSchemes
    {
        get => _customColorSchemes;
        set => SetProperty(ref _customColorSchemes, value ?? new());
    }

    /// <summary>
    /// Custom color scheme data for backward-compatible serialization.
    /// </summary>
    public TerminalColorScheme? CustomColorScheme
    {
        get => ColorScheme.IsBuiltIn ? null : ColorScheme;
        set
        {
            if (value != null)
            {
                value.IsBuiltIn = false;
                if (!CustomColorSchemes.Any(s => s.Name == value.Name))
                {
                    CustomColorSchemes.Add(value);
                }
                ColorScheme = value;
            }
        }
    }

    private string _language = "zh-CN";
    public string Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    private string _themeMode = "Dark";
    public string ThemeMode
    {
        get => _themeMode;
        set => SetProperty(ref _themeMode, value);
    }

    private string _accentColor = "#2563EB";
    public string AccentColor
    {
        get => _accentColor;
        set => SetProperty(ref _accentColor, value);
    }

    private void OnColorSchemeChanged(TerminalColorScheme? oldValue, TerminalColorScheme newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnColorSchemePropertyChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnColorSchemePropertyChanged;
            ColorSchemeName = newValue.Name;
        }
        // Notify that derived and child properties have changed
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(ForegroundColor));
        OnPropertyChanged(nameof(ColorScheme));
    }

    private void OnColorSchemePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When any color in the scheme changes, propagate notifications
        if (e.PropertyName == nameof(TerminalColorScheme.Background))
            OnPropertyChanged(nameof(BackgroundColor));
        else if (e.PropertyName == nameof(TerminalColorScheme.Foreground))
            OnPropertyChanged(nameof(ForegroundColor));
        
        // If current scheme is custom, ensure the matching custom scheme in CustomColorSchemes is kept up-to-date
        if (!ColorScheme.IsBuiltIn)
        {
            var idx = CustomColorSchemes.FindIndex(s => s.Name == ColorScheme.Name);
            if (idx >= 0)
            {
                CustomColorSchemes[idx] = ColorScheme;
            }
            else
            {
                CustomColorSchemes.Add(ColorScheme);
            }
        }

        // Trigger a general change notification for auto-save
        OnPropertyChanged(nameof(ColorScheme));
    }

    /// <summary>
    /// Convenience property: gets the background color from the active color scheme.
    /// </summary>
    [JsonIgnore]
    public Color BackgroundColor => ColorScheme.Background;

    /// <summary>
    /// Convenience property: gets the foreground color from the active color scheme.
    /// </summary>
    [JsonIgnore]
    public Color ForegroundColor => ColorScheme.Foreground;

    // Legacy hex properties kept for backward compatibility during deserialization
    public string? BackgroundColorHex
    {
        get => null; // No longer serialize individually
        set
        {
            // Migration: if old settings had individual color hex, apply to scheme
            if (value != null && Color.TryParse(value, out var color))
            {
                ColorScheme.Background = color;
            }
        }
    }

    public string? ForegroundColorHex
    {
        get => null;
        set
        {
            if (value != null && Color.TryParse(value, out var color))
            {
                ColorScheme.Foreground = color;
            }
        }
    }

    public const string DefaultFontFamily = "DejaVu Sans Mono, Liberation Mono, Consolas, monospace";

    private string _fontFamily = DefaultFontFamily;
    public string FontFamily
    {
        get => _fontFamily;
        set
        {
            if (string.IsNullOrWhiteSpace(value) && value != DefaultFontFamily)
            {
                value = DefaultFontFamily;
            }
            SetProperty(ref _fontFamily, value);
        }
    }

    private double _fontSize = 14;
    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    private bool _enableColors = true;
    public bool EnableColors
    {
        get => _enableColors;
        set => SetProperty(ref _enableColors, value);
    }

    private int _scrollbackLines = 1000;
    public int ScrollbackLines
    {
        get => _scrollbackLines;
        set => SetProperty(ref _scrollbackLines, value);
    }

    /// <summary>
    /// Shell launched for local PTY sessions: PowerShell, Cmd, Wsl or Bash.
    /// </summary>
    private string _localShellKind = "Auto";
    public string LocalShellKind
    {
        get => _localShellKind;
        set => SetProperty(ref _localShellKind, value);
    }

    /// <summary>
    /// Text encoding for remote sessions (SSH/Telnet/Serial), e.g. "UTF-8" or "GBK".
    /// </summary>
    private string _remoteEncoding = "UTF-8";
    public string RemoteEncoding
    {
        get => _remoteEncoding;
        set => SetProperty(ref _remoteEncoding, value);
    }

    /// <summary>Automatically copy selected terminal text to the clipboard.</summary>
    private bool _copyOnSelect;
    public bool CopyOnSelect
    {
        get => _copyOnSelect;
        set => SetProperty(ref _copyOnSelect, value);
    }

    /// <summary>Cursor shape: Block, Bar or Underline.</summary>
    private string _cursorStyle = "Block";
    public string CursorStyle
    {
        get => _cursorStyle;
        set => SetProperty(ref _cursorStyle, value);
    }

    /// <summary>
    /// When true, command-line connection requests will be sent to the running instance
    /// and opened as a new tab. When false, a new independent window/process is always created.
    /// </summary>
    private bool _reuseWindowForCommandLine = true;
    public bool ReuseWindowForCommandLine
    {
        get => _reuseWindowForCommandLine;
        set => SetProperty(ref _reuseWindowForCommandLine, value);
    }

    // ---------------- AI assistant ----------------

    /// <summary>
    /// OpenAI-compatible chat completions base URL, e.g. https://api.openai.com/v1
    /// (works with OpenAI, DeepSeek, Qwen, Ollama and other compatible providers).
    /// Empty disables the assistant.
    /// </summary>
    private string _aiEndpoint = "";
    public string AiEndpoint
    {
        get => _aiEndpoint;
        set => SetProperty(ref _aiEndpoint, value);
    }

    /// <summary>Model name sent in chat completion requests, e.g. gpt-4o-mini.</summary>
    private string _aiModel = "";
    public string AiModel
    {
        get => _aiModel;
        set => SetProperty(ref _aiModel, value);
    }

    /// <summary>
    /// When true, the assistant may write to the terminal directly (one-click control).
    /// When false, every send_input tool call asks for confirmation first.
    /// </summary>
    private bool _aiAutoApproveCommands = true;
    public bool AiAutoApproveCommands
    {
        get => _aiAutoApproveCommands;
        set => SetProperty(ref _aiAutoApproveCommands, value);
    }

    /// <summary>
    /// Maximum tool-call rounds per user request to prevent infinite loops (default 25).
    /// </summary>
    private int _aiMaxToolRounds = 25;
    public int AiMaxToolRounds
    {
        get => _aiMaxToolRounds;
        set => SetProperty(ref _aiMaxToolRounds, value);
    }

    private string _aiApiKey = "";

    /// <summary>API key for the AI endpoint. Never serialized in plain text.</summary>
    [JsonIgnore]
    public string AiApiKey
    {
        get => _aiApiKey;
        set
        {
            if (_aiApiKey != value)
            {
                _aiApiKey = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>AOT-friendly encrypted serialization property for <see cref="AiApiKey"/>.</summary>
    public string EncryptedAiApiKey
    {
        get => Services.SecurityService.Encrypt(AiApiKey);
        set => AiApiKey = Services.SecurityService.Decrypt(value);
    }

    /// <summary>True when both endpoint and API key are set and the assistant can be used.</summary>
    [JsonIgnore]
    public bool IsAiConfigured =>
        !string.IsNullOrWhiteSpace(AiEndpoint) && !string.IsNullOrWhiteSpace(AiApiKey);

    private bool _enableSuggestions = true;
    public bool EnableSuggestions
    {
        get => _enableSuggestions;
        set => SetProperty(ref _enableSuggestions, value);
    }

    /// <summary>Maximum number of executed terminal commands preserved in history (LRU).</summary>
    private int _historyRetentionLimit = 1000;
    public int HistoryRetentionLimit
    {
        get => _historyRetentionLimit;
        set => SetProperty(ref _historyRetentionLimit, value);
    }

    private FolderTransferMode _folderTransferMode = FolderTransferMode.TarGz;
    public FolderTransferMode FolderTransferMode
    {
        get => _folderTransferMode;
        set => SetProperty(ref _folderTransferMode, value);
    }

    private bool _autoExtractAfterTransfer = true;
    public bool AutoExtractAfterTransfer
    {
        get => _autoExtractAfterTransfer;
        set => SetProperty(ref _autoExtractAfterTransfer, value);
    }

    private string? _backgroundImagePath;
    public string? BackgroundImagePath
    {
        get => _backgroundImagePath;
        set
        {
            if (SetProperty(ref _backgroundImagePath, value))
            {
                OnBackgroundImagePathChanged(value);
            }
        }
    }

    private double _backgroundImageOpacity = 0.3;
    public double BackgroundImageOpacity
    {
        get => _backgroundImageOpacity;
        set => SetProperty(ref _backgroundImageOpacity, value);
    }

    private IImage? _backgroundImage;
    [JsonIgnore]
    public IImage? BackgroundImage
    {
        get => _backgroundImage;
        set => SetProperty(ref _backgroundImage, value);
    }

    private void OnBackgroundImagePathChanged(string? value)
    {
        // Release the previous bitmap so replacing the wallpaper does not leak
        // decoded image memory.
        if (BackgroundImage is IDisposable disposable)
            disposable.Dispose();

        if (string.IsNullOrWhiteSpace(value))
        {
            BackgroundImage = null;
            return;
        }

        var fullPath = Services.PathConfig.GetFullPath(value);

        if (!File.Exists(fullPath))
        {
            BackgroundImage = null;
            return;
        }

        try
        {
            BackgroundImage = new Bitmap(fullPath);
        }
        catch (Exception ex)
        {
            Services.AppLog.Warn($"Error loading background image: {ex.Message}");
            BackgroundImage = null;
        }
    }

    /// <summary>
    /// Apply a color scheme by reference. If it is built-in, clones it; otherwise applies the custom scheme and syncs CustomColorSchemes.
    /// </summary>
    public void ApplyScheme(TerminalColorScheme scheme)
    {
        if (scheme.IsBuiltIn)
        {
            ColorScheme = (TerminalColorScheme)scheme.Clone();
            ColorScheme.IsBuiltIn = true;
            ColorSchemeName = scheme.Name;
        }
        else
        {
            ColorScheme = scheme;
            ColorSchemeName = scheme.Name;
            var idx = CustomColorSchemes.FindIndex(s => s.Name == scheme.Name);
            if (idx >= 0)
            {
                CustomColorSchemes[idx] = scheme;
            }
            else
            {
                CustomColorSchemes.Add(scheme);
            }
        }
    }

    /// <summary>
    /// Resolve color scheme after deserialization. 
    /// Called after loading settings from JSON to link scheme name to actual preset or saved custom scheme.
    /// </summary>
    public void ResolveColorScheme()
    {
        // 1. Check if active scheme matches one in CustomColorSchemes
        var custom = CustomColorSchemes.Find(s => s.Name == ColorSchemeName);
        if (custom != null)
        {
            custom.IsBuiltIn = false;
            ColorScheme = custom;
            return;
        }

        // 2. Backward compatibility: if CustomColorScheme was deserialized
        if (ColorScheme != null && !ColorScheme.IsBuiltIn && ColorScheme.Name == ColorSchemeName)
        {
            if (!CustomColorSchemes.Any(s => s.Name == ColorScheme.Name))
            {
                CustomColorSchemes.Add(ColorScheme);
            }
            return;
        }

        // 3. Try to find a built-in preset matching the name
        var preset = TerminalColorSchemes.GetAllPresets()
            .Find(p => p.Name == ColorSchemeName);
        if (preset != null)
        {
            ColorScheme = (TerminalColorScheme)preset.Clone();
            ColorScheme.IsBuiltIn = true;
            return;
        }

        // 4. Default fallback
        var defaultPreset = TerminalColorSchemes.Default;
        ColorScheme = (TerminalColorScheme)defaultPreset.Clone();
        ColorScheme.IsBuiltIn = true;
        ColorSchemeName = defaultPreset.Name;
    }
}
