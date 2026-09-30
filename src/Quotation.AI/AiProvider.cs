using Quotation.Contracts;
using System.Text.Json;

namespace Quotation.AI;

/// <summary>A tool the application offers to the model. Tools are executed by the application, never by the model.</summary>
public sealed record AiToolDefinition(string Name, string Description, JsonElement InputSchema);

public sealed record AiToolCall(string Id, string Name, JsonElement Input);

public sealed record AiToolOutput(string Content, bool IsError = false);

public sealed record AiRunRequest(
    string SystemPrompt,
    string UserMessage,
    IReadOnlyList<AiToolDefinition> Tools,
    int MaxTurns = 10);

public sealed record AiRunResult(
    string FinalText,
    IReadOnlyList<AiToolCallRecord> ToolCalls,
    int Turns,
    string StopReason,
    long InputTokens,
    long OutputTokens,
    string Model);

/// <summary>Raised for AI failures the user should see (not configured, refused, rate limited, unavailable).</summary>
public sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Abstraction over an LLM that can call application tools. The provider owns the conversation loop;
/// the application owns tool execution, validation and every decision based on the result.
/// </summary>
public interface IAIProvider
{
    string Name { get; }

    Task<AiRunResult> RunAsync(
        AiRunRequest request,
        Func<AiToolCall, CancellationToken, Task<AiToolOutput>> executeTool,
        CancellationToken ct);
}
