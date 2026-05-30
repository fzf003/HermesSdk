using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HermesAgent.Sdk.Extensions
{


    public static class LoggerExtensions
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        public static void LogResponse<T>(this ILogger logger, T response,
            string title = "Response", LogLevel level = LogLevel.Information)
        {
            if (!logger.IsEnabled(level)) return;

            var json = JsonSerializer.Serialize(response, _jsonOptions);
            var message = $"""
            ┌─────────────────── {title} ───────────────────
            {json}
            └────────────────────────────────────────────────
            """;

            logger.Log(level, message);
        }

        public static void LogRequest<T>(this ILogger logger, T request,
            string title = "Request", LogLevel level = LogLevel.Information)
        {
            if (!logger.IsEnabled(level)) return;

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var message = $"""
            ┌─────────────────── {title} ───────────────────
            {json}
            └────────────────────────────────────────────────
            """;

            logger.Log(level, message);
        }

        public static void LogException(this ILogger logger, Exception ex,
            object? context = null)
        {
            var contextJson = context is null ? "null"
                : JsonSerializer.Serialize(context, _jsonOptions);

            logger.LogError(ex, """
            ┌─────────────────── Exception ───────────────────
            Message   : {Message}
            Type      : {Type}
            Context   : {Context}
            StackTrace: {StackTrace}
            └──────────────────────────────────────────────────
            """,
                ex.Message,
                ex.GetType().FullName,
                contextJson,
                ex.StackTrace);
        }

        public static IDisposable LogScope(this ILogger logger,
            string operationName, object? parameters = null)
        {
            var paramJson = parameters is null ? "{}"
                : JsonSerializer.Serialize(parameters, _jsonOptions);

            logger.LogInformation("▶ [{Operation}] 开始 | 参数: {Params}",
                operationName, paramJson);

            return new LogScopeDisposable(logger, operationName);
        }

        private class LogScopeDisposable(ILogger logger, string operationName)
            : IDisposable
        {
            private readonly Stopwatch _sw = Stopwatch.StartNew();

            public void Dispose()
            {
                _sw.Stop();
                logger.LogInformation("◀ [{Operation}] 结束 | 耗时: {Elapsed}ms",
                    operationName, _sw.ElapsedMilliseconds);
            }
        }
    }
}
