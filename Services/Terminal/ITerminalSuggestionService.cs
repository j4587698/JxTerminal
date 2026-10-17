using System.Collections.Generic;
using JxTerminal.Models;

namespace JxTerminal.Services.Terminal;

public interface ITerminalSuggestionService
{
    /// <summary>
    /// Records a successfully executed command into the local suggestion history.
    /// </summary>
    void RecordHistory(string command);

    /// <summary>
    /// Gets matching suggestion items for the current line buffer.
    /// </summary>
    IReadOnlyList<TerminalSuggestionItem> GetSuggestions(string lineBuffer, IEnumerable<SnippetItem>? snippets = null, int maxResults = 50);

    /// <summary>
    /// Calculates the suffix characters to append given the current line buffer and selected suggestion item.
    /// </summary>
    string CalculateSuffix(string lineBuffer, TerminalSuggestionItem item);
}
