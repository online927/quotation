using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Data;
using Quotation.Server.Infrastructure;
using Quotation.Server.Services;

namespace Quotation.Server.Endpoints;

public static class QuotationEndpoints
{
    public static void MapQuotationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/quotations").RequireAuthorization();

        g.MapGet("/", async (QuotationDbContext db, string? q, QuotationStatus? status, QuotationSource? source,
            DateOnly? from, DateOnly? to, int? customerId, int? take, int? skip, CancellationToken ct) =>
        {
            var query = db.Quotations.AsNoTracking().AsQueryable();
            if (status is not null) query = query.Where(x => x.Status == status);
            if (source is not null) query = query.Where(x => x.Source == source);
            if (from is not null) query = query.Where(x => x.Date >= from);
            if (to is not null) query = query.Where(x => x.Date <= to);
            if (customerId is not null) query = query.Where(x => x.CustomerId == customerId);
            if (!string.IsNullOrWhiteSpace(q))
            {
                foreach (var term in q.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
                {
                    var like = "%" + term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
                    query = query.Where(x =>
                        EF.Functions.Like(x.Number!, like) ||
                        EF.Functions.Like(x.Buyer.Name, like) ||
                        EF.Functions.Like(x.Buyer.Gstin, like) ||
                        EF.Functions.Like(x.Consignee.Name, like) ||
                        EF.Functions.Like(x.BuyersReference, like) ||
                        EF.Functions.Like(x.OtherReferences, like) ||
                        x.Lines.Any(l => EF.Functions.Like(l.ItemName, like) || EF.Functions.Like(l.Description, like)));
                }
            }
            var rows = await query
                .OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedUtc)
                .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 100, 1, 1000))
                .Select(x => new QuotationSummaryDto(x.Id, x.Number, x.Date, x.Status, x.Source, x.Buyer.Name, x.Buyer.Gstin,
                    x.Lines.OrderBy(l => l.LineNo).Select(l => l.ItemName).FirstOrDefault() ?? "", x.Lines.Count, x.GrandTotal,
                    x.CreatedBy, x.CreatedUtc, x.ApprovedBy, x.PdfPath != null))
                .ToListAsync(ct);
            return rows;
        });

        g.MapGet("/{id:guid}", (Guid id, QuotationService svc, CancellationToken ct) => Handle(() => svc.GetAsync(id, ct)));

        g.MapPost("/", (SaveQuotationRequest r, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.CreateAsync(r, QuotationSource.Manual, User(ctx), ct)));

        g.MapPut("/{id:guid}", (Guid id, SaveQuotationRequest r, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.UpdateAsync(id, r, User(ctx), ct)));

        g.MapPost("/{id:guid}/approve", (Guid id, ApproveRequest r, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.ApproveAsync(id, r, User(ctx), ct)));

        g.MapPost("/{id:guid}/cancel", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.CancelAsync(id, User(ctx), ct)));

        g.MapPost("/{id:guid}/duplicate", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(() => svc.DuplicateAsync(id, User(ctx), ct)));

        g.MapPost("/{id:guid}/regenerate-pdf", (Guid id, QuotationService svc, HttpContext ctx, CancellationToken ct) =>
            Handle(async () =>
            {
                var q = await svc.LoadAsync(id, ct);
                await svc.GeneratePdfAsync(q, User(ctx), ct);
                return svc.ToDto(q);
            }));

        g.MapGet("/{id:guid}/pdf", async (Guid id, QuotationDbContext db, PdfStorage storage, AuditService audit, HttpContext ctx,
            CancellationToken ct) =>
        {
            var q = await db.Quotations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            var path = storage.FullPath(q?.PdfPath);
            if (q is null || path is null) return Results.NotFound(new ApiError("No PDF has been generated for this quotation."));
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "PdfDownloaded", "Quotation", id.ToString(), q.Number ?? "", ct);
            return Results.File(path, "application/pdf", Path.GetFileName(path));
        });

        g.MapGet("/{id:guid}/preview", async (Guid id, QuotationService svc, PdfQuotationRenderer pdf, CancellationToken ct) =>
        {
            try
            {
                return Results.File(await svc.PreviewPdfAsync(id, pdf, ct), "application/pdf", "preview.pdf");
            }
            catch (QuotationException ex)
            {
                return Results.Json(new ApiError(ex.Message, null, ex.Code), statusCode: ex.Status);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiError(ex.Message));
            }
        });

        g.MapGet("/{id:guid}/validate", (Guid id, QuotationService svc, CancellationToken ct) => Handle(async () =>
        {
            var q = await svc.LoadAsync(id, ct);
            return svc.Validate(q).Select(i => new ValidationErrorDto(i.Field, i.Message, i.LineNo)).ToList();
        }));

        g.MapGet("/next-number", (DateOnly? date, QuotationService svc, CancellationToken ct) =>
            Handle(() => svc.PreviewNextNumberAsync(date, ct)));

        var n = app.MapGroup("/api/numbering").RequireAuthorization(SystemEndpoints.AdminPolicy);
        n.MapGet("/", async (int? financialYearStart, NumberingService numbering, FinancialYearService fy, SettingsService settings,
            QuotationDbContext db, CancellationToken ct) =>
        {
            var year = financialYearStart is { } s ? new FinancialYear(s, settings.Quotation.FinancialYearStartMonth) : fy.GetActive().Year;
            return await numbering.GetSeriesAsync(db, year, ct);
        });
        n.MapPut("/", async (SetNextSequenceRequest r, NumberingService numbering, SettingsService settings, QuotationDbContext db,
            AuditService audit, HttpContext ctx, CancellationToken ct) =>
        {
            try
            {
                var year = new FinancialYear(r.FinancialYearStart, settings.Quotation.FinancialYearStartMonth);
                var result = await numbering.SetNextAsync(db, year, r.NextSequence, ct);
                await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "NumberSeriesChanged", "NumberSeries", year.Label,
                    $"Next={r.NextSequence}", ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ApiError(ex.Message));
            }
        });
    }

    internal static UserContext User(HttpContext ctx) => new(ctx.User.UserName(), ctx.User.DisplayName(), ctx.Machine());

    internal static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (QuotationException ex)
        {
            return Results.Json(new ApiError(ex.Message, ex.Details.Count > 0 ? ex.Details : null, ex.Code), statusCode: ex.Status);
        }
    }
}
