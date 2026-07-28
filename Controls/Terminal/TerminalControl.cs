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
using System.Text;
using System.Threading.Tasks;
using XTerm.Buffer;
using XTerm.Common;

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
    private Point? _selectionStart;
    private Point? _selectionEnd;
    private bool _isSelecting;

    // Cursor blink state
    private DispatcherTimer? _cursorBlinkTimer;
    private bool _cursorVisible = true;
    private const int CursorBlinkIntervalMs = 500;
    
    // IME support
    private readonly TerminalTextInputMethodClient _imClient = new();
    private string _preeditText = "";

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

    public static readonly StyledProperty<Color> SelectionBackgroundProperty = 
        AvaloniaProperty.Register<TerminalControl, Color>(nameof(SelectionBackground), Color.FromArgb(77, 255, 255, 255));

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
        
        // Initialize IME client
        _imClient.SetControl(this);

        // Context Menu
        var contextMenu = new ContextMenu();
        var copyItem = new MenuItem();
        copyItem.Click += async (s, e) => await CopyText();
        var pasteItem = new MenuItem();
        pasteItem.Click += async (s, e) => await PasteText();
        var selectAllItem = new MenuItem();
        selectAllItem.Click += (s, e) => SelectAll();
        var clearItem = new MenuItem();
        clearItem.Click += (s, e) => ClearScreen();

        contextMenu.Items.Add(copyItem);
        contextMenu.Items.Add(pasteItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(selectAllItem);
        contextMenu.Items.Add(clearItem);

        _localizeMenu = () =>
        {
            var l = Services.LocalizationService.Instance;
            copyItem.Header = l["Copy"];
            pasteItem.Header = l["Paste"];
            selectAllItem.Header = l["SelectAll"];
            clearItem.Header = l["ClearScreen"];
        };
        _localizeMenu();

        this.ContextMenu = contextMenu;
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
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Services.LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        _cursorBlinkTimer?.Stop();
    }

    /// <summary>
    /// Selects the whole scrollback buffer.
    /// </summary>
    public void SelectAll()
    {
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null) return;

        _selectionStart = new Point(0, 0);
        _selectionEnd = new Point(Math.Max(0, terminal.Cols - 1), Math.Max(0, terminal.Buffer.Lines.Length - 1));
        InvalidateVisual();
    }

    /// <summary>
    /// Asks the remote shell to clear the screen (Ctrl+L).
    /// </summary>
    public void ClearScreen()
    {
        Controller?.SendInput("\x0c");
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
        var text = new StringBuilder();
        var columns = new List<int>();

        for (int row = 0; row < buffer.Lines.Length; row++)
        {
            if (buffer.Lines[row] is not BufferLine line) continue;

            text.Clear();
            columns.Clear();

            int length = Math.Min(line.Length, terminal.Cols);
            for (int col = 0; col < length; col++)
            {
                var cell = line[col];
                if (cell.Width == 0) continue;

                var content = string.IsNullOrEmpty(cell.Content) ? " " : cell.Content;
                for (int k = 0; k < content.Length; k++) columns.Add(col);
                text.Append(content);
            }

            var lineText = text.ToString();
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

    private void RenderSearchMatches(DrawingContext context, TerminalBuffer buffer, double verticalOffset)
    {
        var terminal = Controller?.Terminal;
        if (terminal == null || _charWidth <= 0) return;

        var highlight = new SolidColorBrush(Color.FromArgb(90, 255, 214, 0));
        var activeHighlight = new SolidColorBrush(Color.FromArgb(170, 255, 170, 0));

        for (int i = 0; i < _searchMatches.Count; i++)
        {
            var (row, startCol, endCol) = _searchMatches[i];
            int screenRow = row - buffer.YDisp;
            if (screenRow < 0 || screenRow >= terminal.Rows) continue;

            double x = startCol * _charWidth;
            double y = verticalOffset + screenRow * _charHeight;
            double w = (endCol - startCol) * _charWidth;
            if (w <= 0) continue;

            context.FillRectangle(i == _currentMatch ? activeHighlight : highlight,
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
            _cursorVisible = !_cursorVisible;
            InvalidateVisual();
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
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Buffer indices shift as new output arrives, so recompute matches without moving the view.
            if (!string.IsNullOrEmpty(SearchQuery)) RunSearch(jumpToMatch: false);
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
            }

            if (change.NewValue is ITerminalController newController)
            {
                newController.ScreenUpdated += OnScreenUpdated;
                ResizeTerminal();
                Dispatcher.UIThread.InvokeAsync(() => Focus());
                OnScreenUpdated(); // Initial render
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
            }
        }
        else if (change.Property == BackgroundColorProperty ||
                 change.Property == ForegroundColorProperty ||
                 change.Property == BackgroundImageProperty ||
                 change.Property == BackgroundImageOpacityProperty ||
                 change.Property == EnableColorsProperty ||
                 change.Property == SelectionBackgroundProperty)
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
        _typeface = new Typeface(FontFamily);

        // Measure a representative character to determine grid size
        // We use 'M' as a standard measure for width and height
        var formatted = new FormattedText("M", System.Globalization.CultureInfo.CurrentCulture, 
            FlowDirection.LeftToRight, _typeface, FontSize, Brushes.White);
        
        _charWidth = formatted.WidthIncludingTrailingWhitespace;
        _charHeight = formatted.Height;
    }

    /// <summary>
    /// Calculates the vertical offset to center terminal content within the control.
    /// </summary>
    private double GetVerticalOffset()
    {
        var terminal = Controller?.Terminal;
        if (terminal == null || _charHeight <= 0) return 0;
        
        double usedHeight = terminal.Rows * _charHeight;
        return (Bounds.Height - usedHeight) / 2;
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

    private void ResizeTerminal()
    {
        if (_charWidth <= 0 || _charHeight <= 0) return;

        var terminal = Controller?.Terminal;
        if (terminal == null) return;

        var cols = (int)(Bounds.Width / _charWidth);
        var rows = (int)(Bounds.Height / _charHeight);

        if (cols > 0 && rows > 0 && (cols != terminal.Cols || rows != terminal.Rows))
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
            Controller?.Resize(rows, cols, (int)Bounds.Width, (int)Bounds.Height);
            InvalidateVisual();
        }
    }

    private Color GetColor(int colorValue, int colorMode, Color defaultColor, bool isBold = false, bool isBackground = false)
    {
        if (colorValue < 0) return defaultColor;

        if (colorMode == (int)ColorMode.Palette256)
        {
            if (isBackground && colorValue == 0) return defaultColor;
            if (isBold && !isBackground && colorValue >= 0 && colorValue < 8) colorValue += 8;
            if (colorValue >= 0 && colorValue < 256) return _ansiColors[colorValue];
        }
        else if (colorMode == (int)ColorMode.RGB)
        {
            byte r = (byte)((colorValue >> 16) & 0xFF);
            byte g = (byte)((colorValue >> 8) & 0xFF);
            byte b = (byte)(colorValue & 0xFF);
            return Color.FromRgb(r, g, b);
        }

        if (colorValue >= 0 && colorValue < 256)
        {
             if (isBackground && colorValue == 0) return defaultColor;
             if (isBold && !isBackground && colorValue >= 0 && colorValue < 8) colorValue += 8;
             return _ansiColors[colorValue];
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

                for (int col = 0; col < terminal.Cols && col < bl.Length; col++)
                {
                    var cell = bl[col];
                    if (cell.Width == 0) continue;

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
                        context.FillRectangle(new SolidColorBrush(bgColor), new Rect(xPos, yPos, width, _charHeight));
                    }

                    // Draw text
                    string content = cell.Content;
                    if (!string.IsNullOrEmpty(content))
                    {
                        var typeface = isBold ? new Typeface(_typeface.FontFamily, FontStyle.Normal, FontWeight.Bold) : _typeface;
                        var fgBrush = new SolidColorBrush(fgColor);
                        
                        var formatted = new FormattedText(content, System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight, typeface, FontSize, fgBrush);
                            
                        // Vertically center content within the cell
                        double textVerticalOffset = (_charHeight - formatted.Height) / 2;
                        
                        context.DrawText(formatted, new Point(xPos, yPos + textVerticalOffset));
                    }
                }
            }

            if (_searchMatches.Count > 0)
            {
                RenderSearchMatches(context, buffer, verticalOffset);
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
                        FlowDirection.LeftToRight, _typeface, FontSize, new SolidColorBrush(ForegroundColor));
                    double preeditWidth = preeditFormatted.WidthIncludingTrailingWhitespace;
                    double textVerticalOffset = (_charHeight - preeditFormatted.Height) / 2;
                    
                    // Background for preedit text
                    context.FillRectangle(new SolidColorBrush(BackgroundColor), 
                        new Rect(cursorX, rowY, preeditWidth, _charHeight));
                    
                    // Draw underline to indicate composition
                    var underlineY = rowY + _charHeight - 2;
                    var pen = new Pen(new SolidColorBrush(ForegroundColor), 1);
                    context.DrawLine(pen, new Point(cursorX, underlineY), new Point(cursorX + preeditWidth, underlineY));
                    
                    // Draw preedit text
                    context.DrawText(preeditFormatted, new Point(cursorX, rowY + textVerticalOffset));
                    
                    // Move cursor position after preedit text for display
                    cursorX += preeditWidth;
                }
                
                // Calculate cursor height and position to match text rendering
                // The cursor should match the actual text height (FontSize), not the line height (_charHeight)
                double cursorHeight = FontSize;
                // Center the cursor vertically within the cell, same as text
                double cursorVerticalOffset = (_charHeight - cursorHeight) / 2;
                double cursorY = rowY + cursorVerticalOffset;
                
                var cursorRect = new Rect(cursorX, cursorY, _charWidth, cursorHeight);

                if (IsFocused)
                {
                    // Focused: blinking solid cursor
                    if (_cursorVisible)
                    {
                        context.FillRectangle(new SolidColorBrush(cursorColor), cursorRect);
                    }
                }
                else
                {
                    // Not focused: static hollow cursor (outline only)
                    var pen = new Pen(new SolidColorBrush(cursorColor), 1.5);
                    context.DrawRectangle(null, pen, cursorRect);
                }
            }
        }
    }

    private void RenderSelection(DrawingContext context, TerminalBuffer buffer, double verticalOffset)
    {
        var terminal = Controller?.Terminal;
        if (terminal == null) return;

        var p1 = _selectionStart!.Value;
        var p2 = _selectionEnd!.Value;

        if (p1.Y > p2.Y || (p1.Y == p2.Y && p1.X > p2.X))
        {
            (p1, p2) = (p2, p1);
        }

        int startRowSel = (int)p1.Y;
        int endRowSel = (int)p2.Y;
        int startColSel = (int)p1.X;
        int endColSel = (int)p2.X;

        for (int r = startRowSel; r <= endRowSel; r++)
        {
            if (r < 0 || r >= buffer.Lines.Length) continue;

            var line = buffer.Lines[r];
            int lineLen = terminal.Cols;

            if (line is BufferLine bl)
            {
                lineLen = bl.GetTrimmedLength();
                if (lineLen <= 0) lineLen = 0;
            }

            int c1 = (r == startRowSel) ? startColSel : 0;
            int c2 = (r == endRowSel) ? endColSel : lineLen;

            if (c1 > lineLen) c1 = lineLen;
            if (c2 > lineLen) c2 = lineLen;
            if (c1 >= c2) continue;

            double selX = c1 * _charWidth;
            double selY = verticalOffset + (r - buffer.YDisp) * _charHeight;
            double w = (c2 - c1) * _charWidth;

            if (selY >= 0 && selY < Bounds.Height && w > 0)
            {
                context.FillRectangle(new SolidColorBrush(SelectionBackground), new Rect(selX, selY, w, _charHeight));
            }
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!IsFocused || Controller == null) return;

        if (!string.IsNullOrEmpty(e.Text))
        {
            Controller.SendInput(e.Text);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsFocused || Controller == null)
        {
            base.OnKeyDown(e);
            return;
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

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
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
                _ => null
            };
        }
        else
        {
            sequence = e.Key switch
            {
                Key.Enter => "\r",
                Key.Back => "\x7f",
                Key.Tab => "\t",
                Key.Escape => "\x1b",
                Key.Up => "\x1b[A",
                Key.Down => "\x1b[B",
                Key.Right => "\x1b[C",
                Key.Left => "\x1b[D",
                Key.Home => "\x1b[H",
                Key.End => "\x1b[F",
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

        if (sequence != null)
        {
            Controller.SendInput(sequence);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var point = e.GetPosition(this);
        _selectionStart = GetGridPosition(point);
        _selectionEnd = _selectionStart;
        _isSelecting = true;
        Focus();
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isSelecting)
        {
            var point = e.GetPosition(this);
            _selectionEnd = GetGridPosition(point);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isSelecting = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null) return;

        int scrollAmount = (int)(-e.Delta.Y * 3);
        terminal.Buffer.ScrollLines(scrollAmount);
        UpdateScrollProperties();
        InvalidateVisual();
        e.Handled = true;
    }

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

        return new Point(col, row);
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
                Controller.SendInput(text);
            }
        }
#pragma warning restore CS0618
    }

    private string GetSelectedText()
    {
        if (_selectionStart == null || _selectionEnd == null) return "";
        var terminal = Controller?.Terminal;
        if (terminal?.Buffer == null) return "";

        var p1 = _selectionStart.Value;
        var p2 = _selectionEnd.Value;
        var buffer = terminal.Buffer;

        if (p1.Y > p2.Y || (p1.Y == p2.Y && p1.X > p2.X)) (p1, p2) = (p2, p1);

        int startRow = (int)p1.Y;
        int endRow = (int)p2.Y;
        int startCol = (int)p1.X;
        int endCol = (int)p2.X;

        var sb = new StringBuilder();

        for (int r = startRow; r <= endRow; r++)
        {
            if (r >= buffer.Lines.Length) break;
            var line = buffer.Lines[r];

            string lineText = "";
            if (line is BufferLine bl) lineText = bl.TranslateToString(true);
            else lineText = line?.ToString() ?? "";

            int c1 = 0;
            int c2 = lineText.Length;

            if (r == startRow) c1 = startCol;
            if (r == endRow) c2 = endCol + 1;

            c1 = Math.Max(0, Math.Min(c1, lineText.Length));
            c2 = Math.Max(0, Math.Min(c2, lineText.Length));

            if (c1 < c2)
            {
                if (r == startRow && r == endRow) sb.Append(lineText.Substring(c1, c2 - c1));
                else if (r == startRow)
                {
                    sb.Append(lineText.Substring(c1));
                    sb.AppendLine();
                }
                else if (r == endRow) sb.Append(lineText.Substring(0, c2));
                else sb.AppendLine(lineText);
            }
        }

        return sb.ToString();
    }
}
