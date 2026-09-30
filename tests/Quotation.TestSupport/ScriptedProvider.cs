using Quotation.Contracts;
using System.Text.Json;
using Quotation.AI;

namespace Quotation.TestSupport;

/// <summary>
/// Test double for <see cref="IAIProvider"/>: a script plays the model's part, calling tools and
/// reading their outputs, so orchestration and safety rules can be tested without the network.
/// </summary>
public sealed class ScriptedProvider(Func<ScriptedProvider.Context, Task> script) : IAIProvider
{
    public string Name => "Scripted";
    public AiRunRequest? LastRequest { get; private set; }

    public sealed class Context(Func<AiToolCall, CancellationToken, Task<AiToolOutput>> execute)
    {
        private int _n;
        public List<AiToolCallRecord> Calls { get; } = [];

        public async Task<JsonElement> Call(string tool, object input)
        {
            var element = JsonSerializer.SerializeToElement(input);
            var output = await execute(new AiToolCall($"t{++_n}", tool, element), CancellationToken.None);
            Calls.Add(new AiToolCallRecord(tool, element.GetRawText(), output.Content, output.IsError));
            if (output.IsError) return default;
            try { return JsonDocument.Parse(output.Content).RootElement; }
            catch (JsonException) { return JsonSerializer.SerializeToElement(output.Content); }
        }
    }

    public async Task<AiRunResult> RunAsync(AiRunRequest request, Func<AiToolCall, CancellationToken, Task<AiToolOutput>> executeTool, CancellationToken ct)
    {
        LastRequest = request;
        var ctx = new Context(executeTool);
        await script(ctx);
        return new AiRunResult("", ctx.Calls, ctx.Calls.Count + 1, "end_turn", 1000, 200, "scripted");
    }

    public static object Draft(string? customerQuery, int? customerId, params object[] lines) => new
    {
        is_quotation_request = true,
        customer_query = customerQuery,
        customer_id = customerId,
        reference = (string?)null,
        summary = "test",
        notes = (string?)null,
        confidence = 0.99,
        lines,
    };

    public static object Line(string text, string query, int? productId, decimal? qty, string? specs = null) => new
    {
        requested_text = text,
        product_query = query,
        product_id = productId,
        quantity = qty,
        unit = (string?)null,
        specifications = specs,
    };
}
