using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JxTerminal.Services.Ai;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JxTerminal.Tests;

[TestClass]
public class AiServiceTests
{
    private sealed class MockHandler : HttpMessageHandler
    {
        public string ResponseBody { get; set; } = "{}";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private static AiService CreateWith(MockHandler handler)
        => new AiService(new HttpClient(handler));

    [TestMethod]
    public async Task PlainTextResponse_ReturnsText()
    {
        var handler = new MockHandler
        {
            ResponseBody = """{"choices":[{"message":{"role":"assistant","content":"Hello!"}}]}"""
        };
        var ai = CreateWith(handler);

        var result = await ai.CompleteAsync(
            "https://example.com/v1", "sk-test", "my-model",
            new[] { AiChatMessage.User("hi") }, CancellationToken.None);

        Assert.AreEqual("Hello!", result.Text);
        Assert.IsFalse(result.HasToolCalls);

        // Request must hit {endpoint}/chat/completions with the model and tools.
        Assert.AreEqual("https://example.com/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.AreEqual("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.AreEqual("sk-test", handler.LastRequest.Headers.Authorization.Parameter);
        StringAssert.Contains(handler.LastRequestBody, "\"my-model\"");
        StringAssert.Contains(handler.LastRequestBody, "read_terminal");
        StringAssert.Contains(handler.LastRequestBody, "send_input");
    }

    [TestMethod]
    public async Task ToolCallResponse_ParsesCalls()
    {
        var handler = new MockHandler
        {
            ResponseBody = """
            {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
                {"id":"call_1","type":"function","function":{"name":"read_terminal","arguments":"{\"lines\":20}"}},
                {"id":"call_2","type":"function","function":{"name":"send_input","arguments":"{\"text\":\"ls -la\",\"enter\":true}"}}
            ]}}]}
            """
        };
        var ai = CreateWith(handler);

        var result = await ai.CompleteAsync(
            "https://example.com/v1/", "k", "m",
            new[] { AiChatMessage.User("list files") }, CancellationToken.None);

        Assert.IsTrue(result.HasToolCalls);
        Assert.AreEqual(2, result.ToolCalls.Count);
        Assert.AreEqual("call_1", result.ToolCalls[0].Id);
        Assert.AreEqual("read_terminal", result.ToolCalls[0].Name);
        Assert.AreEqual(20, result.ToolCalls[0].Arguments["lines"]!.GetValue<int>());
        Assert.AreEqual("send_input", result.ToolCalls[1].Name);
        Assert.AreEqual("ls -la", result.ToolCalls[1].Arguments["text"]!.GetValue<string>());
        Assert.IsTrue(result.ToolCalls[1].Arguments["enter"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task ErrorStatus_ThrowsWithProviderMessage()
    {
        var handler = new MockHandler
        {
            StatusCode = HttpStatusCode.Unauthorized,
            ResponseBody = """{"error":{"message":"Incorrect API key"}}"""
        };
        var ai = CreateWith(handler);

        var ex = await Assert.ThrowsExactlyAsync<AiServiceException>(() =>
            ai.CompleteAsync("https://example.com/v1", "bad", "m",
                new[] { AiChatMessage.User("hi") }, CancellationToken.None));

        StringAssert.Contains(ex.Message, "Incorrect API key");
        StringAssert.Contains(ex.Message, "401");
    }

    [TestMethod]
    public async Task InvalidJson_ThrowsAiException()
    {
        var handler = new MockHandler { ResponseBody = "not json{{{" };
        var ai = CreateWith(handler);

        await Assert.ThrowsExactlyAsync<AiServiceException>(() =>
            ai.CompleteAsync("https://example.com/v1", "k", "m",
                new[] { AiChatMessage.User("hi") }, CancellationToken.None));
    }

    [TestMethod]
    public async Task ToolResultMessages_SerializeToolCallId()
    {
        var handler = new MockHandler
        {
            ResponseBody = """{"choices":[{"message":{"role":"assistant","content":"done"}}]}"""
        };
        var ai = CreateWith(handler);

        await ai.CompleteAsync("https://example.com/v1", "k", "m",
            new AiChatMessage[]
            {
                AiChatMessage.User("run ls"),
                new AiChatMessage("assistant", "") { ToolCalls = new System.Collections.Generic.List<AiToolCall> { new AiToolCall("call_1", "send_input", new System.Text.Json.Nodes.JsonObject()) } },
                AiChatMessage.ToolResult("call_1", "ok")
            }, CancellationToken.None);

        StringAssert.Contains(handler.LastRequestBody, "\"tool_call_id\":\"call_1\"");
        StringAssert.Contains(handler.LastRequestBody, "\"tool_calls\"");
    }
}
