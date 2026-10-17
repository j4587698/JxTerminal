using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JxTerminal.Models;

namespace JxTerminal.Services.Terminal;

public class TerminalSuggestionService : ITerminalSuggestionService
{
    private static readonly Lazy<TerminalSuggestionService> _instance =
        new(() => new TerminalSuggestionService());
    public static TerminalSuggestionService Instance => _instance.Value;

    private readonly List<string> _history = new();
    private readonly object _lock = new();
    private int MaxHistoryCount => DataStore.LoadSettings().HistoryRetentionLimit > 0 ? DataStore.LoadSettings().HistoryRetentionLimit : 1000;

    /// <summary>
    /// Base popularity score (1 to 100) for common CLI commands.
    /// Higher score commands rank higher when prefixes or matching levels are tied.
    /// </summary>
    private static readonly Dictionary<string, int> CommandPopularity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ls"] = 100,
        ["cd"] = 100,
        ["git"] = 98,
        ["docker"] = 96,
        ["cat"] = 92,
        ["grep"] = 92,
        ["rm"] = 90,
        ["cp"] = 90,
        ["mv"] = 90,
        ["mkdir"] = 90,
        ["pwd"] = 90,
        ["ps"] = 88,
        ["curl"] = 88,
        ["ssh"] = 88,
        ["sudo"] = 88,
        ["clear"] = 88,
        ["find"] = 86,
        ["tail"] = 86,
        ["systemctl"] = 86,
        ["chmod"] = 85,
        ["chown"] = 85,
        ["tar"] = 85,
        ["df"] = 82,
        ["du"] = 82,
        ["free"] = 82,
        ["top"] = 82,
        ["htop"] = 82,
        ["ping"] = 80,
        ["netstat"] = 80,
        ["ss"] = 80,
        ["nginx"] = 80,
        ["apt"] = 80,
        ["yum"] = 80,
        ["kill"] = 80,
        ["vim"] = 80,
        ["nano"] = 78,
        ["echo"] = 78,
        ["touch"] = 78,
        ["history"] = 78,
        ["journalctl"] = 75,
        ["lsof"] = 65,
        ["crontab"] = 70,
        ["scp"] = 72,
        ["rsync"] = 72,
        ["ip"] = 75,
        ["ifconfig"] = 70,
        ["whoami"] = 70,
        ["uptime"] = 70,
        ["uname"] = 70,
        ["head"] = 72,
        ["less"] = 72,
        ["wget"] = 72
    };

    private List<SnippetItem> _cachedSnippets = PresetCommands.GetDefaultSnippets();

    public void RecordHistory(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        var trimmed = command.Trim();
        if (trimmed.Length == 0) return;

        lock (_lock)
        {
            _history.Remove(trimmed);
            _history.Insert(0, trimmed);
            if (_history.Count > MaxHistoryCount)
            {
                _history.RemoveAt(_history.Count - 1);
            }
        }

        _ = DataStore.SaveCommandHistoryAsync(trimmed);
    }

    public TerminalSuggestionService()
    {
        _ = RefreshSnippetsAsync();
        _ = LoadHistoryAsync();
    }

    public async Task LoadHistoryAsync()
    {
        try
        {
            var list = await DataStore.LoadCommandHistoryAsync(MaxHistoryCount);
            lock (_lock)
            {
                foreach (var cmd in list)
                {
                    if (!_history.Contains(cmd))
                    {
                        _history.Add(cmd);
                    }
                }
            }
        }
        catch { }
    }

    public async Task RefreshSnippetsAsync()
    {
        try
        {
            var list = await DataStore.LoadSnippetsAsync();
            lock (_lock)
            {
                _cachedSnippets = list.ToList();
            }
        }
        catch { }
    }

    public IReadOnlyList<TerminalSuggestionItem> GetSuggestions(
        string lineBuffer,
        IEnumerable<SnippetItem>? snippets = null,
        int maxResults = 50)
    {
        if (string.IsNullOrWhiteSpace(lineBuffer))
            return Array.Empty<TerminalSuggestionItem>();

        var trimmed = lineBuffer.TrimStart();
        if (trimmed.Length == 0)
            return Array.Empty<TerminalSuggestionItem>();

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return Array.Empty<TerminalSuggestionItem>();

        bool hasSpace = trimmed.Contains(' ');
        string userBaseCmd = tokens[0];

        // Gather all candidate pools
        var pool = new List<TerminalSuggestionItem>();

        // 1. History items
        lock (_lock)
        {
            foreach (var h in _history)
            {
                pool.Add(new TerminalSuggestionItem(h, "执行过的历史命令", SuggestionCategory.History, h));
            }
        }

        // 2. Snippets
        var effectiveSnippets = snippets;
        if (effectiveSnippets == null)
        {
            lock (_lock)
            {
                effectiveSnippets = _cachedSnippets.Count > 0 ? _cachedSnippets.ToList() : null;
            }
        }
        if (effectiveSnippets != null)
        {
            foreach (var s in effectiveSnippets)
            {
                var code = s.Command?.Trim();
                if (!string.IsNullOrEmpty(code))
                {
                    var desc = !string.IsNullOrEmpty(s.Name) ? s.Name : "快捷命令";
                    pool.Add(new TerminalSuggestionItem(code, desc, SuggestionCategory.Snippet, code));
                }
            }
        }

        // Score and rank all candidates
        var scoredList = new List<(TerminalSuggestionItem Item, int Score)>();
        var seenTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in pool)
        {
            if (string.IsNullOrWhiteSpace(candidate.Text)) continue;
            var candText = candidate.Text.Trim();
            if (seenTexts.Contains(candText)) continue;

            var candTokens = candText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (candTokens.Length == 0) continue;
            var candBaseCmd = candTokens[0];

            int score = CalculateMatchScore(trimmed, tokens, hasSpace, userBaseCmd, candidate, candText, candBaseCmd, candTokens);
            if (score > 0)
            {
                candidate.MatchedPrefix = trimmed;
                scoredList.Add((candidate, score));
                seenTexts.Add(candText);
            }
        }

        return scoredList
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Text.Length)
            .Take(maxResults)
            .Select(x => x.Item)
            .ToList();
    }

    private static int CalculateMatchScore(
        string trimmedInput,
        string[] userTokens,
        bool hasSpace,
        string userBaseCmd,
        TerminalSuggestionItem candidate,
        string candText,
        string candBaseCmd,
        string[] candTokens)
    {
        int score = 0;

        // --- Scenario 1: User has typed space or arguments (e.g. "ls ", "ls -", "docker ps") ---
        if (hasSpace)
        {
            // If user has committed to a base command (e.g. "ls "), candidate must match that base command!
            // E.g., for "ls ", candidate "lsof" is completely disqualified.
            if (!candBaseCmd.Equals(userBaseCmd, StringComparison.OrdinalIgnoreCase))
            {
                // Special exception: if the full input is a direct prefix of the candidate text
                if (candText.StartsWith(trimmedInput, StringComparison.OrdinalIgnoreCase))
                {
                    score = 25000;
                }
                else
                {
                    return 0; // Disqualified
                }
            }
            else
            {
                // If candidate is just the bare base command (e.g. "ls") and user has already typed "ls ",
                // don't suggest the shorter bare command.
                if (candTokens.Length == 1 && candText.Length <= trimmedInput.Length)
                {
                    return 0;
                }

                if (candText.Equals(trimmedInput, StringComparison.OrdinalIgnoreCase))
                {
                    score = 100000; // Perfect match
                }
                else if (candText.StartsWith(trimmedInput, StringComparison.OrdinalIgnoreCase))
                {
                    score = 70000; // Subcommand / argument prefix match
                }
                else if (!trimmedInput.EndsWith(' '))
                {
                    // Tail token match (e.g. user typed "docker ps -" and candidate is "docker ps -a")
                    var lastUserToken = userTokens[^1];
                    if (candTokens.Any(t => t.StartsWith(lastUserToken, StringComparison.OrdinalIgnoreCase)))
                    {
                        score = 40000;
                    }
                    else if (candText.Contains(lastUserToken, StringComparison.OrdinalIgnoreCase))
                    {
                        score = 10000;
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return 0;
                }
            }
        }
        // --- Scenario 2: Single word without space (e.g. "ls", "dock", "git") ---
        else
        {
            // Level 1: Exact match with candidate text (e.g. user typed "ls", candidate is "ls")
            if (candText.Equals(trimmedInput, StringComparison.OrdinalIgnoreCase))
            {
                score = 100000; // Highest priority
            }
            // Level 2: Same base command with arguments (e.g. user typed "ls", candidate is "ls -la", "ls -l", "ls -lh")
            else if (candBaseCmd.Equals(trimmedInput, StringComparison.OrdinalIgnoreCase))
            {
                score = 60000;
            }
            // Level 3: Candidate base command starts with input prefix (e.g. user typed "dock", candidate is "docker", "docker ps")
            else if (candBaseCmd.StartsWith(trimmedInput, StringComparison.OrdinalIgnoreCase))
            {
                // If candidate is the bare command (e.g. "docker"), give it higher priority than composite ("docker ps")
                if (candTokens.Length == 1)
                {
                    score = 30000;
                }
                else
                {
                    score = 20000;
                }
            }
            // Level 4: Candidate contains the input word as a separate token (e.g. "docker network ls")
            else if (candTokens.Any(t => t.Equals(trimmedInput, StringComparison.OrdinalIgnoreCase)))
            {
                score = 5000;
            }
            // Level 5: Substring contains fallback
            else if (candText.Contains(trimmedInput, StringComparison.OrdinalIgnoreCase))
            {
                score = 1000;
            }
            else
            {
                return 0;
            }
        }

        // --- Bonus & Penalty Weights ---

        // Category bonuses
        if (candidate.Category == SuggestionCategory.History)
        {
            score += 15000;
        }
        else if (candidate.Category == SuggestionCategory.Snippet)
        {
            score += 12000;
        }

        // Popularity bonus based on base command
        if (CommandPopularity.TryGetValue(candBaseCmd, out int popularity))
        {
            score += popularity * 15; // Up to 1500 points
        }

        // Length penalty: cleaner, more concise commands rank higher among equals
        score -= Math.Min(candText.Length * 5, 500);

        return score;
    }

    public string CalculateSuffix(string lineBuffer, TerminalSuggestionItem item)
    {
        if (string.IsNullOrEmpty(item.Text)) return string.Empty;

        var trimmed = lineBuffer.TrimStart();
        if (item.Text.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return item.Text.Substring(trimmed.Length);
        }

        // Tail token matching
        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0)
        {
            var last = tokens[^1];
            if (item.Text.EndsWith(last, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
            if (item.Text.StartsWith(last, StringComparison.OrdinalIgnoreCase))
            {
                return item.Text.Substring(last.Length);
            }
        }

        return item.Text;
    }
}
