using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using JxTerminal.Models;
using System.Text;
using System.Threading.Tasks;
using XTerm.Buffer;
using XTerm.Common;

namespace JxTerminal.Views;

public class TerminalControl : Control
{
    private XTerm.Terminal _terminal;
    private ITerminalSession? _session;
    private Typeface _typeface;
    private double _charWidth;
    private double _charHeight;

    // Selection state
    private Point? _selectionStart;
    private Point? _selectionEnd;
    private bool _isSelecting;

    // ANSI 256 color palette
    private static readonly Color[] AnsiColors = CreateAnsiColorPalette();

    // Default colors
    private static readonly Color DefaultForeground = Color.FromRgb(229, 229, 229);
    private static readonly Color DefaultBackground = Color.FromRgb(0, 0, 0);

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

    public static readonly StyledProperty<ITerminalSession?> SessionProperty =
        AvaloniaProperty.Register<TerminalControl, ITerminalSession?>(nameof(Session));

    public ITerminalSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
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

    public TerminalControl()
    {
        Focusable = true;

        // Create terminal with configured scrollback
        var options = new XTerm.Options.TerminalOptions
        {
            Scrollback = ScrollbackLines,
            Cols = 80,
            Rows = 24
        };
        _terminal = new XTerm.Terminal(options);

        UpdateTypeface();

        // Context Menu
        var contextMenu = new ContextMenu();
        var copyItem = new MenuItem { Header = "Copy" };
        copyItem.Click += async (s, e) => await CopyText();
        var pasteItem = new MenuItem { Header = "Paste" };
        pasteItem.Click += async (s, e) => await PasteText();

        contextMenu.Items.Add(copyItem);
        contextMenu.Items.Add(pasteItem);

        this.ContextMenu = contextMenu;
    }

    /// <summary>
    /// Creates the standard ANSI 256-color palette.
    /// </summary>
    private static Color[] CreateAnsiColorPalette()
    {
        var palette = new Color[256];

        // Standard 16 colors (0-15)
        palette[0] = Color.FromRgb(0, 0, 0);       // Black
        palette[1] = Color.FromRgb(205, 0, 0);     // Red
        palette[2] = Color.FromRgb(0, 205, 0);     // Green
        palette[3] = Color.FromRgb(205, 205, 0);   // Yellow
        palette[4] = Color.FromRgb(0, 0, 238);     // Blue
        palette[5] = Color.FromRgb(205, 0, 205);   // Magenta
        palette[6] = Color.FromRgb(0, 205, 205);   // Cyan
        palette[7] = Color.FromRgb(229, 229, 229); // White
        palette[8] = Color.FromRgb(127, 127, 127); // Bright Black (Gray)
        palette[9] = Color.FromRgb(255, 0, 0);     // Bright Red
        palette[10] = Color.FromRgb(0, 255, 0);    // Bright Green
        palette[11] = Color.FromRgb(255, 255, 0);  // Bright Yellow
        palette[12] = Color.FromRgb(92, 92, 255);  // Bright Blue
        palette[13] = Color.FromRgb(255, 0, 255);  // Bright Magenta
        palette[14] = Color.FromRgb(0, 255, 255);  // Bright Cyan
        palette[15] = Color.FromRgb(255, 255, 255);// Bright White

        // 216 color cube (16-231): 6x6x6 cube
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    int index = 16 + (36 * r) + (6 * g) + b;
                    byte rv = (byte)(r == 0 ? 0 : 55 + r * 40);
                    byte gv = (byte)(g == 0 ? 0 : 55 + g * 40);
                    byte bv = (byte)(b == 0 ? 0 : 55 + b * 40);
                    palette[index] = Color.FromRgb(rv, gv, bv);
                }
            }
        }

        // Grayscale (232-255): 24 shades
        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            palette[232 + i] = Color.FromRgb(v, v, v);
        }

        return palette;
    }

    public void AttachSession(ITerminalSession session)
    {
        _session = session;
        if (_session != null)
        {
            _session.DataReceived += OnDataReceived;
            _session.Disconnected += OnDisconnected;
        }
        ResizeTerminal();

        // Auto-focus when session is attached
        Dispatcher.UIThread.InvokeAsync(() => Focus());
    }

    public void DetachSession()
    {
        if (_session != null)
        {
            _session.DataReceived -= OnDataReceived;
            _session.Disconnected -= OnDisconnected;
            _session = null;
        }
    }

    private void OnDisconnected()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _terminal.Write("\r\n[DISCONNECTED]\r\n");
            InvalidateVisual();
        });
    }

    private void OnDataReceived(string data)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _terminal.Write(data);
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
        else if (change.Property == SessionProperty)
        {
            if (change.NewValue is ITerminalSession newSession)
            {
                AttachSession(newSession);
            }
        }
        else if (change.Property == ScrollPositionProperty)
        {
            // External scrollbar changed position
            if (_terminal != null)
            {
                int newYDisp = (int)ScrollPosition;
                if (newYDisp != _terminal.Buffer.YDisp)
                {
                    _terminal.Buffer.ViewportY = newYDisp;
                    InvalidateVisual();
                }
            }
        }
        else if (change.Property == BackgroundColorProperty ||
                 change.Property == ForegroundColorProperty ||
                 change.Property == BackgroundImageProperty ||
                 change.Property == BackgroundImageOpacityProperty ||
                 change.Property == EnableColorsProperty)
        {
            InvalidateVisual();
        }
    }

    private void UpdateScrollProperties()
    {
        if (_terminal == null) return;

        var buffer = _terminal.Buffer;
        // YBase is the index of the first line that could scroll off (scrollback start)
        // Lines.Length is total lines, including scrollback
        // Rows is the visible viewport lines count
        double maximum = Math.Max(0, buffer.YBase);
        double viewport = _terminal.Rows;
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

        // Measure character size (assuming monospaced)
        var formatted = new FormattedText("M", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, FontSize, Brushes.White);
        _charWidth = formatted.WidthIncludingTrailingWhitespace;
        _charHeight = formatted.Height;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ResizeTerminal();
    }

    private void ResizeTerminal()
    {
        if (_charWidth <= 0 || _charHeight <= 0) return;

        var cols = (int)(Bounds.Width / _charWidth);
        var rows = (int)(Bounds.Height / _charHeight);

        if (cols > 0 && rows > 0 && (cols != _terminal.Cols || rows != _terminal.Rows))
        {
            int oldRows = _terminal.Rows;
            _terminal.Resize(cols, rows);

            // When height increases, adjust viewport to show cursor correctly
            var buffer = _terminal.Buffer;
            if (rows > oldRows && buffer != null)
            {
                // Ensure cursor is visible and viewport is at the right position
                // When growing, we want to scroll up to show more scrollback if available,
                // but keep the cursor line visible
                int cursorAbsoluteY = buffer.YBase + buffer.Y;
                int maxYDisp = Math.Max(0, buffer.YBase);

                // Adjust YDisp to keep cursor at bottom of screen if it was there
                if (cursorAbsoluteY >= buffer.YDisp + oldRows - 1)
                {
                    // Cursor was at/near bottom, keep it visible
                    buffer.ViewportY = Math.Max(0, Math.Min(cursorAbsoluteY - rows + 1, maxYDisp));
                }
            }

            UpdateScrollProperties();
            _session?.Resize(rows, cols, (int)Bounds.Width, (int)Bounds.Height);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Converts color from XTerm AttributeData to Avalonia Color.
    /// </summary>
    private Color GetColor(int colorValue, int colorMode, Color defaultColor, bool isBold = false)
    {
        // Default color (usually indicated by color value < 0 or special mode)
        if (colorValue < 0)
        {
            return defaultColor;
        }

        // Palette 256 mode
        if (colorMode == (int)ColorMode.Palette256)
        {
            // If bold and standard color (0-7), use bright version (8-15) for foreground
            if (isBold && colorValue >= 0 && colorValue < 8)
            {
                colorValue += 8;
            }
            if (colorValue >= 0 && colorValue < 256)
            {
                return AnsiColors[colorValue];
            }
        }
        // RGB mode - value is 0xRRGGBB
        else if (colorMode == (int)ColorMode.RGB)
        {
            byte r = (byte)((colorValue >> 16) & 0xFF);
            byte g = (byte)((colorValue >> 8) & 0xFF);
            byte b = (byte)(colorValue & 0xFF);
            return Color.FromRgb(r, g, b);
        }

        // Fallback for standard colors (often Mode 0 or similar but with valid index)
        if (colorValue >= 0 && colorValue < 256)
        {
             if (isBold && colorValue >= 0 && colorValue < 8)
             {
                 colorValue += 8;
             }
             return AnsiColors[colorValue];
        }

        return defaultColor;
    }

    public override void Render(DrawingContext context)
    {
        // Draw background
        context.DrawRectangle(new SolidColorBrush(BackgroundColor), null, new Rect(0, 0, Bounds.Width, Bounds.Height));

        // Draw Background Image
        if (BackgroundImage != null)
        {
            using (context.PushOpacity(BackgroundImageOpacity))
            {
                context.DrawImage(BackgroundImage, new Rect(0, 0, Bounds.Width, Bounds.Height));
            }
        }

        if (_terminal == null) return;

        var buffer = _terminal.Buffer;

        // Render each line in the viewport
        for (int row = 0; row < _terminal.Rows; row++)
        {
            int lineIndex = buffer.YDisp + row;
            if (lineIndex >= buffer.Lines.Length) break;

            var bufferLine = buffer.Lines[lineIndex];
            if (bufferLine is not BufferLine bl) continue;

            double yPos = row * _charHeight;

            // Render cells with proper color support
            int col = 0;
            while (col < _terminal.Cols && col < bl.Length)
            {
                var cell = bl[col];

                // Skip placeholder cells (width 0) that follow wide characters
                if (cell.Width == 0)
                {
                    col++;
                    continue;
                }

                // Get cell attributes
                var attrs = cell.Attributes;
                bool isBold = attrs.IsBold();
                bool isInverse = attrs.IsInverse();

                int fgColorValue = attrs.GetFgColor();
                int bgColorValue = attrs.GetBgColor();
                int fgColorMode = attrs.GetFgColorMode();
                int bgColorMode = attrs.GetBgColorMode();

                Color fgColor = GetColor(fgColorValue, fgColorMode, ForegroundColor, isBold);
                Color bgColor = GetColor(bgColorValue, bgColorMode, BackgroundColor);

                // Handle inverse colors
                if (isInverse)
                {
                    (fgColor, bgColor) = (bgColor, fgColor);
                }

                // Batch consecutive cells with same attributes
                var textBuilder = new StringBuilder();
                int startCol = col;
                int cellCount = 0;

                while (col < _terminal.Cols && col < bl.Length)
                {
                    var currentCell = bl[col];

                    // Stop if width=0 placeholder
                    if (currentCell.Width == 0)
                    {
                        col++;
                        continue;
                    }

                    // Check if attributes match
                    var currentAttrs = currentCell.Attributes;

                    if (EnableColors)
                    {
                        if (currentAttrs.GetFgColor() != attrs.GetFgColor() ||
                            currentAttrs.GetBgColor() != attrs.GetBgColor() ||
                            currentAttrs.GetFgColorMode() != attrs.GetFgColorMode() ||
                            currentAttrs.GetBgColorMode() != attrs.GetBgColorMode() ||
                            currentAttrs.IsBold() != isBold ||
                            currentAttrs.IsInverse() != isInverse)
                        {
                            break;
                        }
                    }
                    else
                    {
                        // In monochrome, only inverse changes rendering
                        if (currentAttrs.IsInverse() != isInverse)
                        {
                            break;
                        }
                    }

                    // Append content
                    string content = currentCell.Content;
                    if (!string.IsNullOrEmpty(content))
                    {
                        textBuilder.Append(content);
                    }
                    cellCount += currentCell.Width;
                    col += currentCell.Width; // Skip placeholder cells for wide chars
                }

                string text = textBuilder.ToString();
                if (string.IsNullOrEmpty(text)) continue;

                double xPos = startCol * _charWidth;
                double width = cellCount * _charWidth;

                // Draw background if not default
                if (bgColor != BackgroundColor)
                {
                    context.FillRectangle(new SolidColorBrush(bgColor), new Rect(xPos, yPos, width, _charHeight));
                }

                // Draw text
                var typeface = isBold ? new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold) : _typeface;
                var fgBrush = new SolidColorBrush(fgColor);
                var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, FontSize, fgBrush);
                context.DrawText(formatted, new Point(xPos, yPos));
            }
        }

        // Render Selection
        if (_selectionStart.HasValue && _selectionEnd.HasValue)
        {
            RenderSelection(context, buffer);
        }

        // Draw cursor
        // absoluteCursorY = YBase + Y (buffer.Y is relative to active screen starting at YBase)
        int absoluteCursorY = buffer.YBase + buffer.Y;
        int viewportY = buffer.YDisp;
        int screenY = absoluteCursorY - viewportY;

        // Only draw cursor if visible in viewport
        if (screenY >= 0 && screenY < _terminal.Rows)
        {
            double cursorX = buffer.X * _charWidth;
            double cursorY = screenY * _charHeight;

            // Use bright green cursor for visibility
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(0, 255, 0)), new Rect(cursorX, cursorY, _charWidth, _charHeight));
        }
    }

    private void RenderSelection(DrawingContext context, TerminalBuffer buffer)
    {
        var p1 = _selectionStart!.Value;
        var p2 = _selectionEnd!.Value;

        // Normalize selection coordinates
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
            int lineLen = _terminal.Cols;

            if (line is BufferLine bl)
            {
                lineLen = bl.GetTrimmedLength();
                if (lineLen <= 0) lineLen = 0;
            }

            int c1 = (r == startRowSel) ? startColSel : 0;
            int c2 = (r == endRowSel) ? endColSel : lineLen;

            // Clamp to actual content
            if (c1 > lineLen) c1 = lineLen;
            if (c2 > lineLen) c2 = lineLen;
            if (c1 >= c2) continue;

            double selX = c1 * _charWidth;
            double selY = (r - buffer.YDisp) * _charHeight;
            double w = (c2 - c1) * _charWidth;

            if (selY >= 0 && selY < Bounds.Height && w > 0)
            {
                context.FillRectangle(new SolidColorBrush(Colors.White, 0.3), new Rect(selX, selY, w, _charHeight));
            }
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        // Only process if focused and have a session
        if (!IsFocused || _session == null)
            return;

        if (!string.IsNullOrEmpty(e.Text))
        {
            _session.Send(e.Text);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Only process if focused and have a session
        if (!IsFocused || _session == null)
        {
            base.OnKeyDown(e);
            return;
        }

        string? sequence = null;

        // Handle Ctrl+key combinations
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            sequence = e.Key switch
            {
                Key.C => "\x03",  // Ctrl+C (SIGINT)
                Key.D => "\x04",  // Ctrl+D (EOF)
                Key.Z => "\x1a",  // Ctrl+Z (SIGTSTP)
                Key.L => "\x0c",  // Ctrl+L (clear screen)
                Key.A => "\x01",  // Ctrl+A (beginning of line)
                Key.E => "\x05",  // Ctrl+E (end of line)
                Key.K => "\x0b",  // Ctrl+K (kill to end of line)
                Key.U => "\x15",  // Ctrl+U (kill to beginning of line)
                Key.W => "\x17",  // Ctrl+W (kill word)
                Key.R => "\x12",  // Ctrl+R (reverse search)
                _ => null
            };
        }
        else
        {
            // Handle special keys with ANSI escape sequences
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
            _session.Send(sequence);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Only start selection on Left Click
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var point = e.GetPosition(this);
        _selectionStart = GetGridPosition(point);
        _selectionEnd = _selectionStart;
        _isSelecting = true;

        // Focus the control for keyboard input
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

        var buffer = _terminal?.Buffer;
        if (buffer == null) return;

        // Scroll by 3 lines per wheel notch
        int scrollAmount = (int)(-e.Delta.Y * 3);
        buffer.ScrollLines(scrollAmount);
        UpdateScrollProperties();
        InvalidateVisual();

        e.Handled = true;
    }

    private Point GetGridPosition(Point p)
    {
        if (_charWidth <= 0 || _charHeight <= 0) return new Point(0, 0);

        var buffer = _terminal?.Buffer;
        if (buffer == null) return new Point(0, 0);

        int col = (int)(p.X / _charWidth);
        int row = (int)(p.Y / _charHeight) + buffer.YDisp;

        // Clamp to valid range
        col = Math.Max(0, Math.Min(col, _terminal?.Cols - 1 ?? 0));
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
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
        }
    }

    private async Task PasteText()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null)
        {
            var text = await topLevel.Clipboard.GetTextAsync() ?? "";
            if (!string.IsNullOrEmpty(text))
            {
                _session?.Send(text);
            }
        }
    }

    private string GetSelectedText()
    {
        if (_selectionStart == null || _selectionEnd == null) return "";

        var p1 = _selectionStart.Value;
        var p2 = _selectionEnd.Value;

        var buffer = _terminal?.Buffer;
        if (buffer == null) return "";

        // Normalize: p1 should be before p2
        if (p1.Y > p2.Y || (p1.Y == p2.Y && p1.X > p2.X))
        {
            (p1, p2) = (p2, p1);
        }

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
            if (line is BufferLine bl)
            {
                lineText = bl.TranslateToString(true);
            }
            else
            {
                lineText = line?.ToString() ?? "";
            }

            int c1 = 0;
            int c2 = lineText.Length;

            if (r == startRow) c1 = startCol;
            if (r == endRow) c2 = endCol + 1;

            c1 = Math.Max(0, Math.Min(c1, lineText.Length));
            c2 = Math.Max(0, Math.Min(c2, lineText.Length));

            if (c1 < c2)
            {
                if (r == startRow && r == endRow)
                {
                    sb.Append(lineText.Substring(c1, c2 - c1));
                }
                else if (r == startRow)
                {
                    sb.Append(lineText.Substring(c1));
                    sb.AppendLine();
                }
                else if (r == endRow)
                {
                    sb.Append(lineText.Substring(0, c2));
                }
                else
                {
                    sb.AppendLine(lineText);
                }
            }
        }

        return sb.ToString();
    }
}
