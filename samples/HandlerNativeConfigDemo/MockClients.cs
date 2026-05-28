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

    

        // 新增: 补全 IHermesRunClient 接口 (run-client-complete)
        public Task<RunStatusResponse?> GetRunStatusAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<RunStatusResponse?>(new RunStatusResponse { RunId = runId, Status = "completed" });

        public Task<StopRunResponse> StopRunAsync(string runId, CancellationToken ct = default)
            => Task.FromResult(new StopRunResponse { RunId = runId, Status = "stopping" });

        public Task<ApprovalResponse> ApproveRunAsync(string runId, ApprovalRequest approval, CancellationToken ct = default)
            => Task.FromResult(new ApprovalResponse { RunId = runId, Choice = approval.Choice, Resolved = 1 });
public void Dispose() { }
}

internal sealed class NullRunClient : IHermesRunClient
{
    public Task<RunStartResponse> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new RunStartResponse { RunId = "run-mock", Status = "started" });

    public async IAsyncEnumerable<RunEvent> SubscribeEventsAsync(string runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new RunEvent { Type = "run.completed", OutPut = "mock-output" };
    }

    public Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new RunResult { RunId = "run-mock", Status = "completed" });

    public Task RunWithLoggingAsync(string prompt, Action<RunEvent,string>? eventaction = null, ILogger? logger = null, CancellationToken ct = default)
        => Task.CompletedTask;

    

        // 新增: 补全 IHermesRunClient 接口 (run-client-complete)
        public Task<RunStatusResponse?> GetRunStatusAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<RunStatusResponse?>(new RunStatusResponse { RunId = runId, Status = "completed" });

        public Task<StopRunResponse> StopRunAsync(string runId, CancellationToken ct = default)
            => Task.FromResult(new StopRunResponse { RunId = runId, Status = "stopping" });

        public Task<ApprovalResponse> ApproveRunAsync(string runId, ApprovalRequest approval, CancellationToken ct = default)
            => Task.FromResult(new ApprovalResponse { RunId = runId, Choice = approval.Choice, Resolved = 1 });
public void Dispose() { }
}
