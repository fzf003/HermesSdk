using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HermesAgent.Sdk.MicrosoftAgent.Tests;

public class HermesContextTests
{
    [Fact]
    public void SetAndGet_ReturnsSameValue()
    {
        HermesContext.SetConversationId("conv_001");
        var result = HermesContext.GetConversationId();
        Assert.Equal("conv_001", result);
        HermesContext.Clear();
    }

    [Fact]
    public void Get_WhenNotSet_ReturnsNull()
    {
        HermesContext.Clear();
        var result = HermesContext.GetConversationId();
        Assert.Null(result);
    }

    [Fact]
    public void Clear_RemovesValue()
    {
        HermesContext.SetConversationId("conv_001");
        HermesContext.Clear();
        Assert.Null(HermesContext.GetConversationId());
    }

    [Fact]
    public async Task AsyncLocal_FlowsAcrossAwait()
    {
        HermesContext.SetConversationId("conv_async");

        var result = await Task.Run(async () =>
        {
            await Task.Delay(10);
            return HermesContext.GetConversationId();
        });

        Assert.Equal("conv_async", result);
        HermesContext.Clear();
    }

    [Fact]
    public async Task AsyncLocal_IsIsolatedBetweenParallelTasks()
    {
        HermesContext.Clear();

        var task1 = Task.Run(async () =>
        {
            HermesContext.SetConversationId("conv_1");
            await Task.Delay(50);
            return HermesContext.GetConversationId();
        });

        var task2 = Task.Run(async () =>
        {
            HermesContext.SetConversationId("conv_2");
            await Task.Delay(50);
            return HermesContext.GetConversationId();
        });

        var results = await Task.WhenAll(task1, task2);
        Assert.Contains("conv_1", results);
        Assert.Contains("conv_2", results);
    }
}

public class AdapterGetConversationIdTests
{
    private readonly IHermesChatClient _chatClient = Substitute.For<IHermesChatClient>();
    private readonly ILogger<HermesChatClientAdapter> _logger = Substitute.For<ILogger<HermesChatClientAdapter>>();
    private readonly IHermesResponseClient _responseClient = Substitute.For<IHermesResponseClient>();

    [Fact]
    public void GetConversationId_FromAdditionalProperties_ReturnsExplicitValue()
    {
        HermesContext.Clear();
        var options = new MafChatOptions
        {
            AdditionalProperties = new() { ["hermes-conversation-id"] = "conv_explicit" }
        };

        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);
        // Use reflection to test private static method
        var method = typeof(HermesChatClientAdapter).GetMethod("GetConversationId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = method?.Invoke(null, [options]);

        Assert.Equal("conv_explicit", result);
    }

    [Fact]
    public void GetConversationId_FromHermesContext_ReturnsContextValue()
    {
        HermesContext.SetConversationId("conv_context");
        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);
        var method = typeof(HermesChatClientAdapter).GetMethod("GetConversationId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method?.Invoke(null, [null as MafChatOptions]);

        Assert.Equal("conv_context", result);
        HermesContext.Clear();
    }

    [Fact]
    public void GetConversationId_ExplicitOverridesContext()
    {
        HermesContext.SetConversationId("conv_context");
        var options = new MafChatOptions
        {
            AdditionalProperties = new() { ["hermes-conversation-id"] = "conv_explicit" }
        };

        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);
        var method = typeof(HermesChatClientAdapter).GetMethod("GetConversationId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method?.Invoke(null, [options]);

        Assert.Equal("conv_explicit", result);
        HermesContext.Clear();
    }

    [Fact]
    public void GetConversationId_NoSource_ReturnsNull()
    {
        HermesContext.Clear();
        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);
        var method = typeof(HermesChatClientAdapter).GetMethod("GetConversationId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method?.Invoke(null, [null as MafChatOptions]);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConversationId_PassedToResponseOptions_WhenProvided()
    {
        HermesContext.SetConversationId("conv_test");
        HermesAgent.Sdk.ResponseOptions? captured = null;

        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<HermesAgent.Sdk.ResponseOptions?>();
                return Task.FromResult(new HermesAgent.Sdk.ResponseResult { Id = "test", Output = new List<HermesAgent.Sdk.OutputItem>() });
            });

        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);
        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options: null);

        Assert.NotNull(captured);
        Assert.Equal("conv_test", captured.Conversation);
        HermesContext.Clear();
    }

    [Fact]
    public async Task ResponsesApi_PassesConversationInOptions_WhenConversationIdProvided()
    {
        HermesContext.SetConversationId("conv_resp");
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

        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);
        var options = new MafChatOptions { Tools = [AIFunctionFactory.Create(() => "echo")] };

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options);

        Assert.NotNull(capturedOptions);
        Assert.Equal("conv_resp", capturedOptions.Conversation);

        HermesContext.Clear();
    }

    [Fact]
    public async Task ResponsesApi_DoesNotSetConversation_WhenNoConversationId()
    {
        HermesContext.Clear();
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

        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);
        var options = new MafChatOptions { Tools = [AIFunctionFactory.Create(() => "echo")] };

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options);

        Assert.NotNull(capturedOptions);
        Assert.Null(capturedOptions.Conversation);
    }
}
