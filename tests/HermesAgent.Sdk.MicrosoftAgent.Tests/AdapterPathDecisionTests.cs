using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HermesAgent.Sdk.MicrosoftAgent.Tests;

/// <summary>
/// Verifies that all adapter requests go through the Responses API
/// (the Chat Completions path has been removed — single-path routing).
/// </summary>
public class AdapterPathDecisionTests
{
    private readonly ILogger<HermesChatClientAdapter> _logger = Substitute.For<ILogger<HermesChatClientAdapter>>();
    private readonly IHermesResponseClient _responseClient = Substitute.For<IHermesResponseClient>();

    [Fact]
    public async Task GetResponseAsync_NullOptions_UsesResponsesApi()
    {
        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new HermesAgent.Sdk.ResponseResult
            {
                Id = "resp_1",
                Output = new List<HermesAgent.Sdk.OutputItem>
                {
                    new() { Type = "message", Contents = [new() { Type = "text", Text = "hello" }] }
                }
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);

        var result = await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options: null);

        await _responseClient.Received(1).CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>());
        Assert.Contains(result.Messages, m => m.Text == "hello");
    }

    [Fact]
    public async Task GetResponseAsync_WithTools_UsesResponsesApi()
    {
        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new HermesAgent.Sdk.ResponseResult
            {
                Id = "resp_123",
                Output = new List<HermesAgent.Sdk.OutputItem>
                {
                    new HermesAgent.Sdk.OutputItem
                    {
                        Type = "message",
                        Contents = new List<HermesAgent.Sdk.OutPutContent>
                        {
                            new HermesAgent.Sdk.OutPutContent { Type = "text", Text = "Hello from tool" }
                        }
                    }
                }
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);
        var chatOptions = new MafChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "echo")],
        };

        var result = await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], chatOptions);

        await _responseClient.Received(1).CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>());
        Assert.Contains(result.Messages, m => m.Text!.Contains("Hello from tool"));
    }
}

/// <summary>
/// Test helper that captures the last request and returns a configured response.
/// </summary>
public class TestMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

    public TestMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await _handler(request);
    }
}
