using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JxTerminal.Services.Ai;

/// <summary>One message in the chat conversation.</summary>
public record AiChatMessage(string Role, string Content)
{
    /// <summary>Assistant tool calls pending execution (when Role == "tool_calls").</summary>
    public List<AiToolCall>? ToolCalls { get; init; }

    /// <summary>Tool call id this message answers (when Role == "tool").</summary>
    public string? ToolCallId { get; init; }

    public static AiChatMessage System(string content) => new("system", content);
    public static AiChatMessage User(string content) => new("user", content);
    public static AiChatMessage Assistant(string content) => new("assistant", content);
    public static AiChatMessage ToolResult(string toolCallId, string content) =>
        new("tool", content) { ToolCallId = toolCallId };
}

/// <summary>A function invocation requested by the model.</summary>
public record AiToolCall(string Id, string Name, JsonObject Arguments);

/// <summary>
/// Result of one chat completion round: either final text, or tool calls to execute.
/// </summary>
public record AiCompletionResult(string? Text, IReadOnlyList<AiToolCall> ToolCalls)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
}

/// <summary>
/// Minimal OpenAI-compatible chat-completions client with function-calling support.
/// Works with OpenAI, DeepSeek, Qwen (DashScope compatible mode), Ollama and other
/// providers exposing POST {endpoint}/chat/completions.
/// </summary>
public class AiService
{
    private static readonly HttpClient SharedHttp = CreateClient();
    private readonly HttpClient _http;

    public AiService() => _http = SharedHttp;

    /// <summary>Test hook: allows injecting a mock-backed HttpClient.</summary>
    internal AiService(HttpClient? client) => _http = client ?? SharedHttp;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JxTerminal/1.0");
        return client;
    }

    /// <summary>The tools the assistant can use to read from and write to the terminal.</summary>
    public static JsonArray BuildToolDefinitions()
    {
        var array = new JsonArray();
        array.Add((JsonNode)new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "read_terminal",
                ["description"] = "Reads text currently visible on the terminal screen (or the last N lines of output). Use it to inspect command output, errors, or the current prompt before acting.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["lines"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "How many trailing lines to read. Omit or 0 to read the visible screen."
                        }
                    }
                }
            }
        });
        array.Add((JsonNode)new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "send_input",
                ["description"] = "Types text into the terminal, as if the user typed it. Set enter=true to append Return and execute the command line. Prefer one complete command per call.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["text"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The text to type into the terminal."
                        },
                        ["enter"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Append Return after the text (execute the command)."
                        }
                    },
                    ["required"] = new JsonArray("text")
                }
            }
        });
        return array;
    }

    /// <summary>
    /// Sends one chat-completion round and returns either the assistant text or the
    /// tool calls it requested. Throws <see cref="AiServiceException"/> on HTTP or
    /// protocol errors with a user-presentable message.
    /// </summary>
    public async Task<AiCompletionResult> CompleteAsync(
        string endpoint,
        string apiKey,
        string model,
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken ct)
    {
        var msgArray = new JsonArray();
        foreach (var m in messages)
        {
            var node = new JsonObject { ["role"] = m.Role, ["content"] = m.Content };
            if (m.ToolCalls is { Count: > 0 } calls)
            {
                node["tool_calls"] = new JsonArray(calls.Select(c => (JsonNode)new JsonObject
                {
                    ["id"] = c.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = c.Name,
                        ["arguments"] = c.Arguments.ToJsonString()
                    }
                }).ToArray());
            }
            if (m.Role == "tool" && m.ToolCallId != null)
            {
                node["tool_call_id"] = m.ToolCallId;
            }
            msgArray.Add((JsonNode)node);
        }

        var requestBody = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim(),
            ["messages"] = msgArray,
            ["tools"] = BuildToolDefinitions(),
            ["tool_choice"] = "auto",
            ["temperature"] = 0.3
        };

        var url = endpoint.Trim().TrimEnd('/') + "/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _http.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Surface the provider's error message when present.
            string detail = responseText;
            try
            {
                var errNode = JsonNode.Parse(responseText);
                detail = errNode?["error"]?["message"]?.GetValue<string>() ?? responseText;
            }
            catch { /* keep raw body */ }

            throw new AiServiceException($"HTTP {(int)response.StatusCode}: {detail}");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(responseText);
        }
        catch (Exception ex)
        {
            throw new AiServiceException($"Invalid JSON from AI provider: {ex.Message}");
        }

        var choice = root?["choices"]?[0];
        var message = choice?["message"];
        if (message == null)
            throw new AiServiceException("AI provider returned no choices.");

        var toolCalls = new List<AiToolCall>();
        if (message["tool_calls"] is JsonArray callsArray)
        {
            foreach (var call in callsArray)
            {
                if (call == null) continue;
                var id = call["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                var name = call["function"]?["name"]?.GetValue<string>() ?? "";
                JsonObject args;
                try
                {
                    args = JsonNode.Parse(call["function"]?["arguments"]?.GetValue<string>() ?? "{}")
                        as JsonObject ?? new JsonObject();
                }
                catch
                {
                    args = new JsonObject();
                }
                if (!string.IsNullOrEmpty(name))
                    toolCalls.Add(new AiToolCall(id, name, args));
            }
        }

        var text = message["content"]?.GetValue<string>();
        return new AiCompletionResult(text, toolCalls);
    }
}

public class AiServiceException : Exception
{
    public AiServiceException(string message) : base(message) { }
}
