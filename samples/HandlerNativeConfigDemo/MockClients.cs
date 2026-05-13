using Microsoft.Extensions.Logging;

namespace HermesAgent.Sdk.WorkflowChain.Demo;

internal sealed class NullWebhookClient : IHermesWebhookClient
{
    public Task<WebhookSendResult> SendAsync<T>(string routeName, string eventType, T payload,
        WebhookOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

    public Task<WebhookSendResult> SendRawAsync(string routeName, string eventType, string rawJsonPayload,
        WebhookOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

    public Task<WebhookSendResult> SendDirectAsync(string routeName, string message,
        WebhookOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

    public void Dispose() { }
}

internal sealed class NullRunClient : IHermesRunClient
{
    public Task<string> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
        => Task.FromResult("run-mock");

    public async IAsyncEnumerable<RunEvent> SubscribeEventsAsync(string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new RunEvent { Type = "run.completed", OutPut = "mock-output" };
    }

    public Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new RunResult { RunId = "run-mock", Status = "completed" });

    public Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public void Dispose() { }
}
