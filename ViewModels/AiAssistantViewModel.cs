using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using JxTerminal.Services;
using JxTerminal.Services.Ai;

namespace JxTerminal.ViewModels;

/// <summary>One bubble in the assistant chat view.</summary>
public partial class AiChatMessageItem : ObservableObject
{
    public string Role { get; }
    public DateTime Time { get; } = DateTime.Now;

    [ObservableProperty]
    private string _content;

    /// <summary>Tool activity lines ("read_terminal(...)") shown dimmed between bubbles.</summary>
    public bool IsTool { get; }

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";

    public AiChatMessageItem(string role, string content, bool isTool = false)
    {
        Role = role;
        _content = content;
        IsTool = isTool;
    }
}

/// <summary>
/// Per-tab AI assistant: keeps the conversation, drives the tool-calling loop and
/// bridges the model's read_terminal / send_input tools to the owning terminal.
/// </summary>
public partial class AiAssistantViewModel : ViewModelBase, IDisposable
{
    /// <summary>Reads terminal text. Argument = trailing line count (0 = visible screen).</summary>
    private readonly Func<int, Task<string>> _readTerminalAsync;

    /// <summary>Sends raw text to the terminal (as typed input).</summary>
    private readonly Action<string> _sendInput;

    /// <summary>Asks the user before executing a command when auto-approve is off.</summary>
    private readonly Func<string, string, Task<bool>> _confirmAsync;

    /// <summary>Closes the panel (owned by the parent terminal view model).</summary>
    private readonly Action _close;

    /// <summary>Describes the session for the system prompt (target, shell, OS).</summary>
    private readonly string _sessionDescription;

    private readonly TerminalSettings _settings;
    private readonly System.ComponentModel.PropertyChangedEventHandler _settingsHandler;
    private readonly AiService _ai = new();

    /// <summary>Default tool-call rounds fallback if setting is unset or <= 0.</summary>
    public const int DefaultMaxToolRounds = 25;

    /// <summary>Character budget for one read_terminal result.</summary>
    private const int MaxReadChars = 6000;

    public ObservableCollection<AiChatMessageItem> Messages { get; } = new();

    // API conversation history (system prompt at index 0).
    private readonly List<AiChatMessage> _history = new();

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    private bool _isBusy;

    public bool IsInputEnabled => !IsBusy;

    partial void OnInputTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();

    public AiAssistantViewModel(
        TerminalSettings settings,
        Func<int, Task<string>> readTerminalAsync,
        Action<string> sendInput,
        Func<string, string, Task<bool>> confirmAsync,
        string sessionDescription,
        Action? close = null)
    {
        _settings = settings;
        _readTerminalAsync = readTerminalAsync;
        _sendInput = sendInput;
        _confirmAsync = confirmAsync;
        _sessionDescription = sessionDescription;
        _close = close ?? (() => { });
        _history.Add(AiChatMessage.System(BuildSystemPrompt()));

        // Settings edit elsewhere (the settings page shares the same object) must
        // refresh the "not configured" banner immediately.
        _settingsHandler = (_, e) =>
        {
            if (e.PropertyName is nameof(TerminalSettings.AiEndpoint)
                or nameof(TerminalSettings.AiModel)
                or nameof(TerminalSettings.AiApiKey))
            {
                OnPropertyChanged(nameof(IsAiConfigured));
            }
        };
        _settings.PropertyChanged += _settingsHandler;

        Messages.Add(new AiChatMessageItem("assistant", LocalizationService.Instance["AiWelcome"]));
    }

    /// <summary>True when endpoint + API key are set; mirrors the shared settings object.</summary>
    public bool IsAiConfigured => _settings.IsAiConfigured;

    [RelayCommand]
    private void Close() => _close();

    [RelayCommand]
    private void OpenSettings() => MainWindowViewModel.Instance?.NavigateToSettingsTab(2);

    [RelayCommand]
    public async Task CopyMessage(string? content)
    {
        if (!string.IsNullOrEmpty(content))
        {
            await ClipboardService.SetTextAsync(content);
        }
    }

    public void Dispose()
    {
        _settings.PropertyChanged -= _settingsHandler;
    }

    private string BuildSystemPrompt()
    {
        var l = LocalizationService.Instance;
        return string.Format(l["AiSystemPrompt"], _sessionDescription);
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0) return;
        InputText = string.Empty;
        await RunConversationAsync(text, screenSnapshot: null);
    }

    /// <summary>One-click action: analyze the current screen and act on it.</summary>
    [RelayCommand]
    private async Task AnalyzeScreenAsync()
    {
        if (IsBusy) return;

        var l = LocalizationService.Instance;
        string snapshot;
        try
        {
            snapshot = await _readTerminalAsync(0);
        }
        catch (Exception ex)
        {
            snapshot = $"({ex.Message})";
        }

        await RunConversationAsync(l["AiAnalyzeRequest"], screenSnapshot: snapshot);
    }

    [RelayCommand]
    private void Clear()
    {
        Messages.Clear();
        _history.Clear();
        _history.Add(AiChatMessage.System(BuildSystemPrompt()));
        Messages.Add(new AiChatMessageItem("assistant", LocalizationService.Instance["AiWelcome"]));
    }

    private async Task RunConversationAsync(string userText, string? screenSnapshot)
    {
        var l = LocalizationService.Instance;

        if (!_settings.IsAiConfigured)
        {
            Messages.Add(new AiChatMessageItem("assistant", l["AiNotConfigured"]));
            return;
        }

        Messages.Add(new AiChatMessageItem("user", userText));
        var apiUserText = screenSnapshot != null
            ? $"{userText}\n\n```\n{screenSnapshot}\n```"
            : userText;
        _history.Add(AiChatMessage.User(apiUserText));

        IsBusy = true;
        try
        {
            var maxRounds = _settings.AiMaxToolRounds > 0 ? _settings.AiMaxToolRounds : DefaultMaxToolRounds;
            for (int round = 0; round < maxRounds; round++)
            {
                var result = await _ai.CompleteAsync(
                    _settings.AiEndpoint,
                    _settings.AiApiKey,
                    _settings.AiModel,
                    _history,
                    CancellationToken.None);

                if (!result.HasToolCalls)
                {
                    var reply = string.IsNullOrWhiteSpace(result.Text) ? l["AiEmptyReply"] : result.Text!;
                    _history.Add(AiChatMessage.Assistant(reply));
                    Messages.Add(new AiChatMessageItem("assistant", reply));
                    return;
                }

                // Record the assistant's tool-call turn, execute each tool, feed results back.
                _history.Add(new AiChatMessage("assistant", result.Text ?? "")
                {
                    ToolCalls = result.ToolCalls.ToList()
                });

                foreach (var call in result.ToolCalls)
                {
                    var toolResult = await ExecuteToolAsync(call);
                    Messages.Add(new AiChatMessageItem("assistant", toolResult.DisplayLine, isTool: true));
                    _history.Add(AiChatMessage.ToolResult(call.Id, toolResult.ResultForModel));
                }
            }

            Messages.Add(new AiChatMessageItem("assistant", string.Format(l["AiTooManyRounds"], maxRounds)));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"AI request failed: {ex.Message}");
            Messages.Add(new AiChatMessageItem("assistant",
                $"{l["AiErrorPrefix"]}{ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private record ToolExecution(string DisplayLine, string ResultForModel);

    private async Task<ToolExecution> ExecuteToolAsync(AiToolCall call)
    {
        var l = LocalizationService.Instance;

        switch (call.Name)
        {
            case "read_terminal":
            {
                int lines = 0;
                if (call.Arguments.TryGetPropertyValue("lines", out var linesNode)
                    && int.TryParse(linesNode?.ToString(), out int parsed) && parsed > 0)
                {
                    lines = parsed;
                }

                var text = await _readTerminalAsync(lines);
                if (text.Length > MaxReadChars)
                    text = text[^MaxReadChars..];

                var display = string.Format(l["AiToolRead"], lines > 0 ? lines.ToString() : l["AiVisibleScreen"]);
                return new ToolExecution(display, text.Length == 0 ? "(empty screen)" : text);
            }

            case "send_input":
            {
                var text = call.Arguments.TryGetPropertyValue("text", out var textNode)
                    ? textNode?.GetValue<string>() ?? ""
                    : "";
                bool enter = call.Arguments.TryGetPropertyValue("enter", out var enterNode)
                    && enterNode?.GetValue<bool>() == true;

                if (string.IsNullOrEmpty(text))
                    return new ToolExecution(l["AiToolWriteEmpty"], "error: empty text");

                if (!_settings.AiAutoApproveCommands)
                {
                    bool approved = await _confirmAsync(
                        l["AiConfirmRunTitle"],
                        string.Format(l["AiConfirmRunMessage"], text + (enter ? " ⏎" : "")));
                    if (!approved)
                        return new ToolExecution(l["AiToolWriteDenied"], "denied by user");
                }

                _sendInput(enter ? text + "\r" : text);
                return new ToolExecution(
                    string.Format(l["AiToolWrote"], text + (enter ? " ⏎" : "")),
                    "ok");
            }

            default:
                return new ToolExecution(
                    string.Format(l["AiToolUnknown"], call.Name),
                    $"error: unknown tool '{call.Name}'");
        }
    }
}
