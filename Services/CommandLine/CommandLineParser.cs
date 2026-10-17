using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using JxTerminal.Models;

namespace JxTerminal.Services.CommandLine;

/// <summary>
/// Parsed command line options for launching JxTerminal directly into a session.
/// Fully compatible with PuTTY command-line syntax and OpenSSH conventions.
/// </summary>
public class CommandLineLaunchOptions
{
    public bool HasConnection => TargetConnection != null || !string.IsNullOrWhiteSpace(SessionName);

    /// <summary>Explicit connection configured directly via CLI arguments.</summary>
    public SavedConnection? TargetConnection { get; set; }

    /// <summary>Saved session name to load from the stored host list (e.g. from -load or @session).</summary>
    public string? SessionName { get; set; }

    /// <summary>Command or script text to send to the terminal once connected (e.g. from -m).</summary>
    public string? InitialCommand { get; set; }

    /// <summary>User-specified overrides to apply when loading a saved session.</summary>
    public Action<SavedConnection>? Overrides { get; set; }

    /// <summary>Forces launching in a new window/process rather than reusing an existing running instance.</summary>
    public bool ForceNewWindow { get; set; }

    /// <summary>Forces reusing an existing running instance even if settings default to new window.</summary>
    public bool ForceReuseWindow { get; set; }

    public bool ShowHelp { get; set; }
}

/// <summary>
/// Parser supporting PuTTY CLI syntax (-ssh, -telnet, -serial, -P, -l, -pw, -i, -load, -sercfg, @session)
/// and OpenSSH syntax (ssh user@host -p port -i key).
/// </summary>
public static class CommandLineParser
{
    public static CommandLineLaunchOptions Parse(string[]? args)
    {
        var options = new CommandLineLaunchOptions();
        if (args == null || args.Length == 0) return options;

        var protocol = ConnectionType.Ssh;

        string? host = null;
        int? port = null;
        string? username = null;
        string? password = null;
        string? keyFilePath = null;
        string? sessionName = null;
        string? initialCommand = null;

        // Serial properties
        string? serialPort = null;
        int baudRate = 9600;
        int dataBits = 8;
        Parity parity = Parity.None;
        StopBits stopBits = StopBits.One;
        Handshake handshake = Handshake.None;

        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var rawArg = args[i].Trim();
            if (string.IsNullOrEmpty(rawArg)) continue;

            // Normalize prefix (- or / or --)
            string flag = rawArg;
            string? inlineValue = null;

            if (flag.StartsWith("--", StringComparison.Ordinal))
            {
                flag = flag[2..];
            }
            else if (flag.StartsWith('-') || flag.StartsWith('/'))
            {
                flag = flag[1..];
            }
            else if (flag.StartsWith('@') && flag.Length > 1)
            {
                // PuTTY shorthand: @session_name
                sessionName = flag[1..];
                continue;
            }
            else
            {
                // Positional argument
                positional.Add(rawArg);
                continue;
            }

            // Split inline values like -P:22 or -l=admin or -P22
            int colonIdx = flag.IndexOfAny([':', '=']);
            if (colonIdx > 0)
            {
                inlineValue = flag[(colonIdx + 1)..];
                flag = flag[..colonIdx];
            }

            string NextValue()
            {
                if (!string.IsNullOrEmpty(inlineValue)) return inlineValue;
                if (i + 1 < args.Length)
                {
                    return args[++i].Trim();
                }
                return "";
            }

            switch (flag.ToLowerInvariant())
            {
                case "ssh":
                    protocol = ConnectionType.Ssh;
                    break;
                case "telnet":
                    protocol = ConnectionType.Telnet;
                    break;
                case "serial":
                    protocol = ConnectionType.Serial;
                    break;
                case "raw":
                    protocol = ConnectionType.Telnet;
                    break;
                case "p":
                    // PuTTY uses -P for port, OpenSSH uses -p
                    if (int.TryParse(NextValue(), out int p)) port = p;
                    break;
                case "l":
                    username = NextValue();
                    break;
                case "pw":
                    password = NextValue();
                    break;
                case "i":
                    keyFilePath = NextValue();
                    break;
                case "load":
                    sessionName = NextValue();
                    break;
                case "m":
                    var scriptArg = NextValue();
                    if (!string.IsNullOrWhiteSpace(scriptArg))
                    {
                        if (File.Exists(scriptArg))
                        {
                            try { initialCommand = File.ReadAllText(scriptArg); }
                            catch { initialCommand = scriptArg; }
                        }
                        else
                        {
                            initialCommand = scriptArg;
                        }
                    }
                    break;
                case "sercfg":
                    ParseSerialConfig(NextValue(), ref baudRate, ref dataBits, ref parity, ref stopBits, ref handshake);
                    break;
                case "new-window":
                case "newwindow":
                case "new-instance":
                    options.ForceNewWindow = true;
                    break;
                case "reuse-window":
                case "reusewindow":
                case "reuse-instance":
                    options.ForceReuseWindow = true;
                    break;
                case "h":
                case "help":
                case "?":
                    options.ShowHelp = true;
                    break;
                default:
                    // Support attached flags like -P2222 or -ladmin
                    if (flag.Length > 1 && (flag[0] == 'P' || flag[0] == 'p') && int.TryParse(flag[1..], out int compactPort))
                    {
                        port = compactPort;
                    }
                    else if (flag.Length > 1 && flag[0] == 'l')
                    {
                        username = flag[1..];
                    }
                    break;
            }
        }

        // Process positional arguments
        int posIndex = 0;
        if (positional.Count > 0 && (positional[0].Equals("ssh", StringComparison.OrdinalIgnoreCase) ||
                                     positional[0].Equals("telnet", StringComparison.OrdinalIgnoreCase) ||
                                     positional[0].Equals("serial", StringComparison.OrdinalIgnoreCase)))
        {
            if (positional[0].Equals("telnet", StringComparison.OrdinalIgnoreCase)) protocol = ConnectionType.Telnet;
            else if (positional[0].Equals("serial", StringComparison.OrdinalIgnoreCase)) protocol = ConnectionType.Serial;
            else protocol = ConnectionType.Ssh;
            posIndex++;
        }

        if (protocol == ConnectionType.Serial)
        {
            if (posIndex < positional.Count)
            {
                serialPort = positional[posIndex++];
            }
            if (posIndex < positional.Count && int.TryParse(positional[posIndex], out int br))
            {
                baudRate = br;
                posIndex++;
            }
        }
        else
        {
            if (posIndex < positional.Count)
            {
                var target = positional[posIndex++];
                // Check if target is user@host or host:port
                if (target.Contains('@'))
                {
                    var atParts = target.Split('@', 2);
                    if (string.IsNullOrEmpty(username)) username = atParts[0];
                    target = atParts[1];
                }

                if (target.Contains(':') && !target.Contains("]:"))
                {
                    var colonParts = target.Split(':', 2);
                    host = colonParts[0];
                    if (port == null && int.TryParse(colonParts[1], out int targetPort))
                    {
                        port = targetPort;
                    }
                }
                else
                {
                    host = target;
                }
            }

            // Next positional can be port number (e.g. `putty.exe host 22`)
            if (posIndex < positional.Count && port == null && int.TryParse(positional[posIndex], out int posPort))
            {
                port = posPort;
            }
        }

        options.SessionName = sessionName;
        options.InitialCommand = initialCommand;

        // Build overrides action for -load
        if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password) || port.HasValue || !string.IsNullOrEmpty(keyFilePath))
        {
            options.Overrides = conn =>
            {
                if (!string.IsNullOrEmpty(username)) conn.Username = username;
                if (!string.IsNullOrEmpty(password))
                {
                    conn.Password = password;
                    conn.AuthenticationType = SshAuthenticationType.Password;
                }
                if (port.HasValue) conn.Port = port.Value;
                if (!string.IsNullOrEmpty(keyFilePath))
                {
                    conn.KeyFilePath = keyFilePath;
                    conn.AuthenticationType = SshAuthenticationType.Key;
                }
            };
        }

        // Build connection if host or serial port was specified
        if (!string.IsNullOrWhiteSpace(host) || !string.IsNullOrWhiteSpace(serialPort))
        {
            var conn = new SavedConnection();
            conn.Type = protocol;

            if (protocol == ConnectionType.Serial)
            {
                conn.SerialPortName = serialPort ?? "COM1";
                conn.BaudRate = baudRate;
                conn.DataBits = dataBits;
                conn.Parity = parity;
                conn.StopBits = stopBits;
                conn.Handshake = handshake;
                conn.Name = $"{conn.SerialPortName} ({conn.BaudRate})";
            }
            else
            {
                conn.Host = host ?? "localhost";
                conn.Port = port ?? (protocol == ConnectionType.Telnet ? 23 : 22);
                conn.Username = username ?? "";
                conn.Password = password ?? "";
                conn.KeyFilePath = keyFilePath ?? "";

                if (!string.IsNullOrEmpty(keyFilePath))
                {
                    conn.AuthenticationType = SshAuthenticationType.Key;
                }
                else if (!string.IsNullOrEmpty(password))
                {
                    conn.AuthenticationType = SshAuthenticationType.Password;
                }
                else
                {
                    conn.AuthenticationType = SshAuthenticationType.Ask;
                }

                conn.Name = string.IsNullOrEmpty(conn.Username)
                    ? $"{conn.Host}:{conn.Port}"
                    : $"{conn.Username}@{conn.Host}:{conn.Port}";
            }

            options.TargetConnection = conn;
        }

        return options;
    }

    /// <summary>
    /// Parses PuTTY-style serial configuration string: baud[,databits[,parity[,stopbits[,flow]]]]
    /// e.g. 115200,8,n,1,N
    /// </summary>
    public static void ParseSerialConfig(
        string config,
        ref int baudRate,
        ref int dataBits,
        ref Parity parity,
        ref StopBits stopBits,
        ref Handshake handshake)
    {
        if (string.IsNullOrWhiteSpace(config)) return;
        var parts = config.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length > 0 && int.TryParse(parts[0], out int br) && br > 0)
        {
            baudRate = br;
        }
        if (parts.Length > 1 && int.TryParse(parts[1], out int db) && db is >= 5 and <= 8)
        {
            dataBits = db;
        }
        if (parts.Length > 2 && parts[2].Length > 0)
        {
            parity = char.ToLowerInvariant(parts[2][0]) switch
            {
                'o' => Parity.Odd,
                'e' => Parity.Even,
                'm' => Parity.Mark,
                's' => Parity.Space,
                _ => Parity.None
            };
        }
        if (parts.Length > 3 && parts[3].Length > 0)
        {
            stopBits = parts[3] switch
            {
                "1.5" => StopBits.OnePointFive,
                "2" => StopBits.Two,
                _ => StopBits.One
            };
        }
        if (parts.Length > 4 && parts[4].Length > 0)
        {
            handshake = char.ToLowerInvariant(parts[4][0]) switch
            {
                'x' => Handshake.XOnXOff,
                'r' => Handshake.RequestToSend,
                _ => Handshake.None
            };
        }
    }

    /// <summary>
    /// Resolves the effective SavedConnection from parsed options, looking up saved sessions
    /// in the provided groups if -load or @session was used.
    /// </summary>
    public static SavedConnection? ResolveConnection(
        CommandLineLaunchOptions options,
        IEnumerable<ConnectionGroup>? groups = null)
    {
        if (options == null || !options.HasConnection) return null;

        if (!string.IsNullOrWhiteSpace(options.SessionName))
        {
            // Look up session by name across groups
            SavedConnection? matched = null;
            if (groups != null)
            {
                foreach (var group in groups)
                {
                    matched = group.Connections.FirstOrDefault(c =>
                        c.Name.Equals(options.SessionName, StringComparison.OrdinalIgnoreCase) ||
                        c.Host.Equals(options.SessionName, StringComparison.OrdinalIgnoreCase));
                    if (matched != null) break;
                }
            }

            if (matched != null)
            {
                var resolved = matched.Clone();
                options.Overrides?.Invoke(resolved);
                return resolved;
            }

            // If session name was not found in saved list, treat as hostname fallback
            var fallback = new SavedConnection
            {
                Host = options.SessionName,
                Port = 22,
                Type = ConnectionType.Ssh,
                Name = options.SessionName
            };
            options.Overrides?.Invoke(fallback);
            return fallback;
        }

        return options.TargetConnection;
    }
}
