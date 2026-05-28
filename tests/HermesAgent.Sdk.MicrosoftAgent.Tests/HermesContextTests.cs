using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HermesAgent.Sdk.MicrosoftAgent.Tests;

/// <summary>
/// Tests for <see cref="HermesChatClientAdapter.GetConversationId"/> private method,
/// which reads the conversation ID from <see cref="Microsoft.Extensions.AI.ChatOptions.AdditionalProperties"/>.
///
/// Note: The old <c>HermesContext</c> static class has been removed. Conversation IDs
/// now flow exclusively through <c>ChatOptions.AdditionalProperties["hermes-conversation-id"]</c>.
/// </summary>
public class AdapterGetConversationIdTests
{
    private readonly ILogger<HermesChatClientAdapter> _logger = Substitute.For<ILogger<HermesChatClientAdapter>>();
    private readonly IHermesResponseClient _responseClient = Substitute.For<IHermesResponseClient>();

    private static string? InvokeGetConversationId(MafChatOptions? options)
    {
        var method = typeof(HermesChatClientAdapter).GetMethod("GetConversationId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return method?.Invoke(null, [options]) as string;
    }

    [Fact]
    public void GetConversationId_FromAdditionalProperties_ReturnsExplicitValue()
    {
        var options = new MafChatOptions
        {
            AdditionalProperties = new() { ["hermes-conversation-id"] = "conv_explicit" }
        };

        var result = InvokeGetConversationId(options);

        Assert.Equal("conv_explicit", result);
    }

    [Fact]
    public void GetConversationId_NullOptions_ReturnsNull()
    {
        var result = InvokeGetConversationId(null);

        Assert.Null(result);
    }

    [Fact]
    public void GetConversationId_NoAdditionalProperties_ReturnsNull()
    {
        var options = new MafChatOptions();

        var result = InvokeGetConversationId(options);

        Assert.Null(result);
    }

    [Fact]
    public void GetConversationId_EmptyString_ReturnsNull()
    {
        var options = new MafChatOptions
        {
            AdditionalProperties = new() { ["hermes-conversation-id"] = "" }
        };

        var result = InvokeGetConversationId(options);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConversationId_PassedToResponseOptions_WhenProvided()
    {
        HermesAgent.Sdk.ResponseOptions? captured = null;

        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<HermesAgent.Sdk.ResponseOptions?>();
                return Task.FromResult(new HermesAgent.Sdk.ResponseResult { Id = "test", Output = new List<HermesAgent.Sdk.OutputItem>() });
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);
        var options = new MafChatOptions
        {
            AdditionalProperties = new() { ["hermes-conversation-id"] = "conv_test" }
        };

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options);

        Assert.NotNull(captured);
        Assert.Equal("conv_test", captured.Conversation);
    }

    [Fact]
    public async Task ResponsesApi_PassesConversationInOptions_WhenConversationIdProvided()
    {
        HermesAgent.Sdk.ResponseOptions? capturedOptions = null;

        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedOptions = callInfo.Arg<HermesAgent.Sdk.ResponseOptions?>();
                return Task.FromResult(new HermesAgent.Sdk.ResponseResult
                {
                    Id = "resp_1",
                    Output = new List<HermesAgent.Sdk.OutputItem>()
                });
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);
        var options = new MafChatOptions
        {
            AdditionalProperties = new() { ["hermes-conversation-id"] = "conv_resp" },
            Tools = [AIFunctionFactory.Create(() => "echo")],
        };

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options);

        Assert.NotNull(capturedOptions);
        Assert.Equal("conv_resp", capturedOptions.Conversation);
    }

    [Fact]
    public async Task ResponsesApi_DoesNotSetConversation_WhenNoConversationId()
    {
        HermesAgent.Sdk.ResponseOptions? capturedOptions = null;

        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedOptions = callInfo.Arg<HermesAgent.Sdk.ResponseOptions?>();
                return Task.FromResult(new HermesAgent.Sdk.ResponseResult
                {
                    Id = "resp_1",
                    Output = new List<HermesAgent.Sdk.OutputItem>()
                });
            });

        var adapter = new HermesChatClientAdapter(_logger, _responseClient);

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options: null);

        Assert.NotNull(capturedOptions);
        Assert.Null(capturedOptions.Conversation);
    }
}
