using System;
using TinyDb.Attributes;

namespace JxTerminal.Models;

/// <summary>
/// A persisted terminal command history record for suggestions and autocomplete.
/// </summary>
[Entity("command_history")]
public class TerminalHistoryItem
{
    [Id]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Index]
    public string Command { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public TerminalHistoryItem() { }

    public TerminalHistoryItem(string command)
    {
        Command = command;
        Timestamp = DateTime.UtcNow;
    }
}
