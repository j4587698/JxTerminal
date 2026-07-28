using System;
using System.Text.Json.Serialization;
using JxTerminal.Services;

namespace JxTerminal.Models;

public class SavedConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Connection";
    public ConnectionType Type { get; set; } = ConnectionType.Ssh;
    
    // SSH Specific
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";

    [JsonIgnore]
    public string Password { get; set; } = "";

    // AOT-friendly serialization property for Password
    public string EncryptedPassword
    {
        get => SecurityService.Encrypt(Password);
        set => Password = SecurityService.Decrypt(value);
    }

    public SshAuthenticationType AuthenticationType { get; set; } = SshAuthenticationType.Password;
    public string KeyFilePath { get; set; } = "";
    
    [JsonIgnore]
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

    public SavedConnection Clone()
    {
        return new SavedConnection
        {
            Id = Guid.NewGuid(), // Clone usually implies a new entity
            Name = this.Name + " (Copy)",
            Type = this.Type,
            Host = this.Host,
            Port = this.Port,
            Username = this.Username,
            Password = this.Password,
            AuthenticationType = this.AuthenticationType,
            KeyFilePath = this.KeyFilePath,
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
}