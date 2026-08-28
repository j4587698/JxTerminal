using System;

namespace JxTerminal.Models;

/// <summary>
/// Tracks DEC private modes announced by the remote application through
/// CSI ? Ps ; Ps h / l sequences in the output stream. The XTerm screen model
/// ignores them, but the input side needs to know about mouse reporting,
/// bracketed paste and application cursor keys.
/// </summary>
public class AnsiModeTracker
{
    private const char Esc = '\x1b';

    public bool ApplicationCursorKeys { get; private set; }
    public bool MouseNormal { get; private set; }
    public bool MouseButtonEvent { get; private set; }
    public bool MouseAnyEvent { get; private set; }
    public bool SgrMouse { get; private set; }
    public bool BracketedPaste { get; private set; }

    /// <summary>True when any xterm mouse reporting mode is active.</summary>
    public bool MouseTracking => MouseNormal || MouseButtonEvent || MouseAnyEvent;

    /// <summary>
    /// Feeds a decoded output chunk through a minimal CSI parser.
    /// Unknown sequences are ignored; the XTerm model still processes them itself.
    /// </summary>
    public void Process(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        int i = 0;
        while ((i = data.IndexOf(Esc, i)) >= 0 && i < data.Length - 1)
        {
            // We only care about CSI: ESC [
            if (data[i + 1] != '[')
            {
                i += 2;
                continue;
            }

            int j = i + 2;
            int end = FindCsiEnd(data, j);
            if (end < 0) break; // sequence split across chunks

            ParseCsi(data, j, end);
            i = end + 1;
        }
    }

    private static int FindCsiEnd(string data, int start)
    {
        for (int j = start; j < data.Length && j <= start + 32; j++)
        {
            char c = data[j];
            if (c >= 0x40 && c <= 0x7E) return j; // final byte
        }
        return -1;
    }

    private void ParseCsi(string data, int start, int end)
    {
        if (data[start] != '?') return; // only DEC private modes
        char final = data[end];
        if (final != 'h' && final != 'l') return;

        var span = data.Substring(start + 1, end - start - 1);
        foreach (var part in span.Split(';'))
        {
            switch (part)
            {
                case "1": ApplicationCursorKeys = final == 'h'; break;
                case "1000": MouseNormal = final == 'h'; break;
                case "1002": MouseButtonEvent = final == 'h'; break;
                case "1003": MouseAnyEvent = final == 'h'; break;
                case "1006": SgrMouse = final == 'h'; break;
                case "2004": BracketedPaste = final == 'h'; break;
            }
        }
    }
}
