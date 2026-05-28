using System.Text.Json;
using HermesAgent.Sdk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HermesAgent.Sdk.MicrosoftAgent.Tests;

/// <summary>
/// Tests for the Responses API output-to-content mapping logic in
/// <see cref="HermesChatClientAdapter"/>, including function calling support.
///
/// The Hermes server executes function calls server-side and returns:
/// - <c>function_call</c> output items → <see cref="FunctionCallContent"/>
/// - <c>function_call_output</c> output items → <see cref="FunctionResultContent"/>
/// - <c>message</c>/<c>text</c>/<c>output_text</c> → <see cref="TextContent"/>
/// </summary>
public class ResponsesApiMappingTests
{
    private readonly ILogger<HermesChatClientAdapter> _logger = Substitute.For<ILogger<HermesChatClientAdapter>>();
    private readonly IHermesResponseClient _responseClient = Substitute.For<IHermesResponseClient>();

    [Fact]
    public async Task TextOutput_MapsToTextContent()
    {
        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ResponseResult
            {
                Id = "resp_1",
                Output =
                [
                    new() { Type = "message", Contents = [new() { Type = "text", Text = "Hello world" }] }
                ],
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);
        var result = await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hi")]);

        Assert.Contains(result.Messages, m => m.Text == "Hello world");
    }

    [Fact]
    public async Task FunctionCallOutput_MapsToFunctionCallContent()
    {
        var outputItem = JsonSerializer.Deserialize<OutputItem>($$"""
            {
                "type": "function_call",
                "call_id": "call_123",
                "name": "get_weather",
                "arguments": {"location": "Beijing"},
                "content": []
            }
            """)!;

        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ResponseResult
            {
                Id = "resp_1",
                Output = [outputItem],
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);
        var result = await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "weather?")]);

        var functionCall = result.Messages
            .SelectMany(m => m.Contents ?? [])
            .OfType<FunctionCallContent>()
            .FirstOrDefault();

        Assert.NotNull(functionCall);
        Assert.Equal("call_123", functionCall.CallId);
        Assert.Equal("get_weather", functionCall.Name);
        Assert.NotNull(functionCall.Arguments);
    }

    [Fact]
    public async Task FunctionCallOutput_WithoutArgs_MapsToFunctionCallContent()
    {
        var outputItem = JsonSerializer.Deserialize<OutputItem>("""
            {
                "type": "function_call",
                "call_id": "call_456",
                "name": "get_time",
                "content": []
            }
            """)!;

        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ResponseResult
            {
                Id = "resp_2",
                Output = [outputItem],
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);
        var result = await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "time?")]);

        var functionCall = result.Messages
            .SelectMany(m => m.Contents ?? [])
            .OfType<FunctionCallContent>()
            .FirstOrDefault();

        Assert.NotNull(functionCall);
        Assert.Equal("call_456", functionCall.CallId);
        Assert.Equal("get_time", functionCall.Name);
    }

    [Fact]
    public async Task FunctionCallOutput_Result_MapsToFunctionResultContent()
    {
        var outputItem = JsonSerializer.Deserialize<OutputItem>("""
            {
                "type": "function_call_output",
                "call_id": "call_789",
                "output": "Sunny, 25°C",
                "content": []
            }
            """)!;

        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ResponseResult
            {
                Id = "resp_3",
                Output = [outputItem],
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);
        var result = await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "weather?")]);

        var functionResult = result.Messages
            .SelectMany(m => m.Contents ?? [])
            .OfType<FunctionResultContent>()
            .FirstOrDefault();

        Assert.NotNull(functionResult);
        Assert.Equal("call_789", functionResult.CallId);
        Assert.Equal("Sunny, 25°C", functionResult.Result);
    }

    [Fact]
    public async Task MixedOutput_MapsToMultipleContentTypes()
    {
        var fcItem = JsonSerializer.Deserialize<OutputItem>("""
            {
                "type": "function_call",
                "call_id": "call_001",
                "name": "search",
                "arguments": {"q": "hello"},
                "content": []
            }
            """)!;

        var textItem = new OutputItem
        {
            Type = "message",
            Contents = [new() { Type = "text", Text = "Here are the results" }],
        };

        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ResponseResult
            {
                Id = "resp_4",
                Output = [fcItem, textItem],
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);
        var result = await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "search hello")]);

        var contents = result.Messages.SelectMany(m => m.Contents ?? []).ToList();
        Assert.Contains(contents, c => c is FunctionCallContent);
        Assert.Contains(contents, c => c is TextContent);
    }

    [Fact]
    public async Task EmptyOutput_ReturnsEmptyMessage()
    {
        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ResponseResult
            {
                Id = "resp_empty",
                Output = [],
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);
        var result = await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hi")]);

        var message = result.Messages.SingleOrDefault();
        Assert.NotNull(message);
        Assert.Empty(message.Contents ?? []);
    }
}
