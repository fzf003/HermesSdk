using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.MicrosoftAgent;

/// <summary>
/// A <see cref="DelegatingChatClient"/> middleware that routes requests to Hermes Run + SSE mode
/// when the <c>hermes-use-run</c> flag is set on <see cref="Microsoft.Extensions.AI.ChatOptions"/>.
///
/// <para>Place this middleware in the <c>ChatClientBuilder</c> pipeline before the terminal adapter.
/// When the flag is absent, the request passes through to the inner client unchanged.</para>
/// </summary>
public class HermesRunMiddleware : DelegatingChatClient
{
    private readonly IHermesRunClient _runClient;

    /// <summary>
    /// Initializes the middleware.
    /// </summary>
    public HermesRunMiddleware(
        IChatClient innerClient,
        IHermesRunClient runClient,
        ILogger<HermesRunMiddleware> logger)
        : base(innerClient)
    {
        _runClient = runClient;
    }

    // ──────────────────────────────────────────────
    //  Non-streaming
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public override async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!options.IsHermesRunEnabled())
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            return response;
        }

        var messagesList = messages.ToList();
        var prompt = ExtractPrompt(messagesList);
        var startResp = await _runClient.StartAsync(prompt, ToRunOptions(options), cancellationToken);
        var runId = startResp.RunId;

        var textBuilder = new System.Text.StringBuilder();

        await foreach (var runEvent in _runClient.SubscribeEventsAsync(runId, cancellationToken))
        {
            switch (runEvent.Type)
            {
                case "message.delta":
                    
                    break;

                case "run.completion":
                    if (!string.IsNullOrEmpty(runEvent.Text))
                        textBuilder.Append(runEvent.Text);
                    break;
                case "reasoning.available":

                    break;

                case "run.error":
                    var errorMsg = runEvent.Data?.TryGetValue("message", out var m) == true ? m?.ToString() : "Unknown run error";
                    throw new InvalidOperationException($"Hermes Run error: {errorMsg}");
            }
        }

        var responseMessage = new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, textBuilder.ToString());
      
        return new Microsoft.Extensions.AI.ChatResponse(responseMessage);
    }

    // ──────────────────────────────────────────────
    //  Streaming
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public override async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!options.IsHermesRunEnabled())
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                yield return update;
            }
            yield break;
        }

        var messagesList = messages.ToList();
        var prompt = ExtractPrompt(messagesList);
        var startResp = await _runClient.StartAsync(prompt, ToRunOptions(options), cancellationToken);
        var runId = startResp.RunId;

        await foreach (var runEvent in _runClient.SubscribeEventsAsync(runId, cancellationToken))
        {
            switch (runEvent.Type)
            {
                case "delta":
                    if (!string.IsNullOrEmpty(runEvent.Text))
                    {
                        yield return new Microsoft.Extensions.AI.ChatResponseUpdate(null, runEvent.Text);
                    }
                    break;

                case "run.completion":
                    yield break;

                case "run.error":
                    var errorMsg = runEvent.Data?.TryGetValue("message", out var m) == true ? m?.ToString() : "Unknown run error";
                    throw new InvalidOperationException($"Hermes Run error: {errorMsg}");
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private static string ExtractPrompt(List<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User);
        return lastUser?.Text ?? string.Empty;
    }

    private static RunOptions? ToRunOptions(Microsoft.Extensions.AI.ChatOptions? options)
    {
        if (options is null)
            return null;

        return new RunOptions
        {
            Model = options.ModelId,
        };
    }
}
