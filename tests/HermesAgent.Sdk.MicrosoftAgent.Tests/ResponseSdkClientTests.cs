using System.Text.Json;
using HermesAgent.Sdk;
using HermesAgent.Sdk.MicrosoftAgent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HermesAgent.Sdk.MicrosoftAgent.Tests;

/// <summary>
/// Tests for SDK methods: <see cref="HermesAgent.Sdk.ResponseRequest"/> MAF extension,
/// <see cref="IHermesResponseClient.CreateAsync"/> and conversation key management.
/// </summary>
public class ResponseSdkClientTests
{
    // ──────────────────────────────────────────────
    //  6.1: ResponseRequest MAF field serialization
    // ──────────────────────────────────────────────

    [Fact]
    public void ResponseRequest_MafFields_SerializeCorrectly()
    {
        var request = new ResponseRequest
        {
            Model = "test-model",
            Input = "Hello",
            Stream = true,
            FrequencyPenalty = 0.5f,
            PresencePenalty = -0.3f,
            TopP = 0.9f,
            StopSequences = ["\n\n", "."],
            Temperature = 0.7f,
            MaxOutputTokens = 1000,
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        Assert.Contains("\"stream\":true", json);
        Assert.Contains("\"frequency_penalty\":0.5", json);
        Assert.Contains("\"presence_penalty\":-0.3", json);
        Assert.Contains("\"top_p\":0.9", json);
        Assert.Contains("\"stop\":[\"\\n\\n\",\".\"]", json);
        Assert.Contains("\"model\":\"test-model\"", json);
        Assert.Contains("\"input\":\"Hello\"", json);
        Assert.Contains("\"temperature\":0.7", json);
        Assert.Contains("\"max_output_tokens\":1000", json);
    }

    [Fact]
    public void ResponseRequest_MafFields_DefaultValues()
    {
        var request = new ResponseRequest
        {
            Input = "Hello",
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        // stream: false is a default value for non-nullable bool, will be serialized
        Assert.Contains("\"stream\":false", json);

        // Other MAF fields (nullable) should be absent
        Assert.DoesNotContain("\"frequency_penalty\"", json);
        Assert.DoesNotContain("\"presence_penalty\"", json);
        Assert.DoesNotContain("\"top_p\"", json);
        Assert.DoesNotContain("\"stop\"", json);

        // Core fields should be present
        Assert.Contains("\"input\":\"Hello\"", json);
    }

    // ──────────────────────────────────────────────
    //  6.3: CreateAsync returns ResponseResult
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsResponseResult()
    {
        // Use an HttpMessageHandler that returns a valid Responses API response
        var handler = new TestResponseHandler(jsonResponse: """
            {
                "id": "resp_123",
                "object": "response",
                "output": [
                    { "type": "message", "content": [{ "type": "text", "text": "Hello" }] }
                ],
                "usage": { "input_tokens": 10, "output_tokens": 5, "total_tokens": 15 }
            }
            """);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new HermesResponseClient(httpClient);

        var result = await client.CreateAsync("Hello");

        Assert.NotNull(result);
        Assert.Equal("resp_123", result.Id);
        Assert.Single(result.Output);
        Assert.Equal("message", result.Output[0].Type);
    }

    // ──────────────────────────────────────────────
    //  Session key management
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ResponsesApi_PassesConversationFromOptions_ToCreateAsync()
    {
        var logger = Substitute.For<ILogger<HermesChatClientAdapter>>();
        var responseClient = Substitute.For<IHermesResponseClient>();
        ResponseOptions? capturedOptions = null;

        responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedOptions = callInfo.Arg<ResponseOptions?>();
                return new HermesAgent.Sdk.ResponseResult
                {
                    Id = "resp_111",
                    Output = new List<HermesAgent.Sdk.OutputItem>
                {
                    new() { Type = "message", Contents = [new() { Type = "text", Text = "hi" }] }
                }
                };
            });

        var adapter = new HermesChatClientAdapter(logger, responseClient);

        // First call without conversation → null
        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hi")], options: null);
        Assert.Null(capturedOptions!.Conversation);

        // Second call with conversation in AdditionalProperties → passed through
        var chatOptions = new MafChatOptions
        {
            AdditionalProperties = new() { ["hermes-conversation-id"] = "my-session-key" },
            Tools = [AIFunctionFactory.Create(() => "hi")],
        };
        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hi again")], chatOptions);

        Assert.Equal("my-session-key", capturedOptions.Conversation);
    }

    [Fact]
    public async Task GetConversationId_UsesExplicitOverride()
    {
        var logger = Substitute.For<ILogger<HermesChatClientAdapter>>();
        var responseClient = Substitute.For<IHermesResponseClient>();

        responseClient.CreateAsync(Arg.Any<string>(), Arg.Any<ResponseOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new HermesAgent.Sdk.ResponseResult
            {
                Id = "resp_333",
                Output = new List<HermesAgent.Sdk.OutputItem>()
            });

        var adapter = new HermesChatClientAdapter(logger, responseClient);

        var chatOptions = new MafChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "echo")],
            AdditionalProperties = new() { ["hermes-conversation-id"] = "my-explicit-conv" },
        };

        await adapter.GetResponseAsync([new MafChatMessage(ChatRole.User, "hi")], chatOptions);

        // Should pass Conversation = "my-explicit-conv" in options
        await responseClient.Received(1).CreateAsync(
            Arg.Any<string>(),
            Arg.Is<ResponseOptions?>(o => o!.Conversation == "my-explicit-conv"),
            Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    //  6.2: CreateStreamingAsync SSE parsing
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateStreamingAsync_YieldsSseLines()
    {
        var sseStream = new System.IO.MemoryStream();
        var writer = new System.IO.StreamWriter(sseStream);
        writer.Write("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hello\"}\n\n");
        writer.Write("data: {\"type\":\"response.output_text.delta\",\"delta\":\" World\"}\n\n");
        writer.Write("data: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_1\"}}\n\n");
        writer.Flush();
        sseStream.Position = 0;

        var handler = new TestStreamResponseHandler(sseStream);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new HermesResponseClient(httpClient);

        var lines = new List<string>();
        await foreach (var line in client.CreateStreamingAsync("Hello"))
        {
            lines.Add(line);
        }

        Assert.Equal(3, lines.Count);
        Assert.Contains("Hello", lines[0]);
        Assert.Contains(" World", lines[1]);
        Assert.Contains("completed", lines[2]);
    }
}

/// <summary>
/// Test helper: returns a fixed JSON response for any POST request.
/// </summary>
public class TestResponseHandler : HttpMessageHandler
{
    private readonly string _jsonResponse;
    public TestResponseHandler(string jsonResponse) => _jsonResponse = jsonResponse;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(_jsonResponse, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// Test helper: returns a stream as the response content for SSE testing.
/// </summary>
public class TestStreamResponseHandler : HttpMessageHandler
{
    private readonly System.IO.Stream _responseStream;
    public TestStreamResponseHandler(System.IO.Stream stream) => _responseStream = stream;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StreamContent(_responseStream),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        return Task.FromResult(response);
    }
}
