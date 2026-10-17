using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using JxTerminal.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XTerm.Buffer;
using XTerm.Common;
using Material.Icons;
using Material.Icons.Avalonia;

namespace JxTerminal.Controls;

/// <summary>
/// TextInputMethodClient implementation for terminal IME support.
/// </summary>
internal class TerminalTextInputMethodClient : TextInputMethodClient
{
    private TerminalControl? _control;
    private string _preeditText = "";
    
    public void SetControl(TerminalControl? control)
    {
        _control = control;
        RaiseTextViewVisualChanged();
    }
    
    // Deliberately returns null (despite the non-nullable signature) once detached:
    // the IME framework tolerates a null visual but not an exception.
    public override Visual TextViewVisual => _control!;
    
    public override bool SupportsPreedit => true;
    
    public override bool SupportsSurroundingText => false;
    
    public override string SurroundingText => "";
    
    public override Rect CursorRectangle
    {
        get
        {
            if (_control == null) return default;
            return _control.GetCursorRectangle();
        }
    }
    
    public override TextSelection Selection
    {
        get => new TextSelection(0, 0);
        set { }
    }
    
    public override void SetPreeditText(string? preeditText)
    {
        _preeditText = preeditText ?? "";
        _control?.SetPreeditText(_preeditText);
    }
    
    public override void SetPreeditText(string? preeditText, int? cursorPos)
    {
        SetPreeditText(preeditText);
    }
    
    public void NotifyCursorRectangleChanged()
    {
        RaiseCursorRectangleChanged();
    }
}

public class TerminalControl : Control
{
    private Typeface _typeface;
    private double _charWidth;
    private double _charHeight;

    private const string StandardMonoFont = "Consolas, Menlo, Courier New, monospace";

    // Selection state
    // Rows are stored as distance-from-the-end-of-buffer so highlights stay glued to their
    // content as lines are appended or trimmed from the scrollback ring.
    private Point? _selectionStart;
    private Point? _selectionEnd;
    private bool _isSelecting;

    /// <summary>
    /// True when non-empty text is currently selected in the terminal buffer.
    /// </summary>
    public bool HasSelection => _selectionStart != null && _selectionEnd != null && _selectionStart.Value != _selectionEnd.Value;

    // Scroll pinning: keep the viewport glued to the bottom while new output arrives,
    // unless the user scrolled upwards.
    private bool _stickToBottom = true;

    // Hovered hyperlink (URL detection)
    private Rect _hoverLinkRect;
    private string? _hoverLinkUrl;

    // Coalesced render scheduling: multiple ScreenUpdated events within the same
    // dispatcher pass collapse into a single InvalidateVisual cycle.
    private bool _renderQueued;

    // Last mouse button reported as pressed, so SGR release events can name the button.
    private int _lastMouseButton;

    // Brush cache for rendering
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = new();

    // Search re-scan debounce (output can arrive at high frequency)
    private DispatcherTimer? _searchDebounceTimer;

    // Cursor blink state
    private DispatcherTimer? _cursorBlinkTimer;
    private bool _cursorVisible = true;
    private const int CursorBlinkIntervalMs = 500;
    
    // IME support
    private readonly TerminalTextInputMethodClient _imClient = new();
    private string _preeditText = "";

    // Auto-suggestion state
    private string _inputLineBuffer = string.Empty;
    private IReadOnlyList<Models.TerminalSuggestionItem> _suggestions = Array.Empty<Models.TerminalSuggestionItem>();
    private int _selectedSuggestionIndex = -1;
    private int _suggestionScrollOffset;
    private const int MaxVisibleSuggestions = 7;
    private bool _isSuggestionOpen;
    private Rect _suggestionPopupBounds;

    // ANSI 256 color palette (instance-level, updated from color scheme)
    private Color[] _ansiColors = TerminalColorSchemes.Default.BuildAnsiPalette();

    public static readonly StyledProperty<string> FontFamilyProperty = 
        AvaloniaProperty.Register<TerminalControl, string>(nameof(FontFamily), "DejaVu Sans Mono, Consolas, Courier New, monospace");

    public static readonly StyledProperty<double> FontSizeProperty = 
        AvaloniaProperty.Register<TerminalControl, double>(nameof(FontSize), 14);

    public string FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    // Bind to Interface instead of ViewModel
    public static readonly StyledProperty<ITerminalController?> ControllerProperty = 
        AvaloniaProperty.Register<TerminalControl, ITerminalController?>(nameof(Controller));

    public ITerminalController? Controller
    {
        get => GetValue(ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    // Scroll properties for binding to external ScrollBar
    public static readonly StyledProperty<double> ScrollPositionProperty = 
        AvaloniaProperty.Register<TerminalControl, double>(nameof(ScrollPosition), 0);

    public static readonly StyledProperty<double> ScrollMaximumProperty = 
        AvaloniaProperty.Register<TerminalControl, double>(nameof(ScrollMaximum), 0);

    public static readonly StyledProperty<double> ScrollViewportProperty = 
        AvaloniaProperty.Register<TerminalControl, double>(nameof(ScrollViewport), 24);

    public double ScrollPosition
    {
        get => GetValue(ScrollPositionProperty);
        set => SetValue(ScrollPositionProperty, value);
    }

    public double ScrollMaximum
    {
        get => GetValue(ScrollMaximumProperty);
        set => SetValue(ScrollMaximumProperty, value);
    }

    public double ScrollViewport
    {
        get => GetValue(ScrollViewportProperty);
        set => SetValue(ScrollViewportProperty, value);
    }

    // Terminal appearance properties
    public static readonly StyledProperty<Color> BackgroundColorProperty = 
        AvaloniaProperty.Register<TerminalControl, Color>(nameof(BackgroundColor), Color.FromRgb(0, 0, 0));

    public static readonly StyledProperty<Color> ForegroundColorProperty = 
        AvaloniaProperty.Register<TerminalControl, Color>(nameof(ForegroundColor), Color.FromRgb(229, 229, 229));

    public static readonly StyledProperty<IImage?> BackgroundImageProperty = 
        AvaloniaProperty.Register<TerminalControl, IImage?>(nameof(BackgroundImage), null);

    public static readonly StyledProperty<double> BackgroundImageOpacityProperty = 
        AvaloniaProperty.Register<TerminalControl, double>(nameof(BackgroundImageOpacity), 0.3);

    public static readonly StyledProperty<bool> EnableColorsProperty = 
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(EnableColors), true);

    public static readonly StyledProperty<int> ScrollbackLinesProperty = 
        AvaloniaProperty.Register<TerminalControl, int>(nameof(ScrollbackLines), 1000);

    public static readonly StyledProperty<TerminalColorScheme?> ColorSchemeProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalColorScheme?>(nameof(ColorScheme), null);

    public static readonly StyledProperty<bool> CopyOnSelectProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(CopyOnSelect));

    /// <summary>Cursor shape: "Block" (default), "Bar" or "Underline".</summary>
    public static readonly StyledProperty<string> CursorStyleProperty =
        AvaloniaProperty.Register<TerminalControl, string>(nameof(CursorStyle), "Block");

    public static readonly StyledProperty<Color> SelectionBackgroundProperty = 
        AvaloniaProperty.Register<TerminalControl, Color>(nameof(SelectionBackground), Color.FromArgb(77, 255, 255, 255));

    public static readonly StyledProperty<bool> EnableSuggestionsProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(EnableSuggestions), true);

    public bool EnableSuggestions
    {
        get => GetValue(EnableSuggestionsProperty);
        set => SetValue(EnableSuggestionsProperty, value);
    }

    // Search / find-in-buffer properties
    public static readonly StyledProperty<string?> SearchQueryProperty =
        AvaloniaProperty.Register<TerminalControl, string?>(nameof(SearchQuery));

    public static readonly StyledProperty<bool> SearchMatchCaseProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(SearchMatchCase));

    public static readonly StyledProperty<int> SearchMatchCountProperty =
        AvaloniaProperty.Register<TerminalControl, int>(nameof(SearchMatchCount));

    /// <summary>1-based index of the highlighted match, or 0 when there is none.</summary>
    public static readonly StyledProperty<int> SearchMatchOrdinalProperty =
        AvaloniaProperty.Register<TerminalControl, int>(nameof(SearchMatchOrdinal));

    public string? SearchQuery
    {
        get => GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    public bool SearchMatchCase
    {
        get => GetValue(SearchMatchCaseProperty);
        set => SetValue(SearchMatchCaseProperty, value);
    }

    public int SearchMatchCount
    {
        get => GetValue(SearchMatchCountProperty);
        private set => SetValue(SearchMatchCountProperty, value);
    }

    public int SearchMatchOrdinal
    {
        get => GetValue(SearchMatchOrdinalProperty);
        private set => SetValue(SearchMatchOrdinalProperty, value);
    }

    public Color BackgroundColor
    {
        get => GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    public Color ForegroundColor
    {
        get => GetValue(ForegroundColorProperty);
        set => SetValue(ForegroundColorProperty, value);
    }

    public IImage? BackgroundImage
    {
        get => GetValue(BackgroundImageProperty);
        set => SetValue(BackgroundImageProperty, value);
    }

    public double BackgroundImageOpacity
    {
        get => GetValue(BackgroundImageOpacityProperty);
        set => SetValue(BackgroundImageOpacityProperty, value);
    }

    public bool EnableColors
    {
        get => GetValue(EnableColorsProperty);
        set => SetValue(EnableColorsProperty, value);
    }

    public int ScrollbackLines
    {
        get => GetValue(ScrollbackLinesProperty);
        set => SetValue(ScrollbackLinesProperty, value);
    }

    public TerminalColorScheme? ColorScheme
    {
        get => GetValue(ColorSchemeProperty);
        set => SetValue(ColorSchemeProperty, value);
    }

    public bool CopyOnSelect
    {
        get => GetValue(CopyOnSelectProperty);
        set => SetValue(CopyOnSelectProperty, value);
    }

    public string CursorStyle
    {
        get => GetValue(CursorStyleProperty);
        set => SetValue(CursorStyleProperty, value);
    }

    public Color SelectionBackground
    {
        get => GetValue(SelectionBackgroundProperty);
        set => SetValue(SelectionBackgroundProperty, value);
    }

    static TerminalControl()
    {
        // Register IME handler
        TextInputMethodClientRequestedEvent.AddClassHandler<TerminalControl>((terminal, e) =>
        {
            e.Client = terminal._imClient;
        });
    }
    
    public TerminalControl()
    {
        Focusable = true;
        UpdateTypeface();
        InitializeCursorBlinkTimer();
        InitializeSearchDebounce();

        // Initialize IME client
        _imClient.SetControl(this);

        // Context Menu
        var contextMenu = new ContextMenu();
        var copyItem = new MenuItem { Icon = new MaterialIcon { Kind = MaterialIconKind.ContentCopy, Width = 15, Height = 15 } };
        copyItem.Click += async (s, e) => await CopyText();
        var pasteItem = new MenuItem { Icon = new MaterialIcon { Kind = MaterialIconKind.ContentPaste, Width = 15, Height = 15 } };
        pasteItem.Click += async (s, e) => await PasteText();
        var pasteSelectionItem = new MenuItem { Icon = new MaterialIcon { Kind = MaterialIconKind.ClipboardArrowLeftOutline, Width = 15, Height = 15 } };
        pasteSelectionItem.Click += (s, e) => PasteSelection();
        var selectAllItem = new MenuItem { Icon = new MaterialIcon { Kind = MaterialIconKind.SelectAll, Width = 15, Height = 15 } };
        selectAllItem.Click += (s, e) => SelectAll();
        var clearItem = new MenuItem { Icon = new MaterialIcon { Kind = MaterialIconKind.Broom, Width = 15, Height = 15 } };
        clearItem.Click += (s, e) => ClearScreen();
        var clearScrollbackItem = new MenuItem { Icon = new MaterialIcon { Kind = MaterialIconKind.DeleteSweepOutline, Width = 15, Height = 15 } };
        clearScrollbackItem.Click += (s, e) => ClearScrollback();

        contextMenu.Items.Add(copyItem);
        contextMenu.Items.Add(pasteItem);
        contextMenu.Items.Add(pasteSelectionItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(selectAllItem);
        contextMenu.Items.Add(clearItem);
        contextMenu.Items.Add(clearScrollbackItem);

        contextMenu.Opening += async (s, e) =>
        {
            var selectedText = GetSelectedText();
            bool hasSelection = !string.IsNullOrEmpty(selectedText);
            copyItem.IsEnabled = hasSelection;
            pasteSelectionItem.IsEnabled = hasSelection;
            pasteItem.IsEnabled = false;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                try
                {
#pragma warning disable CS0618
                    var clipText = await topLevel.Clipboard.TryGetTextAsync();
#pragma warning restore CS0618
                    pasteItem.IsEnabled = !string.IsNullOrEmpty(clipText);
                }
                catch
                {
                    pasteItem.IsEnabled = false;
                }
            }
        };

        _localizeMenu = () =>
        {
            var l = Services.LocalizationService.Instance;
            copyItem.Header = l["Copy"];
            pasteItem.Header = l["Paste"];
            pasteSelectionItem.Header = l["PasteSelection"];
            selectAllItem.Header = l["SelectAll"];
            clearItem.Header = l["ClearScreen"];
            clearScrollbackItem.Header = l["ClearScrollback"];
        };
        _localizeMenu();

        this.ContextMenu = contextMenu;

        // Drag & drop of files pastes their quoted paths, like classic terminals.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnFileDragOver);
        AddHandler(DragDrop.DropEvent, OnFileDrop);
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files == null || Controller == null) return;

        var paths = files
            .Select(f => $"\"{f.Path.LocalPath}\"")
            .ToList();
        if (paths.Count == 0) return;

        ClearSelectionOnInput();
        Controller.SendInput(string.Join(" ", paths));
        e.Handled = true;
    }

    private Action? _localizeMenu;

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => _localizeMenu?.Invoke();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Services.LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        Services.LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
        _localizeMenu?.Invoke();
        ResizeTerminal(forceNotifyController: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Services.LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        _cursorBlinkTimer?.Stop();
        _searchDebounceTimer?.Stop();
    }

    // ---------- Selection coordinate helpers (distance-from-end rows) ----------

    private static double ToRelRow(double absRow, int lineCount) => lineCount - 1 - absRow;

    private static double ToAbsRow(double relRow, int lineCount)
        => Math.Max(0, Math.Min(lineCount - 1, lineCount - 1 - relRow));

    /// <summary>Returns the plain text of an absolute buffer row, trimmed of trailing blanks.</summary>
    private static string GetLineText(TerminalBuffer buffer, int absRow, int cols)
    {
        if (absRow < 0 || absRow >= buffer.Lines.Length) return "";
        return buffer.Lines[absRow] is BufferLine bl
            ? bl.TranslateToString(true)
            : "";
    }

    /// <summary>
    /// Builds the visible text of a line plus a per-UTF16-index grid column map.
    /// Wide (CJK) glyphs occupy two columns but one text entry, so grid columns and
    /// string indices diverge; this map is the single source of truth for the
    /// conversion used by search, selection and word picking.
    /// </summary>
    private static string BuildLineTextWithColumnMap(BufferLine line, int cols, List<int> columns)
    {
        var sb = new StringBuilder();
        columns.Clear();

        int length = Math.Min(line.Length, cols);
        for (int col = 0; col < length; col++)
        {
            var cell = line[col];
            if (cell.Width == 0) continue; // trailing half of a wide glyph

            var content = string.IsNullOrEmpty(cell.Content) ? " " : cell.Content;
            for (int k = 0; k < content.Length; k++) columns.Add(col);
            sb.Append(content);
        }
        return sb.ToString();
    }

    /// <summary>First text index whose grid column is >= the given column (textLength when none).</summary>
    private static int ColumnToTextIndex(List<int> columns, int col, int textLength)
    {
        for (int i = 0; i < columns.Count; i++)
            if (columns[i] >= col) return i;
        return textLength;
    }

    private SolidColorBrush BrushFor(Color color)
    {
        if (_brushCache.TryGetValue(color, out var brush)) return brush;
        // Truecolor escape sequences can mint an unbounded number of colors;
        // cap the cache so long sessions cannot grow it forever.
        if (_brushCache.Count > 512) _brushCache.Clear();
        brush = new SolidColorBrush(color);
        _brushCache[color] = brush;
        return brush;
    }

    /// <summary>
    /// Selects the whole scrollback buffer.
    /// </summary>
    public void SelectAll()
    {
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null) return;

        // Distance-from-end rows: 0 = newest line, count-1 = oldest.
        int count = terminal.Buffer.Lines.Length;
        _selectionStart = new Point(0, ToRelRow(0, count));
        _selectionEnd = new Point(Math.Max(0, terminal.Cols - 1), 0);
        if (Controller != null)
        {
            Controller.SelectionStart = _selectionStart;
            Controller.SelectionEnd = _selectionEnd;
        }
        InvalidateVisual();
    }

    /// <summary>
    /// Asks the remote shell to clear the screen (Ctrl+L).
    /// </summary>
    public void ClearScreen() => Controller?.SendInput("\x0c");

    /// <summary>
    /// Clears screen and scrollback locally by feeding the corresponding escape
    /// sequences through the XTerm parser.
    /// </summary>
    public void ClearScrollback()
    {
        var terminal = Controller?.Terminal;
        if (terminal == null) return;
        terminal.Write("\x1b[H\x1b[2J\x1b[3J");
        _stickToBottom = true;
        InvalidateVisual();
    }

    private readonly List<(int Row, int StartCol, int EndCol)> _searchMatches = new();
    private int _currentMatch = -1;

    /// <summary>
    /// Rebuilds the match list for <see cref="SearchQuery"/> over the whole scrollback buffer
    /// and jumps to the match closest to the current viewport.
    /// </summary>
    private void RunSearch(bool jumpToMatch = true)
    {
        int previousMatch = _currentMatch;
        _searchMatches.Clear();
        _currentMatch = -1;

        var terminal = Controller?.Terminal;
        var query = SearchQuery;

        if (terminal?.Buffer == null || string.IsNullOrEmpty(query))
        {
            SearchMatchCount = 0;
            SearchMatchOrdinal = 0;
            InvalidateVisual();
            return;
        }

        var comparison = SearchMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var buffer = terminal.Buffer;
        var columns = new List<int>();

        for (int row = 0; row < buffer.Lines.Length; row++)
        {
            if (buffer.Lines[row] is not BufferLine line) continue;

            var lineText = BuildLineTextWithColumnMap(line, terminal.Cols, columns);
            int index = 0;
            while (index <= lineText.Length - query.Length)
            {
                int found = lineText.IndexOf(query, index, comparison);
                if (found < 0) break;

                int startCol = columns[found];
                int lastCharIndex = Math.Min(found + query.Length - 1, columns.Count - 1);
                int endCol = columns[lastCharIndex] + 1;
                _searchMatches.Add((row, startCol, endCol));

                index = found + Math.Max(1, query.Length);
            }
        }

        SearchMatchCount = _searchMatches.Count;

        if (_searchMatches.Count > 0)
        {
            if (jumpToMatch)
            {
                // Prefer the first match at or below the current viewport.
                int viewport = buffer.YDisp;
                int candidate = _searchMatches.FindIndex(m => m.Row >= viewport);
                _currentMatch = candidate >= 0 ? candidate : _searchMatches.Count - 1;
                ScrollToCurrentMatch();
            }
            else
            {
                _currentMatch = Math.Min(Math.Max(previousMatch, 0), _searchMatches.Count - 1);
                SearchMatchOrdinal = _currentMatch + 1;
            }
        }
        else
        {
            SearchMatchOrdinal = 0;
        }

        InvalidateVisual();
    }

    public void FindNext() => StepMatch(1);

    public void FindPrevious() => StepMatch(-1);

    private void StepMatch(int offset)
    {
        if (_searchMatches.Count == 0) return;

        _currentMatch = (_currentMatch + offset + _searchMatches.Count) % _searchMatches.Count;
        ScrollToCurrentMatch();
        InvalidateVisual();
    }

    private void ScrollToCurrentMatch()
    {
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null || _currentMatch < 0) return;

        SearchMatchOrdinal = _currentMatch + 1;

        var buffer = terminal.Buffer;
        int row = _searchMatches[_currentMatch].Row;

        if (row < buffer.YDisp || row >= buffer.YDisp + terminal.Rows)
        {
            int target = Math.Max(0, Math.Min(row - terminal.Rows / 2, buffer.YBase));
            buffer.ViewportY = target;
            UpdateScrollProperties();
        }
    }

    /// <summary>
    /// Clears the current search and its highlights.
    /// </summary>
    public void ClearSearch()
    {
        SearchQuery = null;
    }

    private static readonly SolidColorBrush SearchHighlightBrush = new(Color.FromArgb(90, 255, 214, 0));
    private static readonly SolidColorBrush SearchActiveHighlightBrush = new(Color.FromArgb(170, 255, 170, 0));

    private void RenderSearchMatches(DrawingContext context, TerminalBuffer buffer, double verticalOffset)
    {
        var terminal = Controller?.Terminal;
        if (terminal == null || _charWidth <= 0) return;

        for (int i = 0; i < _searchMatches.Count; i++)
        {
            var (row, startCol, endCol) = _searchMatches[i];
            int screenRow = row - buffer.YDisp;
            if (screenRow < 0 || screenRow >= terminal.Rows) continue;

            double x = startCol * _charWidth;
            double y = verticalOffset + screenRow * _charHeight;
            double w = (endCol - startCol) * _charWidth;
            if (w <= 0) continue;

            context.FillRectangle(i == _currentMatch ? SearchActiveHighlightBrush : SearchHighlightBrush,
                new Rect(x, y, w, _charHeight));
        }
    }

    private void InitializeCursorBlinkTimer()
    {
        _cursorBlinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(CursorBlinkIntervalMs)
        };
        _cursorBlinkTimer.Tick += (s, e) =>
        {
            if (_isSelecting || HasSelection) return;
            _cursorVisible = !_cursorVisible;
            InvalidateVisual();
        };
    }

    private void InitializeSearchDebounce()
    {
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _searchDebounceTimer.Tick += (s, e) =>
        {
            _searchDebounceTimer!.Stop();
            RunSearch(jumpToMatch: false);
        };
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        // Reset cursor to visible and start blinking
        _cursorVisible = true;
        _cursorBlinkTimer?.Start();
        
        // Enable IME
        _imClient.SetControl(this);
        
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        // Stop blinking and keep cursor visible (but as hollow)
        _cursorBlinkTimer?.Stop();
        _cursorVisible = true;
        
        // Clear preedit text and disable IME
        _preeditText = "";
        _imClient.SetControl(null);
        DismissSuggestions();
        
        InvalidateVisual();
    }

    /// <summary>
    /// Handles property changes within the color scheme (e.g. individual color changed).
    /// </summary>
    private void OnColorSchemePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        UpdateAnsiPalette();
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual);
    }

    /// <summary>
    /// Rebuilds the ANSI color palette from the current color scheme.
    /// </summary>
    private void UpdateAnsiPalette()
    {
        if (ColorScheme != null)
        {
            _ansiColors = ColorScheme.BuildAnsiPalette();
        }
        else
        {
            _ansiColors = TerminalColorSchemes.Default.BuildAnsiPalette();
        }
    }

    private void OnScreenUpdated()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _renderQueued = false;

            // Keep the viewport glued to the bottom while output streams in,
            // unless the user deliberately scrolled upwards.
            var terminal = Controller?.Terminal;
            if (terminal?.Buffer != null && _stickToBottom)
            {
                var buffer = terminal.Buffer;
                if (buffer.YDisp != buffer.YBase)
                    buffer.ViewportY = buffer.YBase;
            }

            // Buffer indices shift as new output arrives; debounce the rescan so
            // high-throughput output does not trigger a full-buffer search per chunk.
            if (!string.IsNullOrEmpty(SearchQuery))
            {
                _searchDebounceTimer!.Stop();
                _searchDebounceTimer.Start();
            }

            // Make sure the grid still fits the viewport (e.g. after a scrollback rebuild).
            ResizeTerminal();
            UpdateScrollProperties();
            InvalidateVisual();
        });
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FontFamilyProperty || change.Property == FontSizeProperty)
        {
            UpdateTypeface();
            ResizeTerminal();
            InvalidateVisual();
        }
        else if (change.Property == ControllerProperty)
        {
            if (change.OldValue is ITerminalController oldController)
            {
                oldController.ScreenUpdated -= OnScreenUpdated;
                oldController.SelectionStart = _selectionStart;
                oldController.SelectionEnd = _selectionEnd;
            }

            if (change.NewValue is ITerminalController newController)
            {
                newController.ScreenUpdated += OnScreenUpdated;
                _selectionStart = newController.SelectionStart;
                _selectionEnd = newController.SelectionEnd;
                _isSelecting = false;
                ResizeTerminal(forceNotifyController: true);
                Dispatcher.UIThread.InvokeAsync(() => Focus());
                OnScreenUpdated(); // Initial render
            }
            else
            {
                _selectionStart = null;
                _selectionEnd = null;
                _isSelecting = false;
            }
        }
        else if (change.Property == ScrollPositionProperty)
        {
            // External scrollbar changed position
            var terminal = Controller?.Terminal;
            if (terminal != null)
            {
                int newYDisp = (int)ScrollPosition;
                if (newYDisp != terminal.Buffer.YDisp)
                {
                    terminal.Buffer.ViewportY = newYDisp;
                    InvalidateVisual();
                }
                _stickToBottom = newYDisp >= terminal.Buffer.YBase;
            }
        }
        else if (change.Property == BackgroundColorProperty ||
                 change.Property == ForegroundColorProperty ||
                 change.Property == BackgroundImageProperty ||
                 change.Property == BackgroundImageOpacityProperty ||
                 change.Property == EnableColorsProperty ||
                 change.Property == SelectionBackgroundProperty ||
                 change.Property == CursorStyleProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == SearchQueryProperty || change.Property == SearchMatchCaseProperty)
        {
            RunSearch();
        }
        else if (change.Property == ColorSchemeProperty)
        {
            if (change.OldValue is TerminalColorScheme oldScheme)
            {
                oldScheme.PropertyChanged -= OnColorSchemePropertyChanged;
            }
            if (change.NewValue is TerminalColorScheme newScheme)
            {
                newScheme.PropertyChanged += OnColorSchemePropertyChanged;
            }
            UpdateAnsiPalette();
            InvalidateVisual();
        }
    }

    private void UpdateScrollProperties()
    {
        var terminal = Controller?.Terminal;
        if (terminal == null) return;

        var buffer = terminal.Buffer;
        double maximum = Math.Max(0, buffer.YBase);
        double viewport = terminal.Rows;
        double position = buffer.YDisp;

        if (Math.Abs(ScrollMaximum - maximum) > 0.01)
            ScrollMaximum = maximum;
        if (Math.Abs(ScrollViewport - viewport) > 0.01)
            ScrollViewport = viewport;
        if (Math.Abs(ScrollPosition - position) > 0.01)
            ScrollPosition = position;
    }

    private void UpdateTypeface()
    {
        var family = FontFamily;
        if (!string.IsNullOrWhiteSpace(family) && !family.Contains("monospace", StringComparison.OrdinalIgnoreCase))
        {
            family = $"{family}, monospace";
        }
        _typeface = new Typeface(family);

        // Measure a representative character to determine grid size.
        // In terminals, digit '0' represents standard column width for monospace and tabular figures.
        var formatted = new FormattedText("0", System.Globalization.CultureInfo.InvariantCulture, 
            FlowDirection.LeftToRight, _typeface, FontSize, Brushes.White);
        
        _charWidth = Math.Max(1.0, formatted.WidthIncludingTrailingWhitespace);
        _charHeight = Math.Max(1.0, formatted.Height);
    }

    /// <summary>
    /// Calculates the vertical offset to center terminal content within the control.
    /// </summary>
    private double GetVerticalOffset()
    {
        var terminal = Controller?.Terminal;
        if (terminal == null || _charHeight <= 0) return 0;

        double usedHeight = terminal.Rows * _charHeight;
        // Clamp: while the control is smaller than the grid the midpoint is negative,
        // which would shift content out of the visible area.
        return Math.Max(0, (Bounds.Height - usedHeight) / 2);
    }

    /// <summary>
    /// Gets the cursor rectangle for IME positioning.
    /// </summary>
    internal Rect GetCursorRectangle()
    {
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null || _charWidth <= 0 || _charHeight <= 0)
            return default;
        
        var buffer = terminal.Buffer;
        int absoluteCursorY = buffer.YBase + buffer.Y;
        int viewportY = buffer.YDisp;
        int screenY = absoluteCursorY - viewportY;
        
        if (screenY < 0 || screenY >= terminal.Rows)
            return default;
        
        double verticalOffset = GetVerticalOffset();
        double cursorX = buffer.X * _charWidth;
        double cursorY = verticalOffset + screenY * _charHeight;
        
        return new Rect(cursorX, cursorY, _charWidth, _charHeight);
    }
    
    /// <summary>
    /// Sets the preedit text for IME composition.
    /// </summary>
    internal void SetPreeditText(string preeditText)
    {
        _preeditText = preeditText;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ResizeTerminal();
    }

    private void ResizeTerminal(bool forceNotifyController = false)
    {
        if (_charWidth <= 0 || _charHeight <= 0) return;
        if (Bounds.Width < _charWidth || Bounds.Height < _charHeight) return;

        var terminal = Controller?.Terminal;
        if (terminal == null) return;

        var cols = Math.Max(2, (int)(Bounds.Width / _charWidth));
        var rows = Math.Max(2, (int)(Bounds.Height / _charHeight));

        bool sizeChanged = cols != terminal.Cols || rows != terminal.Rows;

        if (sizeChanged)
        {
            int oldRows = terminal.Rows;
            terminal.Resize(cols, rows);

            var buffer = terminal.Buffer;
            if (rows > oldRows && buffer != null)
            {
                int cursorAbsoluteY = buffer.YBase + buffer.Y;
                int maxYDisp = Math.Max(0, buffer.YBase);
                if (cursorAbsoluteY >= buffer.YDisp + oldRows - 1)
                {
                    buffer.ViewportY = Math.Max(0, Math.Min(cursorAbsoluteY - rows + 1, maxYDisp));
                }
            }

            UpdateScrollProperties();
            InvalidateVisual();
        }

        if (sizeChanged || forceNotifyController)
        {
            Controller?.Resize(rows, cols, (int)Bounds.Width, (int)Bounds.Height);
        }
    }

    private Color GetColor(int colorValue, int colorMode, Color defaultColor, bool isBold = false, bool isBackground = false)
    {
        if (colorValue < 0 || !EnableColors) return defaultColor;

        if (colorMode == (int)ColorMode.RGB)
        {
            byte r = (byte)((colorValue >> 16) & 0xFF);
            byte g = (byte)((colorValue >> 8) & 0xFF);
            byte b = (byte)(colorValue & 0xFF);
            return Color.FromRgb(r, g, b);
        }

        // Palette256 mode (or standard ANSI)
        // In XTerm.NET:
        // colorValue 0..15: Standard & bright ANSI colors
        // colorValue 16..231: 256-color cube
        // colorValue 232..255: 256-color grayscale
        // colorValue 256: Default Foreground
        // colorValue 257: Default Background
        if (colorValue == 256) return ForegroundColor;
        if (colorValue == 257) return BackgroundColor;

        if (colorValue >= 0 && colorValue < 256)
        {
            if (isBold && !isBackground && colorValue >= 0 && colorValue < 8)
            {
                colorValue += 8;
            }
            if (colorValue < _ansiColors.Length)
            {
                return _ansiColors[colorValue];
            }
        }

        return defaultColor;
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(BackgroundColor), null, new Rect(0, 0, Bounds.Width, Bounds.Height));

        if (BackgroundImage != null)
        {
            using (context.PushOpacity(BackgroundImageOpacity))
            {
                context.DrawImage(BackgroundImage, new Rect(0, 0, Bounds.Width, Bounds.Height));
            }
        }

        var terminal = Controller?.Terminal;
        if (terminal == null) return;

        var buffer = terminal.Buffer;
        double verticalOffset = GetVerticalOffset();
        double terminalHeight = terminal.Rows * _charHeight;

        // Create clip region for terminal content area
        var clipRect = new Rect(0, verticalOffset, Bounds.Width, terminalHeight);
        using (context.PushClip(clipRect))
        {
            for (int row = 0; row < terminal.Rows; row++)
            {
                int lineIndex = buffer.YDisp + row;
                if (lineIndex >= buffer.Lines.Length) break;

                var bufferLine = buffer.Lines[lineIndex];
                if (bufferLine is not BufferLine bl) continue;

                double yPos = verticalOffset + row * _charHeight;

                // Batch consecutive same-style cells into a single text run: this aligns
                // all glyph advances strictly to terminal grid columns (Windows Terminal AtlasEngine style),
                // ensuring perfect cursor and click alignment for all fonts (monospace, CJK, proportional).
                int runStartCol = -1;
                int runCols = 0;
                StringBuilder runText = new();
                List<TerminalGlyphRenderer.CellSpan> runCells = new();
                bool runHasVisibleChars = false;
                Color runFg = default;
                bool runBold = false;

                void FlushRun()
                {
                    if (runText.Length == 0) return;
                    if (runHasVisibleChars)
                    {
                        var typeface = runBold
                            ? new Typeface(_typeface.FontFamily, FontStyle.Normal, FontWeight.Bold)
                            : _typeface;
                        TerminalGlyphRenderer.DrawGridAlignedRun(
                            context,
                            runText.ToString(),
                            runCells,
                            typeface,
                            FontSize,
                            BrushFor(runFg),
                            yPos,
                            _charWidth,
                            _charHeight);
                    }
                    runText.Clear();
                    runCells.Clear();
                    runHasVisibleChars = false;
                    runStartCol = -1;
                    runCols = 0;
                }

                for (int col = 0; col < terminal.Cols && col < bl.Length; col++)
                {
                    var cell = bl[col];
                    if (cell.Width == 0) continue; // trailing half of a wide char

                    var attrs = cell.Attributes;
                    bool isBold = attrs.IsBold();
                    bool isInverse = attrs.IsInverse();

                    int fgColorValue = attrs.GetFgColor();
                    int bgColorValue = attrs.GetBgColor();
                    int fgColorMode = attrs.GetFgColorMode();
                    int bgColorMode = attrs.GetBgColorMode();

                    Color fgColor = GetColor(fgColorValue, fgColorMode, ForegroundColor, isBold, isBackground: false);
                    Color bgColor = GetColor(bgColorValue, bgColorMode, BackgroundColor, isBold: false, isBackground: true);

                    if (isInverse) (fgColor, bgColor) = (bgColor, fgColor);

                    double xPos = col * _charWidth;
                    double width = cell.Width * _charWidth;

                    if (bgColor != BackgroundColor)
                    {
                        context.FillRectangle(BrushFor(bgColor), new Rect(xPos, yPos, width, _charHeight));
                    }

                    string content = cell.Content;
                    if (string.IsNullOrEmpty(content)) content = " ";

                    // Break the run when style or column continuity changes.
                    if (runText.Length > 0 &&
                        (!fgColor.Equals(runFg) || isBold != runBold || col != runStartCol + runCols))
                    {
                        FlushRun();
                    }

                    if (runText.Length == 0)
                    {
                        runFg = fgColor;
                        runBold = isBold;
                        runStartCol = col;
                        runCols = 0;
                    }

                    runText.Append(content);
                    runCells.Add(new TerminalGlyphRenderer.CellSpan(col, cell.Width, content.Length));
                    if (!runHasVisibleChars && !string.IsNullOrWhiteSpace(content))
                    {
                        runHasVisibleChars = true;
                    }
                    runCols += cell.Width;
                }

                FlushRun();
            }

            if (_searchMatches.Count > 0)
            {
                RenderSearchMatches(context, buffer, verticalOffset);
            }

            if (_hoverLinkUrl != null && _hoverLinkRect.Width > 0)
            {
                // Underline the hovered hyperlink.
                var underlineY = _hoverLinkRect.Bottom - 1.5;
                context.DrawLine(
                    new Pen(BrushFor(ForegroundColor), 1),
                    new Point(_hoverLinkRect.X, underlineY),
                    new Point(_hoverLinkRect.Right, underlineY));
            }

            if (_selectionStart.HasValue && _selectionEnd.HasValue)
            {
                RenderSelection(context, buffer, verticalOffset);
            }

            int absoluteCursorY = buffer.YBase + buffer.Y;
            int viewportY = buffer.YDisp;
            int screenY = absoluteCursorY - viewportY;

            if (screenY >= 0 && screenY < terminal.Rows)
            {
                double cursorX = buffer.X * _charWidth;
                double rowY = verticalOffset + screenY * _charHeight;
                var cursorColor = ForegroundColor;
                
                // Draw preedit text (IME composition text) if present
                if (!string.IsNullOrEmpty(_preeditText))
                {
                    // Draw preedit background
                    var preeditFormatted = new FormattedText(_preeditText, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, _typeface, FontSize, BrushFor(ForegroundColor));
                    double preeditWidth = preeditFormatted.WidthIncludingTrailingWhitespace;
                    double textVerticalOffset = (_charHeight - preeditFormatted.Height) / 2;

                    // Background for preedit text
                    context.FillRectangle(BrushFor(BackgroundColor),
                        new Rect(cursorX, rowY, preeditWidth, _charHeight));

                    // Draw underline to indicate composition
                    var underlineY = rowY + _charHeight - 2;
                    var pen = new Pen(BrushFor(ForegroundColor), 1);
                    context.DrawLine(pen, new Point(cursorX, underlineY), new Point(cursorX + preeditWidth, underlineY));
                    
                    // Draw preedit text
                    context.DrawText(preeditFormatted, new Point(cursorX, rowY + textVerticalOffset));
                    
                    // Move cursor position after preedit text for display
                    cursorX += preeditWidth;
                }
                
                // Calculate cursor geometry for the configured shape.
                double cursorHeight = FontSize;
                double cursorVerticalOffset = (_charHeight - cursorHeight) / 2;
                double cursorY = rowY + cursorVerticalOffset;

                var style = (CursorStyle ?? "Block").Trim();
                Rect cursorRect = style.Equals("Bar", StringComparison.OrdinalIgnoreCase)
                    ? new Rect(cursorX, cursorY, Math.Max(2, _charWidth * 0.15), cursorHeight)
                    : style.Equals("Underline", StringComparison.OrdinalIgnoreCase)
                        ? new Rect(cursorX, rowY + _charHeight - 3, _charWidth, 2.5)
                        : new Rect(cursorX, cursorY, _charWidth, cursorHeight);

                if (IsFocused)
                {
                    // Focused: blinking solid cursor when no text selected
                    if (_cursorVisible && !HasSelection)
                    {
                        context.FillRectangle(BrushFor(cursorColor), cursorRect);
                    }
                    else if (HasSelection)
                    {
                        // When text is selected, draw a steady outline cursor so it does not fight with selection highlight
                        var pen = new Pen(BrushFor(cursorColor), 1.2);
                        context.DrawRectangle(null, pen, cursorRect);
                    }
                }
                else
                {
                    // Not focused: static hollow cursor (outline only)
                    var pen = new Pen(BrushFor(cursorColor), 1.5);
                    context.DrawRectangle(null, pen, cursorRect);
                }
            }
        }

        if (_isSuggestionOpen && _suggestions.Count > 0 && CanShowSuggestions() && buffer != null)
        {
            int absoluteCursorY = buffer.YBase + buffer.Y;
            int viewportY = buffer.YDisp;
            int screenY = absoluteCursorY - viewportY;
            double cursorX = buffer.X * _charWidth;
            double cursorY = verticalOffset + screenY * _charHeight;
            RenderSuggestions(context, cursorX, cursorY);
        }
    }

    private bool CanShowSuggestions()
    {
        if (!EnableSuggestions || Controller == null) return false;
        if (Controller.ApplicationCursorKeys || Controller.MouseTracking) return false;

        return true;
    }

    private void UpdateSuggestions()
    {
        if (!CanShowSuggestions() || string.IsNullOrWhiteSpace(_inputLineBuffer))
        {
            DismissSuggestions();
            return;
        }

        var list = Services.Terminal.TerminalSuggestionService.Instance.GetSuggestions(_inputLineBuffer);
        if (list.Count > 0)
        {
            _suggestions = list;
            _selectedSuggestionIndex = -1;
            _suggestionScrollOffset = 0;
            _isSuggestionOpen = true;
        }
        else
        {
            _suggestions = Array.Empty<Models.TerminalSuggestionItem>();
            _suggestionScrollOffset = 0;
            _isSuggestionOpen = false;
        }
        InvalidateVisual();
    }

    private void DismissSuggestions()
    {
        if (_isSuggestionOpen)
        {
            _isSuggestionOpen = false;
            _suggestions = Array.Empty<Models.TerminalSuggestionItem>();
            _selectedSuggestionIndex = -1;
            _suggestionScrollOffset = 0;
            _suggestionPopupBounds = default;
            InvalidateVisual();
        }
    }

    private void EnsureSuggestionVisible()
    {
        if (_suggestions.Count <= MaxVisibleSuggestions || _selectedSuggestionIndex < 0)
        {
            _suggestionScrollOffset = 0;
            return;
        }

        if (_selectedSuggestionIndex >= _suggestionScrollOffset + MaxVisibleSuggestions)
        {
            _suggestionScrollOffset = _selectedSuggestionIndex - MaxVisibleSuggestions + 1;
        }
        else if (_selectedSuggestionIndex < _suggestionScrollOffset)
        {
            _suggestionScrollOffset = _selectedSuggestionIndex;
        }

        _suggestionScrollOffset = Math.Clamp(_suggestionScrollOffset, 0, _suggestions.Count - MaxVisibleSuggestions);
    }

    private void ApplySelectedSuggestion()
    {
        if (!_isSuggestionOpen || _suggestions.Count == 0 || _selectedSuggestionIndex < 0 || _selectedSuggestionIndex >= _suggestions.Count)
            return;

        var item = _suggestions[_selectedSuggestionIndex];
        var suffix = Services.Terminal.TerminalSuggestionService.Instance.CalculateSuffix(_inputLineBuffer, item);

        DismissSuggestions();

        if (!string.IsNullOrEmpty(suffix))
        {
            _inputLineBuffer += suffix;
            Controller?.SendInput(suffix);
        }
    }

    private void RenderSuggestions(DrawingContext context, double cursorX, double cursorY)
    {
        if (!_isSuggestionOpen || _suggestions.Count == 0 || !CanShowSuggestions())
            return;

        int totalCount = _suggestions.Count;
        int displayCount = Math.Min(totalCount, MaxVisibleSuggestions);
        const double rowHeight = 26;
        const double footerHeight = 22;
        double cardHeight = displayCount * rowHeight + footerHeight + 8;
        double cardWidth = Math.Min(480, Math.Max(340, Bounds.Width - 20));

        double cardX = cursorX;
        if (cardX + cardWidth > Bounds.Width - 10)
        {
            cardX = Math.Max(10, Bounds.Width - cardWidth - 10);
        }

        double cardY = cursorY + _charHeight + 4;
        if (cardY + cardHeight > Bounds.Height - 10)
        {
            // Flip above cursor if bottom overflows
            cardY = cursorY - cardHeight - 4;
        }
        cardY = Math.Max(4, cardY);

        _suggestionPopupBounds = new Rect(cardX, cardY, cardWidth, cardHeight);

        // Ensure scroll offset is valid
        _suggestionScrollOffset = Math.Clamp(_suggestionScrollOffset, 0, Math.Max(0, totalCount - displayCount));

        // Outer soft shadow
        var shadowBrush = BrushFor(Color.FromArgb(90, 0, 0, 0));
        context.FillRectangle(shadowBrush, new Rect(cardX - 1, cardY - 1, cardWidth + 2, cardHeight + 2), 8);

        // Card surface background
        var bgBrush = BrushFor(Color.FromArgb(246, 24, 25, 36));
        context.FillRectangle(bgBrush, _suggestionPopupBounds, 7);

        // Border outline
        var borderPen = new Pen(BrushFor(Color.FromArgb(100, 100, 110, 140)), 1);
        context.DrawRectangle(null, borderPen, _suggestionPopupBounds, 7);

        bool hasScrollbar = totalCount > MaxVisibleSuggestions;
        double itemContentRight = hasScrollbar ? cardWidth - 14 : cardWidth - 8;

        // Draw items in the visible window
        for (int i = 0; i < displayCount; i++)
        {
            int itemIndex = _suggestionScrollOffset + i;
            if (itemIndex >= totalCount) break;

            var item = _suggestions[itemIndex];
            double itemY = cardY + 4 + i * rowHeight;
            bool isSelected = (itemIndex == _selectedSuggestionIndex);

            if (isSelected)
            {
                var selectBrush = BrushFor(Color.FromArgb(210, 37, 99, 235));
                context.FillRectangle(selectBrush, new Rect(cardX + 4, itemY, itemContentRight, rowHeight - 2), 4);
            }

            // Category badge
            Color badgeColor = item.Category switch
            {
                Models.SuggestionCategory.History => Color.FromRgb(245, 158, 11),
                Models.SuggestionCategory.Snippet => Color.FromRgb(168, 85, 247),
                Models.SuggestionCategory.Flag => Color.FromRgb(16, 185, 129),
                Models.SuggestionCategory.Subcommand => Color.FromRgb(59, 130, 246),
                _ => Color.FromRgb(6, 182, 212)
            };

            var badgeText = $"[{item.CategoryBadge}]";
            var badgeFormatted = new FormattedText(
                badgeText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(_typeface.FontFamily, FontStyle.Normal, FontWeight.Bold),
                10,
                BrushFor(isSelected ? Colors.White : badgeColor));

            context.DrawText(badgeFormatted, new Point(cardX + 8, itemY + 5));

            // Command Display Text
            double cmdStartX = cardX + 54;
            var cmdColor = isSelected ? Colors.White : ForegroundColor;
            var cmdFormatted = new FormattedText(
                item.DisplayText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(_typeface.FontFamily, FontStyle.Normal, FontWeight.Bold),
                12,
                BrushFor(cmdColor));

            context.DrawText(cmdFormatted, new Point(cmdStartX, itemY + 4));

            // Description
            if (!string.IsNullOrEmpty(item.Description))
            {
                var descColor = isSelected
                    ? Color.FromArgb(235, 225, 235, 255)
                    : Color.FromRgb(150, 150, 165);

                var descFormatted = new FormattedText(
                    item.Description,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    11,
                    BrushFor(descColor));

                double descX = cardX + itemContentRight - descFormatted.Width - 4;
                if (descX > cmdStartX + cmdFormatted.Width + 14)
                {
                    context.DrawText(descFormatted, new Point(descX, itemY + 5));
                }
            }
        }

        // Mini Scrollbar on the right
        if (hasScrollbar)
        {
            double trackX = cardX + cardWidth - 6;
            double trackY = cardY + 6;
            double trackHeight = displayCount * rowHeight - 4;

            // Subtle track background
            var trackBrush = BrushFor(Color.FromArgb(25, 255, 255, 255));
            context.FillRectangle(trackBrush, new Rect(trackX, trackY, 3, trackHeight), 1.5f);

            // Thumb
            double thumbHeight = Math.Max(18, trackHeight * displayCount / totalCount);
            double maxScroll = totalCount - displayCount;
            double thumbY = trackY + (_suggestionScrollOffset / maxScroll) * (trackHeight - thumbHeight);
            var thumbBrush = BrushFor(Color.FromArgb(130, 255, 255, 255));
            context.FillRectangle(thumbBrush, new Rect(trackX, thumbY, 3, thumbHeight), 1.5f);
        }

        // Footer hint bar
        double footerY = cardY + cardHeight - footerHeight;
        var dividerPen = new Pen(BrushFor(Color.FromArgb(40, 255, 255, 255)), 1);
        context.DrawLine(dividerPen, new Point(cardX + 6, footerY), new Point(cardX + cardWidth - 6, footerY));

        string hintText = _selectedSuggestionIndex >= 0
            ? "Tab/Enter 确认 • ↑↓ 切换 • Esc 关闭"
            : "↓ 选择 • → 采纳首项 • Tab 原生补全";

        var hintFormatted = new FormattedText(
            hintText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            10.5,
            BrushFor(Color.FromRgb(125, 125, 140)));

        context.DrawText(hintFormatted, new Point(cardX + 10, footerY + 4));

        // Right-aligned item counter (e.g. 1/28 or total count when unselected)
        var countText = _selectedSuggestionIndex >= 0
            ? $"{_selectedSuggestionIndex + 1}/{totalCount}"
            : $"{totalCount}";
        var countFormatted = new FormattedText(
            countText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(_typeface.FontFamily, FontStyle.Normal, FontWeight.Bold),
            10,
            BrushFor(Color.FromArgb(180, 148, 163, 184)));

        double countX = cardX + cardWidth - countFormatted.Width - 10;
        context.DrawText(countFormatted, new Point(countX, footerY + 4));
    }

    private void RenderSelection(DrawingContext context, TerminalBuffer buffer, double verticalOffset)
    {
        var terminal = Controller?.Terminal;
        if (terminal == null || _selectionStart == null || _selectionEnd == null) return;

        int lineCount = buffer.Lines.Length;
        int r1 = (int)ToAbsRow(_selectionStart.Value.Y, lineCount);
        int r2 = (int)ToAbsRow(_selectionEnd.Value.Y, lineCount);
        int c1 = (int)_selectionStart.Value.X;
        int c2 = (int)_selectionEnd.Value.X;

        // In absolute row coordinates (0 = top, lineCount-1 = bottom), ensure (r1, c1) <= (r2, c2)
        if (r1 > r2 || (r1 == r2 && c1 > c2))
        {
            (r1, r2) = (r2, r1);
            (c1, c2) = (c2, c1);
        }

        for (int r = r1; r <= r2; r++)
        {
            if (r < 0 || r >= lineCount) continue;

            var line = buffer.Lines[r];
            int lineLen = terminal.Cols;

            if (line is BufferLine bl)
            {
                lineLen = bl.GetTrimmedLength();
                if (lineLen <= 0) lineLen = 0;
            }

            int startCol = (r == r1) ? c1 : 0;
            int endCol = (r == r2) ? c2 : lineLen;

            if (startCol > lineLen) startCol = lineLen;
            if (endCol > lineLen) endCol = lineLen;
            if (startCol >= endCol) continue;

            double selX = startCol * _charWidth;
            double selY = verticalOffset + (r - buffer.YDisp) * _charHeight;
            double w = (endCol - startCol) * _charWidth;

            if (selY >= 0 && selY < Bounds.Height && w > 0)
            {
                context.FillRectangle(BrushFor(SelectionBackground), new Rect(selX, selY, w, _charHeight));
            }
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!IsFocused || Controller == null) return;

        if (!string.IsNullOrEmpty(e.Text))
        {
            ClearSelectionOnInput();
            Controller.SendInput(e.Text);
            e.Handled = true;

            if (EnableSuggestions && CanShowSuggestions())
            {
                if (e.Text.Contains('\r') || e.Text.Contains('\n'))
                {
                    DismissSuggestions();
                    _inputLineBuffer = string.Empty;
                }
                else
                {
                    _inputLineBuffer += e.Text;
                    UpdateSuggestions();
                }
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsFocused || Controller == null)
        {
            base.OnKeyDown(e);
            return;
        }

        // Handle suggestion navigation and completion when popup is open
        if (_isSuggestionOpen && _suggestions.Count > 0)
        {
            if (e.Key == Key.Down)
            {
                if (_selectedSuggestionIndex < 0)
                {
                    _selectedSuggestionIndex = 0;
                }
                else
                {
                    _selectedSuggestionIndex = (_selectedSuggestionIndex + 1) % _suggestions.Count;
                }
                EnsureSuggestionVisible();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                if (_selectedSuggestionIndex < 0)
                {
                    _selectedSuggestionIndex = _suggestions.Count - 1;
                }
                else
                {
                    _selectedSuggestionIndex = (_selectedSuggestionIndex - 1 + _suggestions.Count) % _suggestions.Count;
                }
                EnsureSuggestionVisible();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.PageDown)
            {
                if (_selectedSuggestionIndex < 0)
                {
                    _selectedSuggestionIndex = Math.Min(_suggestions.Count - 1, MaxVisibleSuggestions - 1);
                }
                else
                {
                    _selectedSuggestionIndex = Math.Min(_suggestions.Count - 1, _selectedSuggestionIndex + MaxVisibleSuggestions);
                }
                EnsureSuggestionVisible();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.PageUp)
            {
                if (_selectedSuggestionIndex < 0)
                {
                    _selectedSuggestionIndex = 0;
                }
                else
                {
                    _selectedSuggestionIndex = Math.Max(0, _selectedSuggestionIndex - MaxVisibleSuggestions);
                }
                EnsureSuggestionVisible();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Right && e.KeyModifiers == KeyModifiers.None)
            {
                // Right arrow quickly accepts the first or currently selected suggestion
                if (_selectedSuggestionIndex < 0)
                {
                    _selectedSuggestionIndex = 0;
                }
                ApplySelectedSuggestion();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.None)
            {
                // Left arrow moves cursor backwards in shell, dismiss suggestions to avoid confusion
                DismissSuggestions();
                // Allow left arrow to pass through to normal sequence generation
            }
            if (e.Key == Key.Tab)
            {
                if (_selectedSuggestionIndex >= 0)
                {
                    ApplySelectedSuggestion();
                    e.Handled = true;
                    return;
                }
                else
                {
                    // User has not explicitly selected a suggestion: dismiss popup and let Tab pass through to Linux shell
                    DismissSuggestions();
                    // Do not mark Handled, allow normal key sequence mapping below to send "\t"
                }
            }
            if (e.Key == Key.Enter)
            {
                if (_selectedSuggestionIndex >= 0)
                {
                    ApplySelectedSuggestion();
                    e.Handled = true;
                    return;
                }
                else
                {
                    // User has not explicitly selected a suggestion: dismiss popup and execute input normally
                    DismissSuggestions();
                    // Do not mark Handled, allow normal key sequence mapping below to send "\r"
                }
            }
            if (e.Key == Key.Escape)
            {
                DismissSuggestions();
                e.Handled = true;
                return;
            }
        }

        string? sequence = null;

        // Terminal-app shortcuts take precedence over control sequences.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.C:
                    _ = CopyText();
                    e.Handled = true;
                    return;
                case Key.V:
                    _ = PasteText();
                    e.Handled = true;
                    return;
                case Key.A:
                    SelectAll();
                    e.Handled = true;
                    return;
            }
        }

        // Shift+Insert is the classic X11 paste binding.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Insert)
        {
            _ = PasteText();
            e.Handled = true;
            return;
        }

        // Plain Ctrl+C copies when a selection exists, otherwise ^C is sent (standard terminal UX).
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C && HasSelection)
        {
            _ = CopyText();
            e.Handled = true;
            return;
        }

        // Ctrl +/- zoom the font, Ctrl+0 resets to the default size.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract:
                    ZoomFont(e.Key is Key.OemMinus or Key.Subtract ? -1 : 1);
                    e.Handled = true;
                    return;
                case Key.D0 or Key.NumPad0:
                    SetValue(FontSizeProperty, 14d);
                    e.Handled = true;
                    return;
            }
        }

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool altOnly = e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                       && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
                       && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            sequence = e.Key switch
            {
                Key.C => "\x03",
                Key.D => "\x04",
                Key.Z => "\x1a",
                Key.L => "\x0c",
                Key.A => "\x01",
                Key.E => "\x05",
                Key.K => "\x0b",
                Key.U => "\x15",
                Key.W => "\x17",
                Key.R => "\x12",
                // Common readline bindings that were missing.
                Key.Y => "\x19",
                Key.O => "\x0f",
                Key.T => "\x14",
                Key.X => "\x18",
                Key.N => "\x0e",
                Key.P => "\x10",
                Key.B => "\x02",
                Key.F => "\x06",
                Key.V => "\x16",
                _ => null
            };
        }
        else
        {
            // DECCKM-aware navigation: application mode uses SS3 forms.
            string ss3Prefix = Controller?.ApplicationCursorKeys == true ? "\x1bO" : "\x1b[";
            sequence = e.Key switch
            {
                Key.Enter => "\r",
                Key.Back => "\x7f",
                Key.Tab => "\t",
                Key.Escape => "\x1b",
                Key.Up => $"{ss3Prefix}A",
                Key.Down => $"{ss3Prefix}B",
                Key.Right => $"{ss3Prefix}C",
                Key.Left => $"{ss3Prefix}D",
                Key.Home => Controller?.ApplicationCursorKeys == true ? "\x1bOH" : "\x1b[H",
                Key.End => Controller?.ApplicationCursorKeys == true ? "\x1bOF" : "\x1b[F",
                Key.Insert => "\x1b[2~",
                Key.Delete => "\x1b[3~",
                Key.PageUp => "\x1b[5~",
                Key.PageDown => "\x1b[6~",
                Key.F1 => "\x1bOP",
                Key.F2 => "\x1bOQ",
                Key.F3 => "\x1bOR",
                Key.F4 => "\x1bOS",
                Key.F5 => "\x1b[15~",
                Key.F6 => "\x1b[17~",
                Key.F7 => "\x1b[18~",
                Key.F8 => "\x1b[19~",
                Key.F9 => "\x1b[20~",
                Key.F10 => "\x1b[21~",
                Key.F11 => "\x1b[23~",
                Key.F12 => "\x1b[24~",
                _ => null
            };
        }

        // Alt+key sends an ESC prefix (readline meta keys: Alt+b, Alt+f, Alt+. ...).
        if (sequence == null && altOnly)
        {
            var text = KeyToChar(e.Key);
            if (text != '\0')
            {
                Controller?.SendInput($"\x1b{text}");
                e.Handled = true;
                return;
            }
        }

        if (sequence != null)
        {
            ClearSelectionOnInput();
            Controller?.SendInput(sequence);
            e.Handled = true;

            if (e.Key == Key.Back)
            {
                if (_inputLineBuffer.Length > 0)
                {
                    _inputLineBuffer = _inputLineBuffer.Substring(0, _inputLineBuffer.Length - 1);
                }
                if (EnableSuggestions && CanShowSuggestions() && _inputLineBuffer.Trim().Length > 0)
                {
                    UpdateSuggestions();
                }
                else
                {
                    DismissSuggestions();
                }
            }
            else if (e.Key == Key.Enter)
            {
                if (!string.IsNullOrWhiteSpace(_inputLineBuffer))
                {
                    Services.Terminal.TerminalSuggestionService.Instance.RecordHistory(_inputLineBuffer);
                }
                _inputLineBuffer = string.Empty;
                DismissSuggestions();
            }
            else if (ctrl && (e.Key == Key.C || e.Key == Key.U || e.Key == Key.D))
            {
                _inputLineBuffer = string.Empty;
                DismissSuggestions();
            }
        }
    }

    /// <summary>Maps a key to its representative character for Alt-prefix encoding.</summary>
    private static char KeyToChar(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return (char)('a' + (key - Key.A));
        if (key >= Key.D0 && key <= Key.D9) return (char)('0' + (key - Key.D0));
        return key switch
        {
            Key.Space => ' ',
            Key.OemPeriod => '.',
            Key.OemComma => ',',
            Key.OemMinus => '-',
            Key.OemPlus => '+',
            Key.Divide => '/',
            Key.OemQuestion => '/',
            _ => '\0'
        };
    }

    private void ClearSelectionOnInput()
    {
        if (_selectionStart != null || _selectionEnd != null)
        {
            _selectionStart = null;
            _selectionEnd = null;
            if (Controller != null)
            {
                Controller.SelectionStart = null;
                Controller.SelectionEnd = null;
            }
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);

        // Middle-click paste (X11 convention).
        if (point.Properties.IsMiddleButtonPressed
            && Controller != null && !Controller.MouseTracking)
        {
            _ = PasteText();
            e.Handled = true;
            return;
        }

        // When the remote application tracks the mouse, encode and forward events
        // instead of doing local selection.
        if (Controller?.MouseTracking == true)
        {
            SendMouseEvent(point, pressed: true, wheelDelta: 0);
            e.Handled = true;
            return;
        }

        if (_hoverLinkUrl != null && point.Properties.IsLeftButtonPressed)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_hoverLinkUrl)
                {
                    UseShellExecute = true
                });
            }
            catch { /* browser launch refused */ }
            return;
        }

        var pos = e.GetPosition(this);

        if (_isSuggestionOpen && _suggestionPopupBounds.Contains(pos))
        {
            double relativeY = pos.Y - (_suggestionPopupBounds.Y + 4);
            int clickedVisibleIndex = (int)(relativeY / 26);
            int clickedIndex = _suggestionScrollOffset + clickedVisibleIndex;
            if (clickedVisibleIndex >= 0 && clickedVisibleIndex < MaxVisibleSuggestions && clickedIndex >= 0 && clickedIndex < _suggestions.Count)
            {
                _selectedSuggestionIndex = clickedIndex;
                ApplySelectedSuggestion();
                e.Handled = true;
                return;
            }
        }
        else if (_isSuggestionOpen)
        {
            DismissSuggestions();
        }

        int clicks = e.ClickCount;

        if (clicks >= 3)
        {
            SelectLine(pos);
            _isSelecting = false;
            if (Controller != null)
            {
                Controller.SelectionStart = _selectionStart;
                Controller.SelectionEnd = _selectionEnd;
            }
        }
        else if (clicks == 2)
        {
            SelectWord(pos);
            _isSelecting = true; // allow drag to extend word-by-word
            e.Pointer.Capture(this);
        }
        else
        {
            _selectionStart = GetGridPosition(pos);
            _selectionEnd = _selectionStart;
            _isSelecting = true;
            e.Pointer.Capture(this);
        }

        Focus();
        InvalidateVisual();
    }

    private void SelectWord(Point pos)
    {
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null) return;

        var grid = GetGridPosition(pos);
        int count = terminal.Buffer.Lines.Length;
        // grid.Y is a distance-from-end row; convert once to an absolute buffer row.
        int absRow = (int)ToAbsRow(grid.Y, count);
        if (terminal.Buffer.Lines[absRow] is not BufferLine line) return;

        var columns = new List<int>();
        string text = BuildLineTextWithColumnMap(line, terminal.Cols, columns);
        if (text.Length == 0) return;

        // grid.X is a grid column; map it to a string index (wide-glyph safe).
        int index = Math.Min(ColumnToTextIndex(columns, (int)grid.X, text.Length), text.Length - 1);
        if (index < 0) index = 0;

        int start = index, end = index;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        while (end < text.Length - 1 && IsWordChar(text[end + 1])) end++;

        // Selection is stored in grid columns (RenderSelection multiplies by char width).
        _selectionStart = new Point(columns[start], grid.Y);
        _selectionEnd = new Point(columns[end], grid.Y);
    }

    private void SelectLine(Point pos)
    {
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null) return;

        // grid.Y is already a distance-from-end row; use it as-is.
        var grid = GetGridPosition(pos);
        _selectionStart = new Point(0, grid.Y);
        _selectionEnd = new Point(Math.Max(0, terminal.Cols - 1), grid.Y);
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ':' or '@';

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var terminal = Controller?.Terminal;

        if (_isSelecting)
        {
            var point = e.GetPosition(this);
            _selectionEnd = GetGridPosition(point);
            InvalidateVisual();
            return;
        }

        if (Controller?.MouseTracking == true)
        {
            SendMouseEvent(e.GetCurrentPoint(this), pressed: null, wheelDelta: 0);
            return;
        }

        // URL hover detection
        UpdateHoverLink(e.GetPosition(this));
    }

    /// <summary>
    /// Finds an URL under the given position and remembers it for cursor styling,
    /// underline painting and click handling.
    /// </summary>
    private void UpdateHoverLink(Point p)
    {
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null || _charWidth <= 0 || _charHeight <= 0)
        {
            ClearHoverLink();
            return;
        }

        var buffer = terminal.Buffer;
        int col = (int)(p.X / _charWidth);
        int row = (int)((p.Y - GetVerticalOffset()) / _charHeight) + buffer.YDisp;
        if (row < buffer.YDisp || row >= buffer.YDisp + terminal.Rows || col < 0 || col >= terminal.Cols)
        {
            ClearHoverLink();
            return;
        }

        string text = GetLineText(buffer, row, terminal.Cols);
        if (text.Length == 0 || col >= text.Length)
        {
            ClearHoverLink();
            return;
        }

        foreach (System.Text.RegularExpressions.Match m in UrlRegex.Matches(text))
        {
            if (col >= m.Index && col < m.Index + m.Length)
            {
                double verticalOffset = GetVerticalOffset();
                _hoverLinkUrl = m.Value;
                _hoverLinkRect = new Rect(
                    m.Index * _charWidth,
                    verticalOffset + (row - buffer.YDisp) * _charHeight,
                    m.Length * _charWidth,
                    _charHeight);
                Cursor = new Cursor(StandardCursorType.Hand);
                InvalidateVisual();
                return;
            }
        }

        ClearHoverLink();
    }

    private static readonly System.Text.RegularExpressions.Regex UrlRegex =
        new(@"(?:https?|ftp|ssh)://[^\s\x00]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private void ClearHoverLink()
    {
        if (_hoverLinkUrl != null)
        {
            _hoverLinkUrl = null;
            _hoverLinkRect = default;
            Cursor = Cursor.Default;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Encodes a mouse event for the remote application (SGR when negotiated, X10 fallback).
    /// </summary>
    private void SendMouseEvent(PointerPoint point, bool? pressed, double wheelDelta)
    {
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null || !Controller!.MouseTracking) return;

        var pos = point.Position;
        int col = Math.Min(Math.Max((int)(pos.X / _charWidth), 0), terminal.Cols - 1) + 1;
        // xterm mouse coordinates are viewport-relative; do NOT add buffer.YDisp here.
        double relY = (pos.Y - GetVerticalOffset()) / _charHeight;
        int row = Math.Min(Math.Max((int)relY, 0), terminal.Rows - 1) + 1;

        var props = point.Properties;
        int button = props.IsRightButtonPressed ? 2 : props.IsMiddleButtonPressed ? 1 : 0;

        int code;
        string final;

        if (wheelDelta != 0)
        {
            code = wheelDelta < 0 ? 64 : 65; // scroll up / down
            final = "M";
        }
        else if (pressed == true)
        {
            _lastMouseButton = button;
            code = button;
            final = "M";
        }
        else if (pressed == false)
        {
            if (Controller.SgrMouse)
            {
                // SGR release names the released button with a 'm' final. Button state
                // is already cleared by the time PointerReleased fires, so use the
                // button remembered from the matching press.
                code = _lastMouseButton;
                final = "m";
            }
            else
            {
                // Legacy X10 encodes any release as button 3 with the press final.
                code = 3;
                final = "M";
            }
        }
        else
        {
            // Motion: report drags always, and all motion when mode 1003 is on.
            bool buttonHeld = props.IsLeftButtonPressed || props.IsMiddleButtonPressed || props.IsRightButtonPressed;
            if (!buttonHeld && !Controller.MouseAnyEvent) return;
            code = 32 + button;
            final = "M";
        }

        if (Controller.SgrMouse)
        {
            Controller.SendInput($"\x1b[<{code};{col};{row}{final}");
        }
        else
        {
            // Legacy X10 encoding, coordinates offset by 32 and capped at 223.
            char b = (char)(32 + Math.Min(code, 191));
            char x = (char)(32 + Math.Min(col, 223));
            char y = (char)(32 + Math.Min(row, 223));
            Controller.SendInput($"\x1b[M{b}{x}{y}");
        }
    }


    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isSelecting = false;
        e.Pointer.Capture(null);

        if (Controller != null)
        {
            Controller.SelectionStart = _selectionStart;
            Controller.SelectionEnd = _selectionEnd;
        }

        if (Controller?.MouseTracking == true)
        {
            SendMouseEvent(e.GetCurrentPoint(this), pressed: false, wheelDelta: 0);
            e.Handled = true;
            return;
        }

        // Copy-on-select: push the finished selection straight to the clipboard.
        if (CopyOnSelect && HasSelection)
        {
            var text = GetSelectedText();
            if (!string.IsNullOrEmpty(text)) _ = CopyText();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isSelecting = false;
        if (Controller != null)
        {
            Controller.SelectionStart = _selectionStart;
            Controller.SelectionEnd = _selectionEnd;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (_isSuggestionOpen && _suggestionPopupBounds.Contains(e.GetPosition(this)))
        {
            int scrollDelta = (int)-e.Delta.Y;
            if (scrollDelta != 0 && _suggestions.Count > MaxVisibleSuggestions)
            {
                _suggestionScrollOffset = Math.Clamp(_suggestionScrollOffset + scrollDelta, 0, _suggestions.Count - MaxVisibleSuggestions);
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null) return;

        // Ctrl+wheel zooms the font; the TwoWay binding persists the change to settings.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomFont(e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
            return;
        }

        // Forward wheel events to the remote application when it tracks the mouse.
        if (Controller!.MouseTracking && Math.Abs(e.Delta.Y) > 0.01)
        {
            SendMouseEvent(e.GetCurrentPoint(this), pressed: true, wheelDelta: e.Delta.Y);
            e.Handled = true;
            return;
        }

        int scrollAmount = (int)(-e.Delta.Y * 3);
        terminal.Buffer.ScrollLines(scrollAmount);

        var buffer = terminal.Buffer;
        _stickToBottom = buffer.YDisp >= buffer.YBase;

        UpdateScrollProperties();
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>Nudges the font size by whole points, clamped to a readable range.</summary>
    public void ZoomFont(int delta)
        => SetValue(FontSizeProperty, Math.Clamp(FontSize + delta, 6, 72));

    private Point GetGridPosition(Point p)
    {
        if (_charWidth <= 0 || _charHeight <= 0) return new Point(0, 0);

        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null) return new Point(0, 0);

        var buffer = terminal.Buffer;
        double verticalOffset = GetVerticalOffset();

        int col = (int)(p.X / _charWidth);
        int row = (int)((p.Y - verticalOffset) / _charHeight) + buffer.YDisp;

        col = Math.Max(0, Math.Min(col, terminal.Cols - 1));
        row = Math.Max(0, Math.Min(row, buffer.Lines.Length - 1));

        // Store the row as distance-from-end so the selection survives appends/trims.
        return new Point(col, ToRelRow(row, buffer.Lines.Length));
    }

    private async Task CopyText()
    {
        if (_selectionStart == null || _selectionEnd == null) return;
        var text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
        {
            var topLevel = TopLevel.GetTopLevel(this);
#pragma warning disable CS0618 // IClipboard is obsolete in Avalonia 11 but has no replacement yet
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
#pragma warning restore CS0618
        }
    }

    private async Task PasteText()
    {
        var topLevel = TopLevel.GetTopLevel(this);
#pragma warning disable CS0618 // IClipboard is obsolete in Avalonia 11 but has no replacement yet
        if (topLevel?.Clipboard != null)
        {
            var text = await topLevel.Clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text) && Controller != null)
            {
                // Bracketed paste keeps multi-line pastes from being executed line-by-line
                // by shells that requested the mode.
                var payload = Controller.BracketedPaste
                    ? $"\x1b[200~{text}\x1b[201~"
                    : text;
                ClearSelectionOnInput();
                Controller.SendInput(payload);
            }
        }
#pragma warning restore CS0618
    }

    /// <summary>
    /// Directly pastes the currently selected text into the terminal session.
    /// </summary>
    public void PasteSelection()
    {
        var text = GetSelectedText();
        if (string.IsNullOrEmpty(text) || Controller == null) return;

        var payload = Controller.BracketedPaste
            ? $"\x1b[200~{text}\x1b[201~"
            : text;
        ClearSelectionOnInput();
        Controller.SendInput(payload);
    }

    private string GetSelectedText()
    {
        if (_selectionStart == null || _selectionEnd == null) return "";
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null) return "";

        var buffer = terminal.Buffer;
        int lineCount = buffer.Lines.Length;

        int r1 = (int)ToAbsRow(_selectionStart.Value.Y, lineCount);
        int r2 = (int)ToAbsRow(_selectionEnd.Value.Y, lineCount);
        int c1 = (int)_selectionStart.Value.X;
        int c2 = (int)_selectionEnd.Value.X;

        // In absolute row coordinates (0 = top, lineCount-1 = bottom), ensure (r1, c1) <= (r2, c2)
        if (r1 > r2 || (r1 == r2 && c1 > c2))
        {
            (r1, r2) = (r2, r1);
            (c1, c2) = (c2, c1);
        }

        int startRow = r1;
        int endRow = r2;
        int startCol = c1;
        int endCol = c2;

        var sb = new StringBuilder();

        for (int r = startRow; r <= endRow; r++)
        {
            if (r >= lineCount) break;
            var line = buffer.Lines[r];

            string lineText;
            List<int>? map = null;
            if (line is BufferLine bl)
            {
                map = new List<int>();
                lineText = BuildLineTextWithColumnMap(bl, terminal.Cols, map);
            }
            else
            {
                lineText = line?.ToString() ?? "";
            }

            // Selection X is stored in grid columns; convert to string indices so
            // wide glyphs do not shift the copied range.
            int sc1;
            int sc2;
            if (map != null)
            {
                sc1 = r == startRow ? ColumnToTextIndex(map, startCol, lineText.Length) : 0;
                sc2 = r == endRow ? ColumnToTextIndex(map, endCol + 1, lineText.Length) : lineText.Length;
            }
            else
            {
                sc1 = r == startRow ? startCol : 0;
                sc2 = r == endRow ? endCol + 1 : lineText.Length;
                sc1 = Math.Max(0, Math.Min(sc1, lineText.Length));
                sc2 = Math.Max(0, Math.Min(sc2, lineText.Length));
            }

            if (sc1 < sc2)
            {
                if (r == startRow && r == endRow) sb.Append(lineText.Substring(sc1, sc2 - sc1));
                else if (r == startRow)
                {
                    sb.Append(lineText.Substring(sc1));
                    sb.AppendLine();
                }
                else if (r == endRow) sb.Append(lineText.Substring(0, sc2));
                else sb.AppendLine(lineText);
            }
            else if (r > startRow && r < endRow)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
