using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HermesAgent.Sdk.MicrosoftAgent.Tests;

public class ChatOptionsMappingTests
{
    private readonly IHermesChatClient _chatClient = Substitute.For<IHermesChatClient>();
    private readonly ILogger<HermesChatClientAdapter> _logger = Substitute.For<ILogger<HermesChatClientAdapter>>();
    private readonly IHermesResponseClient _responseClient = Substitute.For<IHermesResponseClient>();

    private readonly HermesAgent.Sdk.ResponseResult _defaultResult = new()
    {
        Id = "test",
        Output = new List<HermesAgent.Sdk.OutputItem>()
    };

    [Fact]
    public async Task Temperature_MapsToResponseOptions()
    {
        HermesAgent.Sdk.ResponseOptions? captured = null;
        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<HermesAgent.Sdk.ResponseOptions?>();
                return Task.FromResult(_defaultResult);
            });

        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hi")], new MafChatOptions { Temperature = 0.7f });

        Assert.Equal(0.7f, captured!.Temperature);
    }

    [Fact]
    public async Task MaxOutputTokens_MapsToResponseOptions()
    {
        HermesAgent.Sdk.ResponseOptions? captured = null;
        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<HermesAgent.Sdk.ResponseOptions?>();
                return Task.FromResult(_defaultResult);
            });

        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hi")], new MafChatOptions { MaxOutputTokens = 512 });

        Assert.Equal(512, captured!.MaxOutputTokens);
    }

    [Fact]
    public async Task ModelId_MapsToResponseOptions()
    {
        HermesAgent.Sdk.ResponseOptions? captured = null;
        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<HermesAgent.Sdk.ResponseOptions?>();
                return Task.FromResult(_defaultResult);
            });

        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hi")], new MafChatOptions { ModelId = "gpt-4" });

        Assert.Equal("gpt-4", captured!.Model);
    }

    [Fact]
    public async Task NullOptions_UsesDefaults()
    {
        string? capturedInput = null;
        _responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<HermesAgent.Sdk.ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedInput = callInfo.Arg<string>();
                return Task.FromResult(_defaultResult);
            });

        var adapter = new HermesChatClientAdapter(_chatClient, _logger, _responseClient);

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hi")], options: null);

        // Model default 由 SDK HermesResponseClient.CreateAsync 处理，适配器传 null
        Assert.Equal("hi", capturedInput);
    }
}
