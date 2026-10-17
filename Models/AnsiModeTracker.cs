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

    /// <summary>Last reported current working directory from OSC 7 / 1337 / 9;9.</summary>
    public string CurrentDirectory { get; private set; } = string.Empty;

    /// <summary>Fires when the terminal announces a working directory change via OSC.</summary>
    public event Action<string>? CurrentDirectoryChanged;

    // Tail of the previous chunk when a CSI/OSC sequence was split across packets.
    private string _carryOver = "";

    /// <summary>
    /// Feeds a decoded output chunk through a minimal CSI and OSC parser.
    /// Unknown sequences are ignored; the XTerm model still processes them itself.
    /// A sequence split across packets is buffered and completed by the next chunk.
    /// </summary>
    public void Process(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        if (_carryOver.Length > 0)
        {
            data = _carryOver + data;
            _carryOver = "";
        }

        int i = 0;
        while ((i = data.IndexOf(Esc, i)) >= 0)
        {
            // ESC at the very end of the chunk: wait for the next chunk.
            if (i == data.Length - 1)
            {
                _carryOver = data.Substring(i);
                return;
            }

            char next = data[i + 1];
            if (next == '[') // CSI
            {
                int j = i + 2;
                int end = FindCsiEnd(data, j);
                if (end < 0)
                {
                    // Sequence split across chunks: keep the incomplete tail.
                    _carryOver = data.Substring(i);
                    return;
                }

                ParseCsi(data, j, end);
                i = end + 1;
            }
            else if (next == ']') // OSC
            {
                int j = i + 2;
                int end = FindOscEnd(data, j, out int terminatorLength);
                if (end < 0)
                {
                    // If buffer is within reasonable limit, wait for the rest of the OSC sequence
                    if (data.Length - i < 4096)
                    {
                        _carryOver = data.Substring(i);
                        return;
                    }
                    // Malformed or overly long OSC, advance past Esc ]
                    i += 2;
                    continue;
                }

                ParseOsc(data, j, end - j);
                i = end + terminatorLength;
            }
            else
            {
                i += 2;
            }
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

    private static int FindOscEnd(string data, int start, out int terminatorLength)
    {
        terminatorLength = 0;
        for (int j = start; j < data.Length && j <= start + 4096; j++)
        {
            char c = data[j];
            if (c == '\x07') // BEL
            {
                terminatorLength = 1;
                return j;
            }
            if (c == Esc)
            {
                if (j + 1 < data.Length && data[j + 1] == '\\') // ST: ESC \
                {
                    terminatorLength = 2;
                    return j;
                }
                if (j + 1 == data.Length)
                {
                    // ESC is at end of chunk: might be partial ST "ESC \"
                    return -1;
                }
            }
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

    private void ParseOsc(string data, int start, int length)
    {
        if (length <= 0) return;
        var payload = data.Substring(start, length);
        int semi = payload.IndexOf(';');
        if (semi < 0) return;

        var code = payload.Substring(0, semi).Trim();
        var param = payload.Substring(semi + 1);

        string? extractedPath = null;
        if (code == "7")
        {
            extractedPath = ExtractOsc7Path(param);
        }
        else if (code == "1337" && param.StartsWith("CurrentDir=", StringComparison.OrdinalIgnoreCase))
        {
            extractedPath = param.Substring(11).Trim();
        }
        else if (code == "9" && param.StartsWith("9;"))
        {
            var raw = param.Substring(2).Trim().Trim('"');
            if (!string.IsNullOrEmpty(raw)) extractedPath = raw;
        }

        if (!string.IsNullOrEmpty(extractedPath))
        {
            CurrentDirectory = extractedPath;
            CurrentDirectoryChanged?.Invoke(extractedPath);
        }
    }

    private static string? ExtractOsc7Path(string param)
    {
        if (string.IsNullOrWhiteSpace(param)) return null;
        string path = param.Trim();
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(7);
            int slashIdx = path.IndexOf('/');
            if (slashIdx >= 0)
            {
                path = path.Substring(slashIdx);
            }
        }

        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch
        {
            // fallback to raw path
        }

        // Convert Windows /C:/Users/... to C:/Users/... if present
        if (path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':')
        {
            path = path.Substring(1);
        }

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
