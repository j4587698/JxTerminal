using System;

namespace JxTerminal.Models;

public enum SuggestionCategory
{
    Command,
    Subcommand,
    Flag,
    History,
    Snippet
}

/// <summary>
/// Represents a single candidate item in the terminal auto-suggestion dropdown.
/// </summary>
public class TerminalSuggestionItem
{
    /// <summary>
    /// The full command text or token to complete to (e.g. "docker ps -a").
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Display text shown in the suggestions list.
    /// </summary>
    public string DisplayText { get; set; } = string.Empty;

    /// <summary>
    /// Brief explanation of the command/flag in Chinese.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Category of the suggestion (Command, Subcommand, Flag, History, Snippet).
    /// </summary>
    public SuggestionCategory Category { get; set; } = SuggestionCategory.Command;

    /// <summary>
    /// Optional prefix token that triggered this match.
    /// </summary>
    public string? MatchedPrefix { get; set; }

    /// <summary>
    /// Returns the badge string to render in the card (e.g. "历史", "快捷", "命令", "参数").
    /// </summary>
    public string CategoryBadge => Category switch
    {
        SuggestionCategory.History => "历史",
        SuggestionCategory.Snippet => "快捷",
        SuggestionCategory.Subcommand => "子命令",
        SuggestionCategory.Flag => "参数",
        _ => "命令"
    };

    public TerminalSuggestionItem() { }

    public TerminalSuggestionItem(string text, string? description, SuggestionCategory category = SuggestionCategory.Command, string? displayText = null)
    {
        Text = text;
        DisplayText = displayText ?? text;
        Description = description;
        Category = category;
    }
}
