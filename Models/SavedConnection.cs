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

    // Grouping
    public string GroupName { get; set; } = "Default";

    // Local Specific (Optional: Shell path, e.g., /bin/bash, powershell)
    public string ShellPath { get; set; } = ""; 

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
            GroupName = this.GroupName,
            ShellPath = this.ShellPath
        };
    }
}