using System;

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
    public string Password { get; set; } = "";
    public SshAuthenticationType AuthenticationType { get; set; } = SshAuthenticationType.Password;
    public string KeyFilePath { get; set; } = "";
    public string Passphrase { get; set; } = ""; // For private key
    
    // Serial Specific
    public string SerialPortName { get; set; } = "";
    public int BaudRate { get; set; } = 9600;

    // Grouping
    public string GroupName { get; set; } = "Default";

    // Local Specific (Optional: Shell path, e.g., /bin/bash, powershell)
    public string ShellPath { get; set; } = ""; 
}
