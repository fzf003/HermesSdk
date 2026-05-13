using HermesAgent.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HermesAgent.Sdk.Extensions;
using Microsoft.Extensions.Logging;

/// <summary>
/// ConsoleChat 示例程序
/// 演示如何使用 HermesAgent.Sdk 进行控制台聊天对话
/// 使用场景：快速测试聊天功能、开发调试、简单的命令行工具
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        var chatClient = host.Services.GetRequiredService<IHermesChatClient>();

        Console.WriteLine("🤖 Hermes Agent 控制台聊天示例");
        Console.WriteLine("输入 'exit' 退出，输入 'stream' 切换到流式模式");
        Console.WriteLine("--------------------------------");

        var useStreaming = false;

        while (true)
        {
            Console.Write("\n你: ");
            var input = Console.ReadLine();

            if (string.IsNullOrEmpty(input))
                continue;

            if (input.ToLower() == "exit")
                break;

            if (input.ToLower() == "stream")
            {
                useStreaming = !useStreaming;
                Console.WriteLine($"流式模式: {(useStreaming ? "开启" : "关闭")}");
                continue;
            }

            try
            {
                if (useStreaming)
                {
                    await DemonstrateStreamingChat(chatClient, input);
                }
                else
                {
                    await DemonstrateSyncChat(chatClient, input);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 发生错误: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 演示同步聊天
    /// 使用场景：需要完整响应后再进行下一步处理的场景
    /// </summary>
    static async Task DemonstrateSyncChat(IHermesChatClient chatClient, string message)
    {
        Console.Write("AI: ");

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage("user", message)
            },
            Temperature = 0.7f,
            MaxTokens = 1000
        };

        var response = await chatClient.ChatAsync(request);
        Console.WriteLine(response.Choices[0].Message.Content);
    }

    /// <summary>
    /// 演示流式聊天
    /// 使用场景：需要实时显示响应、长文本生成、用户体验更好的场景
    /// </summary>
    static async Task DemonstrateStreamingChat(IHermesChatClient chatClient, string message)
    {
        Console.Write("AI: ");

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage("user", message),
            },
            Stream = true,
            Temperature = 0.7f,
            MaxTokens = 1000
        };

        await foreach (var chunk in chatClient.ChatStreamAsync(request))
        {
            if (chunk?.Choices?.Count > 0)
            {
                var deltaContent = chunk.Choices[0].Delta?.Content;
                if (!string.IsNullOrEmpty(deltaContent))
                {
                    Console.Write(deltaContent);
                }
            }
        }
        Console.WriteLine(); // 换行
    }
     
    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true);
                config.AddUserSecrets<Program>();
                config.AddEnvironmentVariables();
         
            })
            .ConfigureServices((context, services) =>
            {
                // 配置 HermesAgent
                services.AddHermesAgent(context.Configuration);
              //  services.AddLogging(configure => configure.AddConsole().AddDebug());
            });
}