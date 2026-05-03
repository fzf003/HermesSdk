using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class WorkflowRecoveryTests
{
    [Fact]
    public void WorkflowCheckpoint_RoundTripsStructuredContextValues()
    {
        var createdAt = new DateTime(2026, 4, 29, 10, 30, 0, DateTimeKind.Utc);
        var correlationId = Guid.Parse("6D3B52D0-4F0F-4B17-8EA0-0D3F9C05A812");
        var context = new WorkflowContext
        {
            InstanceId = "wf-structured",
            InitialInput = new Dictionary<string, object?>
            {
                ["attempt"] = 3,
                ["enabled"] = true,
                ["ratio"] = 1.0d,
                ["meta"] = new Dictionary<string, object?> { ["name"] = "demo" },
            },
        };
        context.StepOutputs["result"] = new DemoPayload
        {
            Status = "ok",
            Scores = new[] { 1, 2, 3 },
        };
        context.Data["tags"] = new object?[] { "a", "b" };
        context.Data["threshold"] = 0.75m;
        context.Data["createdAt"] = createdAt;
        context.Data["correlationId"] = correlationId;
        context.PendingStepIds.Add("agent-step");

        var instance = new WorkflowInstance
        {
            Context = context,
            EntryStepId = "agent-step",
            Status = "running",
            StartedAt = DateTime.UtcNow,
        };
        instance.InFlightStepIds.Add("agent-step");
        instance.StepRecords.Add(new StepRecord
        {
            StepId = "agent-step",
            StepType = "Agent",
            Status = StepStatus.Dispatched,
            StartedAt = DateTime.UtcNow,
            InputSnapshot = "{\"payload\":true}",
        });

        var checkpoint = WorkflowCheckpoint.FromInstance(instance);
        var restored = checkpoint.ToInstance();

        Assert.Equal(3, Assert.IsType<int>(restored.Context.InitialInput["attempt"]));
        Assert.True(Assert.IsType<bool>(restored.Context.InitialInput["enabled"]));
        Assert.Equal(1.0d, Assert.IsType<double>(restored.Context.InitialInput["ratio"]));

        var meta = Assert.IsType<Dictionary<string, object?>>(restored.Context.InitialInput["meta"]);
        // System.Text.Json 反序列化到 object 时值为 JsonElement，用 ToString() 验证
        Assert.Equal("demo", meta["name"]?.ToString());

        var result = Assert.IsType<DemoPayload>(restored.Context.StepOutputs["result"]);
        Assert.Equal("ok", result.Status);
        Assert.Equal(new[] { 1, 2, 3 }, result.Scores);

        var tags = Assert.IsType<object?[]>(restored.Context.Data["tags"]);
        Assert.Equal("a", tags[0]?.ToString());
        Assert.Equal("b", tags[1]?.ToString());
        Assert.Equal(0.75m, Assert.IsType<decimal>(restored.Context.Data["threshold"]));
        Assert.Equal(createdAt, Assert.IsType<DateTime>(restored.Context.Data["createdAt"]));
        Assert.Equal(correlationId, Assert.IsType<Guid>(restored.Context.Data["correlationId"]));

        Assert.Contains("agent-step", restored.Context.PendingStepIds);
        Assert.Contains("agent-step", restored.InFlightStepIds);
    }

    [Fact]
    public async Task HostedStartup_RestoresRunningInstances_And_InitializeAsync_IsIdempotent()
    {
        var store = new CountingStateStore();
        var checkpoint = CreateRunningCheckpoint();
        await store.SaveAsync(checkpoint);

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IHermesWebhookClient, NullWebhookClient>();
                services.AddSingleton<IHermesRunClient, NullRunClient>();
                services.AddWorkflowChain(chain =>
                {
                    chain.SetHeartbeatThreshold(TimeSpan.Zero);
                    chain.AddStep(new RecoveryAgentStepHandler());
                }, store);
            })
            .Build();

        await host.StartAsync();

        var engine = host.Services.GetRequiredService<WorkflowEngine>();
        var instance = engine.GetInstance(checkpoint.InstanceId);
        Assert.NotNull(instance);

        var record = Assert.Single(engine.GetStepRecords(checkpoint.InstanceId));
        Assert.Equal(StepStatus.Recovering, record.Status);
        Assert.Equal(1, store.ListRunningCalls);

        await engine.InitializeAsync();
        Assert.Equal(1, store.ListRunningCalls);

        await host.StopAsync();
    }

    private static WorkflowCheckpoint CreateRunningCheckpoint()
        => new()
        {
            InstanceId = "wf-recovery",
            EntryStepId = "agent-step",
            Status = "running",
            StartedAt = DateTime.UtcNow.AddMinutes(-2),
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-30),
            InitialInput = new Dictionary<string, JsonElement>
            {
                ["attempt"] = JsonSerializer.SerializeToElement(1),
            },
            PendingStepIds = new List<string> { "agent-step" },
            InFlightStepIds = new List<string> { "agent-step" },
            StepRecords = new List<StepRecord>
            {
                new()
                {
                    StepId = "agent-step",
                    StepType = "Agent",
                    Status = StepStatus.Dispatched,
                    StartedAt = DateTime.UtcNow.AddMinutes(-1),
                    InputSnapshot = "{\"instanceId\":\"wf-recovery\",\"stepId\":\"agent-step\"}",
                },
            },
        };

    private sealed class RecoveryAgentStepHandler : AgentStepHandler
    {
        public override string StepId => "agent-step";
        public override string RouteName => "workflow.recover";
        public override string EventType => "workflow.step";
        public override string BuildPrompt(WorkflowContext context) => "recover";
        public override Task<StepResult> ExecuteAsync(WorkflowContext context, CancellationToken ct)
            => Task.FromResult(Complete());
    }

    private sealed class DemoPayload
    {
        public string? Status { get; init; }
        public int[] Scores { get; init; } = [];
    }

    private sealed class CountingStateStore : IWorkflowStateStore
    {
        private readonly Dictionary<string, WorkflowCheckpoint> _store = new();
        public int ListRunningCalls { get; private set; }

        public Task SaveAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
        {
            _store[checkpoint.InstanceId] = checkpoint;
            return Task.CompletedTask;
        }

        public Task<WorkflowCheckpoint?> LoadAsync(string instanceId, CancellationToken ct = default)
        {
            _store.TryGetValue(instanceId, out var checkpoint);
            return Task.FromResult(checkpoint);
        }

        public Task DeleteAsync(string instanceId, CancellationToken ct = default)
        {
            _store.Remove(instanceId);
            return Task.CompletedTask;
        }

        public Task<List<string>> ListRunningAsync(CancellationToken ct = default)
        {
            ListRunningCalls++;
            return Task.FromResult(_store.Values
                .Where(checkpoint => checkpoint.Status == "running")
                .Select(checkpoint => checkpoint.InstanceId)
                .ToList());
        }

        public Task<List<string>> ListTimedOutAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_store.Values
                .Where(checkpoint => checkpoint.Status == "timed-out")
                .Select(checkpoint => checkpoint.InstanceId)
                .ToList());
        }
    }

    private sealed class NullWebhookClient : IHermesWebhookClient
    {
        public Task<WebhookSendResult> SendAsync<T>(string routeName, string eventType, T payload, WebhookOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

        public Task<WebhookSendResult> SendRawAsync(string routeName, string eventType, string rawJsonPayload, WebhookOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

        public Task<WebhookSendResult> SendDirectAsync(string routeName, string message, WebhookOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new WebhookSendResult { Status = "ok", HttpStatusCode = 200 });

        public void Dispose()
        {
        }
    }

    private sealed class NullRunClient : IHermesRunClient
    {
        public Task<string> StartAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
            => Task.FromResult("run-1");

        public async IAsyncEnumerable<RunEvent> SubscribeEventsAsync(
            string runId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }

        public Task<RunResult> RunAndWaitAsync(string prompt, RunOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new RunResult { RunId = "run-1", Status = "completed" });

        public Task RunWithLoggingAsync(string prompt, ILogger? logger = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
