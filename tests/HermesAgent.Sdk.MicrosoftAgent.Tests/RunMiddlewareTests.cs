using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HermesAgent.Sdk.MicrosoftAgent.Tests;

public class RunMiddlewareTests
{
    private readonly IChatClient _innerClient = Substitute.For<IChatClient>();
    private readonly IHermesRunClient _runClient = Substitute.For<IHermesRunClient>();
    private readonly ILogger<HermesRunMiddleware> _logger = Substitute.For<ILogger<HermesRunMiddleware>>();

    // ──────────────────────────────────────────────
    //  Routing decisions (8.5)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetResponse_WithoutFlag_PassesToInnerClient()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _innerClient.GetResponseAsync(Arg.Any<IEnumerable<MafChatMessage>>(), Arg.Any<MafChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new MafChatResponse(new MafChatMessage(ChatRole.Assistant, "echo")));

        await middleware.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options: null);

        await _innerClient.Received(1).GetResponseAsync(Arg.Any<IEnumerable<MafChatMessage>>(), Arg.Any<MafChatOptions?>(), Arg.Any<CancellationToken>());
        await _runClient.DidNotReceiveWithAnyArgs().StartAsync(default!);
    }

    [Fact]
    public async Task GetResponse_WithFlag_UsesRunClient()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _runClient.StartAsync(Arg.Any<string>(), Arg.Any<RunOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEvents(new RunEvent { Type = "delta", Text = "Hello" }, new RunEvent { Type = "run.completion" }));

        var result = await middleware.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], new MafChatOptions().UseHermesRun());

        await _runClient.Received(1).StartAsync("hello", Arg.Any<RunOptions?>(), Arg.Any<CancellationToken>());
        Assert.Contains(result.Messages, m => m.Text == "Hello");
    }

    [Fact]
    public async Task GetStreamingResponse_WithoutFlag_PassesToInnerClient()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _innerClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<MafChatMessage>>(), Arg.Any<MafChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(AsyncUpdates(new MafChatResponseUpdate(null, "echo")));

        var updates = new List<MafChatResponseUpdate>();
        await foreach (var u in middleware
            .GetStreamingResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options: null))
        {
            updates.Add(u);
        }

        _innerClient.Received(1).GetStreamingResponseAsync(Arg.Any<IEnumerable<MafChatMessage>>(), Arg.Any<MafChatOptions?>(), Arg.Any<CancellationToken>());
        Assert.Single(updates);
    }

    [Fact]
    public async Task GetStreamingResponse_WithFlag_UsesRunClient()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _runClient.StartAsync(Arg.Any<string>(), Arg.Any<RunOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEvents(new RunEvent { Type = "delta", Text = "Hello" }, new RunEvent { Type = "run.completion" }));

        var updates = new List<MafChatResponseUpdate>();
        await foreach (var u in middleware
            .GetStreamingResponseAsync([new MafChatMessage(ChatRole.User, "hello")], new MafChatOptions().UseHermesRun()))
        {
            updates.Add(u);
        }

        Assert.Single(updates);
        Assert.Equal("Hello", updates[0].Text);
    }

    // ──────────────────────────────────────────────
    //  RunEvent mapping (8.6)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunEventDelta_MapsToText()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _runClient.StartAsync(default!, default!, default!).ReturnsForAnyArgs(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(default!, default!).ReturnsForAnyArgs(
            AsyncEvents(new RunEvent { Type = "delta", Text = "Hello" }, new RunEvent { Type = "run.completion" }));

        var result = await middleware.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], new MafChatOptions().UseHermesRun());

        Assert.Equal("Hello", result.Messages[0].Text);
    }

    [Fact]
    public async Task RunEventDelta_EmptyText_Skipped()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _runClient.StartAsync(default!, default!, default!).ReturnsForAnyArgs(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(default!, default!).ReturnsForAnyArgs(
            AsyncEvents(new RunEvent { Type = "delta", Text = "" }, new RunEvent { Type = "run.completion" }));

        var result = await middleware.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], new MafChatOptions().UseHermesRun());

        Assert.Equal("", result.Messages[0].Text);
    }

    [Fact]
    public async Task RunEventCompletion_EndsEnumeration()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _runClient.StartAsync(default!, default!, default!).ReturnsForAnyArgs(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(default!, default!).ReturnsForAnyArgs(
            AsyncEvents(new RunEvent { Type = "run.completion" }));

        var result = await middleware.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], new MafChatOptions().UseHermesRun());

        Assert.Equal("", result.Messages[0].Text);
    }

    [Fact]
    public async Task RunEventError_ThrowsInvalidOperationException()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _runClient.StartAsync(default!, default!, default!).ReturnsForAnyArgs(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(default!, default!).ReturnsForAnyArgs(
            AsyncEvents(new RunEvent
            {
                Type = "run.error",
                Data = new Dictionary<string, object?> { ["message"] = "Something went wrong" },
            }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], new MafChatOptions().UseHermesRun()));

        Assert.Contains("Something went wrong", ex.Message);
    }

    [Fact]
    public async Task RunEventError_WithoutDataMessage_UsesDefaultMessage()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _runClient.StartAsync(default!, default!, default!).ReturnsForAnyArgs(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(default!, default!).ReturnsForAnyArgs(
            AsyncEvents(new RunEvent { Type = "run.error", Data = null }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], new MafChatOptions().UseHermesRun()));

        Assert.Contains("Unknown run error", ex.Message);
    }

    [Fact]
    public async Task RunEvent_MultipleDeltas_Concatenated()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _runClient.StartAsync(default!, default!, default!).ReturnsForAnyArgs(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(default!, default!).ReturnsForAnyArgs(
            AsyncEvents(
                new RunEvent { Type = "delta", Text = "Hello" },
                new RunEvent { Type = "delta", Text = " world" },
                new RunEvent { Type = "run.completion" }));

        var result = await middleware.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], new MafChatOptions().UseHermesRun());

        Assert.Equal("Hello world", result.Messages[0].Text);
    }

    [Fact]
    public async Task RunEvent_StreamingMultipleDeltas_YieldsEach()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        _runClient.StartAsync(default!, default!, default!).ReturnsForAnyArgs(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(default!, default!).ReturnsForAnyArgs(
            AsyncEvents(
                new RunEvent { Type = "delta", Text = "Hello" },
                new RunEvent { Type = "delta", Text = " world" },
                new RunEvent { Type = "run.completion" }));

        var updates = new List<MafChatResponseUpdate>();
        await foreach (var u in middleware
            .GetStreamingResponseAsync([new MafChatMessage(ChatRole.User, "hello")], new MafChatOptions().UseHermesRun()))
        {
            updates.Add(u);
        }

        Assert.Equal(2, updates.Count);
        Assert.Equal("Hello", updates[0].Text);
        Assert.Equal(" world", updates[1].Text);
    }

    [Fact]
    public async Task RunMiddleware_PassesModelIdInRunOptions()
    {
        var middleware = new HermesRunMiddleware(_innerClient, _runClient, _logger);

        RunOptions? capturedOptions = null;
        _runClient.StartAsync(Arg.Any<string>(), Arg.Do<RunOptions?>(o => capturedOptions = o), Arg.Any<CancellationToken>())
            .Returns(new RunStartResponse { RunId = "run_123", Status = "started" });
        _runClient.SubscribeEventsAsync(default!, default!).ReturnsForAnyArgs(
            AsyncEvents(new RunEvent { Type = "run.completion" }));

        var options = new MafChatOptions().UseHermesRun();
        options.ModelId = "gpt-4";

        await middleware.GetResponseAsync([new MafChatMessage(ChatRole.User, "hello")], options);

        Assert.NotNull(capturedOptions);
        Assert.Equal("gpt-4", capturedOptions!.Model);
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private static async IAsyncEnumerable<RunEvent> AsyncEvents(params RunEvent[] events)
    {
        foreach (var e in events)
        {
            yield return e;
        }
    }

    private static async IAsyncEnumerable<MafChatResponseUpdate> AsyncUpdates(params MafChatResponseUpdate[] updates)
    {
        foreach (var u in updates)
        {
            yield return u;
        }
    }
}
