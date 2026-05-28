using Microsoft.Extensions.AI;
using NSubstitute;

namespace HermesAgent.Sdk.MicrosoftAgent.Tests;

/// <summary>
/// Tests for <see cref="AutoSessionMiddleware"/> which automatically injects a
/// <c>hermes-conversation-id</c> (Topic ID) into <see cref="MafChatOptions.AdditionalProperties"/>
/// when one is not already present.
///
/// Generated IDs follow the format <c>"topic-{Guid:N}"</c> (e.g. <c>"topic-550e8400e29b41d4a716446655440000"</c>).
/// </summary>
public class AutoSessionMiddlewareTests
{
    private static readonly MafChatMessage TestMessage = new(ChatRole.User, "hello");
    private const string ConvIdKey = "hermes-conversation-id";

    private static (AutoSessionMiddleware Middleware, IChatClient Inner) CreateMiddleware()
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<MafChatMessage>>(), Arg.Any<MafChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new MafChatResponse(new MafChatMessage(ChatRole.Assistant, "")));
        inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<MafChatMessage>>(), Arg.Any<MafChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(EmptyAsyncEnumerable<MafChatResponseUpdate>());
        return (new AutoSessionMiddleware(inner), inner);
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>() { yield break; }

    // ──────────────────────────────────────────────
    //  GetResponseAsync — conversation ID injection
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetResponseAsync_NullOptions_DoesNotThrow()
    {
        var (middleware, _) = CreateMiddleware();

        await middleware.GetResponseAsync([TestMessage], options: null);
    }

    [Fact]
    public async Task GetResponseAsync_NullAdditionalProperties_InitializesAndAddsGuid()
    {
        var (middleware, _) = CreateMiddleware();
        var options = new MafChatOptions();

        await middleware.GetResponseAsync([TestMessage], options);

        Assert.NotNull(options.AdditionalProperties);
        Assert.True(options.AdditionalProperties.ContainsKey(ConvIdKey));
        var id = options.AdditionalProperties[ConvIdKey] as string;
        Assert.StartsWith("topic-", id);
        Assert.Matches(@"^topic-[0-9a-f]{32}$", id);
    }

    [Fact]
    public async Task GetResponseAsync_NoConversationId_InjectsGuid()
    {
        var (middleware, _) = CreateMiddleware();
        var options = new MafChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { ["other-key"] = "other-value" },
        };

        await middleware.GetResponseAsync([TestMessage], options);

        Assert.True(options.AdditionalProperties.ContainsKey(ConvIdKey));
        var id = options.AdditionalProperties[ConvIdKey] as string;
        Assert.StartsWith("topic-", id);
        Assert.Matches(@"^topic-[0-9a-f]{32}$", id);
        Assert.Equal("other-value", options.AdditionalProperties["other-key"]);
    }

    [Fact]
    public async Task GetResponseAsync_ExistingConversationId_NotOverridden()
    {
        var (middleware, _) = CreateMiddleware();
        var options = new MafChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ConvIdKey] = "my-existing-topic" },
        };

        await middleware.GetResponseAsync([TestMessage], options);

        Assert.Equal("my-existing-topic", options.AdditionalProperties[ConvIdKey]);
    }

    [Fact]
    public async Task GetResponseAsync_PassesConversationIdToInnerClient()
    {
        var (middleware, inner) = CreateMiddleware();

        MafChatOptions? captured = null;
        inner.GetResponseAsync(Arg.Any<IEnumerable<MafChatMessage>>(), Arg.Do<MafChatOptions?>(o => captured = o), Arg.Any<CancellationToken>())
            .Returns(new MafChatResponse(new MafChatMessage(ChatRole.Assistant, "")));

        var options = new MafChatOptions();
        await middleware.GetResponseAsync([TestMessage], options);

        Assert.NotNull(captured);
        Assert.NotNull(captured!.AdditionalProperties);
        Assert.True(captured.AdditionalProperties!.ContainsKey(ConvIdKey));
        var id = captured.AdditionalProperties[ConvIdKey] as string;
        Assert.StartsWith("topic-", id);
    }

    [Fact]
    public async Task GetResponseAsync_GeneratedId_IsUniquePerCall()
    {
        var (middleware, _) = CreateMiddleware();

        var options1 = new MafChatOptions();
        var options2 = new MafChatOptions();

        await middleware.GetResponseAsync([TestMessage], options1);
        await middleware.GetResponseAsync([TestMessage], options2);

        var id1 = options1.AdditionalProperties![ConvIdKey] as string;
        var id2 = options2.AdditionalProperties![ConvIdKey] as string;
        Assert.NotEqual(id1, id2);
    }

    // ──────────────────────────────────────────────
    //  GetStreamingResponseAsync — conversation ID injection
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetStreamingResponseAsync_NullOptions_DoesNotThrow()
    {
        var (middleware, _) = CreateMiddleware();

        await foreach (var u in middleware.GetStreamingResponseAsync([TestMessage], options: null)) { }
    }

    [Fact]
    public async Task GetStreamingResponseAsync_InjectsGuidWhenMissing()
    {
        var (middleware, _) = CreateMiddleware();
        var options = new MafChatOptions();

        await foreach (var _ in middleware.GetStreamingResponseAsync([TestMessage], options)) { }

        Assert.NotNull(options.AdditionalProperties);
        Assert.True(options.AdditionalProperties.ContainsKey(ConvIdKey));
        Assert.StartsWith("topic-", options.AdditionalProperties[ConvIdKey] as string);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ExistingConversationId_NotOverridden()
    {
        var (middleware, _) = CreateMiddleware();
        var options = new MafChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ConvIdKey] = "my-existing-topic" },
        };

        await foreach (var _ in middleware.GetStreamingResponseAsync([TestMessage], options)) { }

        Assert.Equal("my-existing-topic", options.AdditionalProperties[ConvIdKey]);
    }
}
