global using Xunit;

// Aliases to disambiguate MAF types from HermesAgent.Sdk parent-namespace types.
// Parent-namespace lookup beats 'using' directives, so we use explicit aliases.
global using MafChatMessage = Microsoft.Extensions.AI.ChatMessage;
global using MafChatOptions = Microsoft.Extensions.AI.ChatOptions;
global using MafChatResponse = Microsoft.Extensions.AI.ChatResponse;
global using MafChatResponseUpdate = Microsoft.Extensions.AI.ChatResponseUpdate;
