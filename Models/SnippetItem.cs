using System;
using TinyDb.Attributes;

namespace JxTerminal.Models;

/// <summary>
/// A reusable command snippet shown on the Snippets page and sent to the active terminal.
/// </summary>
[Entity("snippets")]
public class SnippetItem
{
    [Id]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
}
