namespace HermesAgent.Sdk.MicrosoftAgent;

/// <summary>
/// Extension methods for <see cref="Microsoft.Extensions.AI.ChatOptions"/> to enable Hermes-specific features.
/// </summary>
public static class ChatOptionsExtensions
{
    private const string UseRunKey = "hermes-use-run";

    /// <summary>
    /// Marks this <see cref="Microsoft.Extensions.AI.ChatOptions"/> to route the request through Hermes Run + SSE mode.
    /// </summary>
    /// <param name="options">The chat options to configure.</param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    public static Microsoft.Extensions.AI.ChatOptions UseHermesRun(this Microsoft.Extensions.AI.ChatOptions options)
    {
        options.AdditionalProperties ??= [];
        options.AdditionalProperties[UseRunKey] = true;
        return options;
    }

    /// <summary>
    /// Returns <c>true</c> if the <c>hermes-use-run</c> flag is set.
    /// </summary>
    internal static bool IsHermesRunEnabled(this Microsoft.Extensions.AI.ChatOptions? options)
    {
        return options?.AdditionalProperties?.TryGetValue(UseRunKey, out var val) == true
            && val is true;
    }
}
