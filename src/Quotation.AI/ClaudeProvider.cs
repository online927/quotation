using Quotation.Contracts;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Model = Anthropic.Models.Messages.Model;
using Microsoft.Extensions.Logging;

namespace Quotation.AI;

public sealed record ClaudeOptions(string ApiKey, string Model = "claude-opus-5-5", string Effort = "medium", int MaxTokens = 16000);

/// <summary>
/// Claude via the official Anthropic C# SDK. Manual tool loop so the application executes every tool.
/// Uses adaptive thinking (always on for Claude Opus 5.5) with an explicit effort level, append-only
/// history (thinking blocks echoed back unchanged), prompt caching on the static system prompt and
/// tools, and server-side refusal fallback.
/// </summary>
public sealed class ClaudeProvider(ClaudeOptions options, ILogger<ClaudeProvider>? log = null) : IAIProvider
{
    private readonly AnthropicClient _client = new() { ApiKey = options.ApiKey };

    public string Name => "Claude";

    public async Task<AiRunResult> RunAsync(AiRunRequest request, Func<AiToolCall, CancellationToken, Task<AiToolOutput>> executeTool,
        CancellationToken ct)
    {
        var tools = request.Tools.Select((t, i) => (BetaToolUnion)new BetaTool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = ToSchema(t.InputSchema),
            Strict = true,
            // Cache breakpoint after the (static) tool list.
            CacheControl = i == request.Tools.Count - 1 ? new BetaCacheControlEphemeral() : null,
        }).ToList();

        List<BetaMessageParam> messages = [new() { Role = Role.User, Content = request.UserMessage }];
        var calls = new List<AiToolCallRecord>();
        long inputTokens = 0, outputTokens = 0;
        var finalText = "";
        var stopReason = "";

        for (var turn = 1; turn <= request.MaxTurns; turn++)
        {
            BetaMessage response;
            try
            {
                response = await _client.Beta.Messages.Create(new MessageCreateParams
                {
                    Model = options.Model,
                    MaxTokens = options.MaxTokens,
                    System = new List<BetaTextBlockParam>
                    {
                        new() { Text = request.SystemPrompt, CacheControl = new BetaCacheControlEphemeral() },
                    },
                    Thinking = new BetaThinkingConfigAdaptive(),
                    OutputConfig = new BetaOutputConfig { Effort = ParseEffort(options.Effort) },
                    Tools = tools,
                    // Server-side refusal fallback: a declined request is re-served by the fallback model.
                    Betas = [AnthropicBeta.ServerSideFallback2026_06_01],
                    Fallbacks = new List<BetaFallbackParam> { new(Model.ClaudeOpus4_8) },
                    Messages = messages,
                }, ct);
            }
            catch (AnthropicRateLimitException ex)
            {
                throw new AiUnavailableException("Claude is rate limited right now. Please try again in a minute.", ex);
            }
            catch (AnthropicBadRequestException ex)
            {
                log?.LogError("Claude rejected the request: {Message}", ex.Message);
                throw new AiUnavailableException("Claude rejected the request (check the model name in Settings).", ex);
            }
            catch (Anthropic5xxException ex)
            {
                throw new AiUnavailableException("Claude is temporarily unavailable. Please try again shortly.", ex);
            }
            catch (AnthropicApiException ex)
            {
                throw new AiUnavailableException("Claude API error: " + ex.Message, ex);
            }
            catch (AnthropicIOException ex)
            {
                throw new AiUnavailableException("Cannot reach the Claude API (network).", ex);
            }

            inputTokens += response.Usage.InputTokens;
            outputTokens += response.Usage.OutputTokens;
            stopReason = response.StopReason?.ToString() ?? "";

            if (stopReason == "refusal")
            {
                throw new AiUnavailableException("Claude declined to process this request. Please create the quotation manually.");
            }

            // Echo the assistant turn back unchanged (append-only; thinking signatures preserved).
            List<BetaContentBlockParam> assistant = [];
            List<BetaContentBlockParam> results = [];
            var texts = new List<string>();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var text))
                {
                    assistant.Add(new BetaTextBlockParam { Text = text.Text });
                    texts.Add(text.Text);
                }
                else if (block.TryPickThinking(out var thinking))
                {
                    assistant.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out var redacted))
                {
                    assistant.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    assistant.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                    var input = JsonSerializer.SerializeToElement(toolUse.Input);
                    AiToolOutput output;
                    try
                    {
                        output = await executeTool(new AiToolCall(toolUse.ID, toolUse.Name, input), ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        output = new AiToolOutput("Tool failed: " + ex.Message, IsError: true);
                    }
                    calls.Add(new AiToolCallRecord(toolUse.Name, input.GetRawText(), output.Content, output.IsError));
                    results.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = output.Content, IsError = output.IsError });
                }
            }
            finalText = string.Join("\n", texts);
            messages.Add(new BetaMessageParam { Role = Role.Assistant, Content = assistant });

            if (results.Count == 0 || stopReason != "tool_use")
            {
                return new AiRunResult(finalText, calls, turn, stopReason, inputTokens, outputTokens, options.Model);
            }
            messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
        }
        return new AiRunResult(finalText, calls, request.MaxTurns, "max_turns", inputTokens, outputTokens, options.Model);
    }

    private static InputSchema ToSchema(JsonElement schema)
    {
        var properties = new Dictionary<string, JsonElement>();
        if (schema.TryGetProperty("properties", out var props))
        {
            foreach (var p in props.EnumerateObject()) properties[p.Name] = p.Value.Clone();
        }
        var required = schema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(r => r.GetString()!).ToList()
            : [];
        var raw = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(properties),
            ["required"] = JsonSerializer.SerializeToElement(required),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false), // required for strict tools
        };
        return InputSchema.FromRawUnchecked(raw);
    }

    private static Effort ParseEffort(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };
}
