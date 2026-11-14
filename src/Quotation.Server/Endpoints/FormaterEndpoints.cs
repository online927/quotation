using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Data;
using Quotation.Data.Entities;

namespace Quotation.Server.Endpoints;

/// <summary>Formater page: the Tally ledgers / items chosen for typed names, remembered for every PC (this app's database only).</summary>
public static class FormaterEndpoints
{
    public static void MapFormaterEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/formater/matches").RequireAuthorization();

        g.MapGet("/", async (QuotationDbContext db, CancellationToken ct) =>
        {
            var rows = await db.FormaterMatches.AsNoTracking().OrderBy(m => m.Kind).ThenBy(m => m.Typed).ToListAsync(ct);
            var customerIds = rows.Where(r => r.Kind == FormaterMatchDto.Customer).Select(r => r.TallyId).ToList();
            var productIds = rows.Where(r => r.Kind == FormaterMatchDto.Product).Select(r => r.TallyId).ToList();
            var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            // Entries whose ledger or item is no longer in the lists are left out.
            return rows.Select(r => new FormaterMatchDto(r.Kind, r.Typed, r.TallyId,
                    (r.Kind == FormaterMatchDto.Customer ? customers : products).GetValueOrDefault(r.TallyId) ?? ""))
                .Where(m => m.TallyName.Length > 0).ToList();
        });

        g.MapPut("/", async (FormaterMatchDto request, HttpContext ctx, QuotationDbContext db, CancellationToken ct) =>
        {
            var typed = FormaterMatchDto.Normalize(request.Typed);
            if (request.Kind is not (FormaterMatchDto.Customer or FormaterMatchDto.Product) || typed.Length == 0 || typed.Length > 200 || request.TallyId <= 0)
                return Results.BadRequest(new ApiError("A customer or item name and its Tally entry are needed.", null, ApiErrorCodes.Validation));
            var exists = request.Kind == FormaterMatchDto.Customer
                ? await db.Customers.AnyAsync(c => c.Id == request.TallyId, ct)
                : await db.Products.AnyAsync(p => p.Id == request.TallyId, ct);
            if (!exists) return Results.BadRequest(new ApiError("That Tally entry is not in the lists.", null, ApiErrorCodes.Validation));
            var row = await db.FormaterMatches.FirstOrDefaultAsync(m => m.Kind == request.Kind && m.Typed == typed, ct);
            if (row is null) db.FormaterMatches.Add(row = new FormaterMatch { Kind = request.Kind, Typed = typed });
            row.TallyId = request.TallyId;
            row.CreatedBy = ctx.User.Identity?.Name ?? "";
            row.CreatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new FormaterMatchDto(row.Kind, row.Typed, row.TallyId));
        });

        g.MapDelete("/", async (string kind, string typed, QuotationDbContext db, CancellationToken ct) =>
        {
            var key = FormaterMatchDto.Normalize(typed);
            await db.FormaterMatches.Where(m => m.Kind == kind && m.Typed == key).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }
}
