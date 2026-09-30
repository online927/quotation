using Quotation.AI;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Server.Services;

namespace Quotation.Server.Endpoints;

public static class AiEndpoints
{
    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ai").RequireAuthorization();

        g.MapPost("/analyze", (AnalyzeRequest r, AiService ai, HttpContext ctx, CancellationToken ct) =>
            QuotationEndpoints.Handle(async () =>
            {
                if (string.IsNullOrWhiteSpace(r.Text)) throw new QuotationException(400, ApiErrorCodes.Validation, "Type what should be quoted.");
                return await ai.AnalyzeAsync(new AnalyzeInput(r.Text.Trim()), QuotationSource.Ai, QuotationEndpoints.User(ctx), null, ct);
            }));

        g.MapGet("/requests", (AiRequestStatus? status, int? take, AiService ai, CancellationToken ct) =>
            QuotationEndpoints.Handle(() => ai.ListAsync(status, Math.Clamp(take ?? 100, 1, 500), ct)));

        g.MapGet("/requests/{id:int}", (int id, AiService ai, CancellationToken ct) => QuotationEndpoints.Handle(() => ai.GetAsync(id, ct)));

        g.MapPost("/requests/{id:int}/create-draft", (int id, CreateDraftFromAiRequest r, AiService ai, HttpContext ctx, CancellationToken ct) =>
            QuotationEndpoints.Handle(() => ai.CreateDraftAsync(id, r, QuotationEndpoints.User(ctx), ct)));

        g.MapPost("/requests/{id:int}/dismiss", (int id, AiService ai, HttpContext ctx, CancellationToken ct) =>
            QuotationEndpoints.Handle(() => ai.DismissAsync(id, QuotationEndpoints.User(ctx), ct)));
    }
}
