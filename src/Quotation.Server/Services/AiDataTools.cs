using Microsoft.EntityFrameworkCore;
using Quotation.AI;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Core.Search;
using Quotation.Data;

namespace Quotation.Server.Services;

/// <summary>Read-only data access for AI tools, backed by the synchronized Tally data and search indexes.</summary>
public sealed class AiDataTools(QuotationDbContext db, CatalogSearchService search, QuotationService quotations, FinancialYearService fy) : IQuotationDataTools
{
    private static MatchEvidenceInfo E(MatchEvidence e, double score) => new(e.AllTokensMatched, e.ExactName, e.ExactIdentifier, e.UsedFuzzy, score);

    private static CustomerCandidate ToCandidate(CustomerDoc c, MatchEvidenceInfo e) =>
        new(c.Id, c.Name, c.StateName, c.Gstin, c.Email, c.Address.Split('\n').LastOrDefault() ?? "", e);

    private static ProductCandidate ToCandidate(ProductDoc p, MatchEvidenceInfo e) =>
        new(p.Id, p.Name, p.PartNumber, p.Brand, p.Unit, p.Hsn, p.GstRate, p.Rate, p.Description, e);

    public async Task<IReadOnlyList<CustomerCandidate>> SearchCustomersAsync(string query, int limit, CancellationToken ct) =>
        (await search.CustomersAsync(ct)).Search(query, limit).Select(h => ToCandidate(h.Item, E(h.Evidence, h.Score))).ToList();

    public async Task<IReadOnlyList<CustomerCandidate>> FindCustomersByEmailAsync(string email, CancellationToken ct)
    {
        var e = email.Trim().ToLowerInvariant();
        if (e.Length == 0) return [];
        var rows = await db.Customers.AsNoTracking().Where(c => !c.IsDeleted && c.Email.ToLower().Contains(e)).ToListAsync(ct);
        // Tally e-mail fields may hold several addresses separated by commas/semicolons.
        return rows.Where(c => c.Email.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Any(x => x.Equals(e, StringComparison.OrdinalIgnoreCase)))
            .Select(c => new CustomerCandidate(c.Id, c.Name, c.StateName, c.Gstin, c.Email, "", new MatchEvidenceInfo(true, false, true, false, 100)))
            .ToList();
    }

    public async Task<CustomerCandidate?> GetCustomerAsync(int id, CancellationToken ct)
    {
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        return c is null ? null : new CustomerCandidate(c.Id, c.Name, c.StateName, c.Gstin, c.Email, "", new MatchEvidenceInfo(false, false, false, false, 0));
    }

    public async Task<IReadOnlyList<ProductCandidate>> SearchProductsAsync(string query, int limit, CancellationToken ct) =>
        (await search.ProductsAsync(ct)).Search(query, limit).Select(h => ToCandidate(h.Item, E(h.Evidence, h.Score))).ToList();

    public async Task<ProductCandidate?> GetProductAsync(int id, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        return p is null ? null : new ProductCandidate(p.Id, p.Name, p.PartNumber, p.Brand, p.Unit, p.Hsn, p.GstRate, p.Rate, p.Description,
            new MatchEvidenceInfo(false, false, false, false, 0));
    }

    public async Task<decimal?> GetCurrentRateAsync(int productId, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId, ct);
        return p is null ? null : quotations.RateOn(p, fy.Today());
    }

    public async Task<IReadOnlyList<QuotationHistoryItem>> GetQuotationHistoryAsync(int? customerId, string? query, int limit, CancellationToken ct)
    {
        var q = db.Quotations.AsNoTracking().Where(x => x.Number != null && x.Status != QuotationStatus.Cancelled);
        if (customerId is not null) q = q.Where(x => x.CustomerId == customerId);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var like = "%" + query.Trim() + "%";
            q = q.Where(x => x.Lines.Any(l => EF.Functions.Like(l.ItemName, like)) || EF.Functions.Like(x.Buyer.Name, like));
        }
        return await q.OrderByDescending(x => x.Date).Take(limit)
            .Select(x => new QuotationHistoryItem(x.Number!, x.Date, x.Buyer.Name,
                string.Join(", ", x.Lines.OrderBy(l => l.LineNo).Select(l => l.Quantity + " x " + l.ItemName)), x.GrandTotal))
            .ToListAsync(ct);
    }
}
