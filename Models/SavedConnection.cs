using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using JxTerminal.Services;
using TinyDb.Attributes;

namespace JxTerminal.Models;

public enum PingStatus
{
    None,
    Pinging,
    Success,
    Timeout
}

public enum PingLevel
{
    Pinging,
    Fast,
    Medium,
    Slow,
    Timeout
}

public class SavedConnection : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Connection";
    public ConnectionType Type { get; set; } = ConnectionType.Ssh;
    
    // SSH Specific
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";

    [JsonIgnore]
    [BsonIgnore]
    public string Password { get; set; } = "";

    /// <summary>
    /// True when a stored/ imported ciphertext failed to decrypt (e.g. the file came from
    /// another machine whose master key differs). The password is empty in that case; the
    /// flag lets the UI tell the user instead of silently dropping it.
    /// </summary>
    [JsonIgnore]
    public bool PasswordDecryptFailed { get; private set; }

    // AOT-friendly serialization property for Password
    public string EncryptedPassword
    {
        get => SecurityService.Encrypt(Password);
        set
        {
            var decrypted = SecurityService.Decrypt(value);
            PasswordDecryptFailed = !string.IsNullOrEmpty(value) && decrypted.Length == 0;
            Password = decrypted;
        }
    }

    public SshAuthenticationType AuthenticationType { get; set; } = SshAuthenticationType.Password;
    public string KeyFilePath { get; set; } = "";
    public Guid? KeyStoreId { get; set; }
    
    [JsonIgnore]
    [BsonIgnore]
    public string Passphrase { get; set; } = ""; // For private key

    // AOT-friendly serialization property for Passphrase
    public string EncryptedPassphrase
    {
        get => SecurityService.Encrypt(Passphrase);
        set => Passphrase = SecurityService.Decrypt(value);
    }
    
    // Serial Specific
    public string SerialPortName { get; set; } = "";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public System.IO.Ports.Parity Parity { get; set; } = System.IO.Ports.Parity.None;
    public System.IO.Ports.StopBits StopBits { get; set; } = System.IO.Ports.StopBits.One;
    public System.IO.Ports.Handshake Handshake { get; set; } = System.IO.Ports.Handshake.None;

    // Grouping
    public string GroupName { get; set; } = "Default";

    // Local Specific (Optional: Shell path, e.g., /bin/bash, powershell)
    public string ShellPath { get; set; } = ""; 

    /// <summary>
    /// Human readable target shown on host cards, e.g. "root@10.0.0.5:22", "COM3 @ 9600" or "Local".
    /// </summary>
    [JsonIgnore]
    public string DisplayTarget => Type switch
    {
        ConnectionType.Local => string.IsNullOrWhiteSpace(ShellPath) ? "Local shell" : ShellPath,
        ConnectionType.Serial => $"{SerialPortName} @ {BaudRate} {DataBits}{ParityLetter}{StopBitsLabel}",
        _ => string.IsNullOrWhiteSpace(Username) ? $"{Host}:{Port}" : $"{Username}@{Host}:{Port}"
    };

    /// <summary>
    /// Connection endpoint without username prefix (e.g. "38.76.219.52:22", "COM3 @ 9600" or "PowerShell").
    /// </summary>
    [JsonIgnore]
    public string EndpointDisplay => Type switch
    {
        ConnectionType.Local => string.IsNullOrWhiteSpace(ShellPath) ? "PowerShell" : ShellPath,
        ConnectionType.Serial => $"{SerialPortName} • {BaudRate} bps",
        _ => string.IsNullOrWhiteSpace(Host) ? "-" : $"{Host}:{Port}"
    };

    /// <summary>
    /// True if this connection specifies a username.
    /// </summary>
    [JsonIgnore]
    public bool HasUsername => !string.IsNullOrWhiteSpace(Username) && Type is ConnectionType.Ssh or ConnectionType.Telnet;

    /// <summary>
    /// Authentication type badge label (e.g. "密码", "私钥", "询问").
    /// </summary>
    [JsonIgnore]
    public string AuthBadgeLabel
    {
        get
        {
            var l = LocalizationService.Instance;
            return Type switch
            {
                ConnectionType.Ssh => AuthenticationType switch
                {
                    SshAuthenticationType.Key => l["PrivateKey"],
                    SshAuthenticationType.Ask => l["AskInteractive"],
                    _ => l["Password"]
                },
                _ => string.Empty
            };
        }
    }

    /// <summary>
    /// True if this connection has an authentication badge to show.
    /// </summary>
    [JsonIgnore]
    public bool HasAuthBadge => Type == ConnectionType.Ssh;

    [JsonIgnore]
    public Material.Icons.MaterialIconKind AuthBadgeIcon => AuthenticationType switch
    {
        SshAuthenticationType.Key => Material.Icons.MaterialIconKind.KeyVariant,
        SshAuthenticationType.Ask => Material.Icons.MaterialIconKind.HelpCircleOutline,
        _ => Material.Icons.MaterialIconKind.LockOutline
    };

    private PingStatus _pingStatus = PingStatus.None;
    [JsonIgnore]
    public PingStatus PingStatus
    {
        get => _pingStatus;
        set
        {
            if (SetProperty(ref _pingStatus, value))
            {
                OnPropertyChanged(nameof(PingDisplay));
                OnPropertyChanged(nameof(PingLevel));
                OnPropertyChanged(nameof(HasPing));
                OnPropertyChanged(nameof(IsPinging));
            }
        }
    }

    private int? _pingMs;
    [JsonIgnore]
    public int? PingMs
    {
        get => _pingMs;
        set
        {
            if (SetProperty(ref _pingMs, value))
            {
                OnPropertyChanged(nameof(PingDisplay));
                OnPropertyChanged(nameof(PingLevel));
                OnPropertyChanged(nameof(HasPing));
            }
        }
    }

    [JsonIgnore]
    public bool CanPing => Type is ConnectionType.Ssh or ConnectionType.Telnet && !string.IsNullOrWhiteSpace(Host);

    [JsonIgnore]
    public bool HasPing => CanPing && PingStatus != PingStatus.None;

    [JsonIgnore]
    public bool IsPinging => PingStatus == PingStatus.Pinging;

    [JsonIgnore]
    public PingLevel PingLevel => PingStatus switch
    {
        PingStatus.Pinging => PingLevel.Pinging,
        PingStatus.Timeout => PingLevel.Timeout,
        PingStatus.Success => PingMs switch
        {
            < 100 => PingLevel.Fast,
            < 250 => PingLevel.Medium,
            _ => PingLevel.Slow
        },
        _ => PingLevel.Pinging
    };

    [JsonIgnore]
    public string PingDisplay => PingStatus switch
    {
        PingStatus.Pinging => "...",
        PingStatus.Success => $"{PingMs} ms",
        PingStatus.Timeout => LocalizationService.Instance["Timeout"] ?? "超时",
        _ => string.Empty
    };

    /// <summary>
    /// Protocol label as commonly written by SSH clients (SSH / Telnet / Serial / Local).
    /// </summary>
    [JsonIgnore]
    public string TypeLabel => Type switch
    {
        ConnectionType.Ssh => "SSH",
        ConnectionType.Telnet => "Telnet",
        ConnectionType.Serial => "Serial",
        _ => "Local"
    };

    /// <summary>
    /// Style class suffix for protocol-specific badges and chips (Ssh, Telnet, Serial, Local).
    /// </summary>
    [JsonIgnore]
    public string ProtocolClass => Type switch
    {
        ConnectionType.Ssh => "Ssh",
        ConnectionType.Telnet => "Telnet",
        ConnectionType.Serial => "Serial",
        _ => "Local"
    };

    [JsonIgnore]
    public Material.Icons.MaterialIconKind IconKind => Type switch
    {
        ConnectionType.Ssh => Material.Icons.MaterialIconKind.Console,
        ConnectionType.Telnet => Material.Icons.MaterialIconKind.Lan,
        ConnectionType.Serial => Material.Icons.MaterialIconKind.SerialPort,
        _ => Material.Icons.MaterialIconKind.Laptop
    };

    // Short forms used in the classic "9600 8N1" notation.
    private string ParityLetter => Parity switch
    {
        System.IO.Ports.Parity.Even => "E",
        System.IO.Ports.Parity.Odd => "O",
        System.IO.Ports.Parity.Mark => "M",
        System.IO.Ports.Parity.Space => "S",
        _ => "N"
    };

    private string StopBitsLabel => StopBits switch
    {
        System.IO.Ports.StopBits.Two => "2",
        System.IO.Ports.StopBits.OnePointFive => "1.5",
        System.IO.Ports.StopBits.None => "0",
        _ => "1"
    };

    /// <summary>
    /// Creates an exact clone of this connection (with a new ID).
    /// </summary>
    public SavedConnection Clone()
    {
        return new SavedConnection
        {
            Id = Guid.NewGuid(),
            Name = this.Name,
            Type = this.Type,
            Host = this.Host,
            Port = this.Port,
            Username = this.Username,
            Password = this.Password,
            AuthenticationType = this.AuthenticationType,
            KeyFilePath = this.KeyFilePath,
            KeyStoreId = this.KeyStoreId,
            Passphrase = this.Passphrase,
            SerialPortName = this.SerialPortName,
            BaudRate = this.BaudRate,
            DataBits = this.DataBits,
            Parity = this.Parity,
            StopBits = this.StopBits,
            Handshake = this.Handshake,
            GroupName = this.GroupName,
            ShellPath = this.ShellPath
        };
    }

    /// <summary>
    /// Creates a duplicated connection with " (Copy)" appended to the name.
    /// </summary>
    public SavedConnection Duplicate()
    {
        var copy = Clone();
        copy.Name = this.Name + " (Copy)";
        return copy;
    }
}