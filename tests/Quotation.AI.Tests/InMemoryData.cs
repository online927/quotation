using Quotation.Contracts;
using Quotation.AI;
using Quotation.Core.Search;
using Tally.Simulator;

namespace Quotation.AI.Tests;

/// <summary>IQuotationDataTools over the simulator dataset using the real search engine.</summary>
public sealed class InMemoryData : IQuotationDataTools
{
    public sealed record P(int Id, string Name, string Aliases, string PartNo, string Brand, string Group, string Unit, string Hsn, decimal Gst, decimal? Rate, string Desc);
    public sealed record C(int Id, string Name, string State, string Gstin, string Email, string Phone, string Address);

    private readonly SearchIndex<P> _products;
    private readonly SearchIndex<C> _customers;
    public List<P> Products { get; }
    public List<C> Customers { get; }

    public InMemoryData()
    {
        var d = SampleDataset.Create(600, 150, today: new DateOnly(2026, 9, 30));
        Products = d.StockItems.Select((i, n) => new P(n + 1, i.Name, string.Join("\n", i.Aliases), i.PartNo, i.Category, i.Parent, i.Unit,
            i.Gst.FirstOrDefault()?.Hsn ?? "", i.Gst.FirstOrDefault()?.Rate ?? 18, i.StandardPrices.LastOrDefault().Rate is var r && r > 0 ? r : null, i.Description)).ToList();
        Customers = d.Ledgers.Where(l => l.Parent.Contains("Debtors")).Select((l, n) => new C(n + 1, l.Name, l.State, l.Gstin, l.Email, l.Mobile, string.Join(" ", l.Address))).ToList();
        _products = new SearchIndex<P>(Products,
        [
            new("Name", p => p.Name, 1.0, FieldKind.Identifier, IsPrimaryName: true),
            new("Alias", p => p.Aliases, 0.95, FieldKind.Identifier),
            new("PartNumber", p => p.PartNo, 1.2, FieldKind.Identifier),
            new("Brand", p => p.Brand, 0.6),
            new("Group", p => p.Group, 0.3),
        ]);
        _customers = new SearchIndex<C>(Customers,
        [
            new("Name", c => c.Name, 1.0, IsPrimaryName: true),
            new("GSTIN", c => c.Gstin, 1.2, FieldKind.Identifier),
            new("Email", c => c.Email, 0.6),
            new("Address", c => c.Address, 0.3),
        ]);
    }

    private static MatchEvidenceInfo E(MatchEvidence e, double score) => new(e.AllTokensMatched, e.ExactName, e.ExactIdentifier, e.UsedFuzzy, score);

    private static ProductCandidate ToCandidate(P p, MatchEvidenceInfo e) => new(p.Id, p.Name, p.PartNo, p.Brand, p.Unit, p.Hsn, p.Gst, p.Rate, p.Desc, e);
    private static CustomerCandidate ToCandidate(C c, MatchEvidenceInfo e) => new(c.Id, c.Name, c.State, c.Gstin, c.Email, "", e);

    public Task<IReadOnlyList<CustomerCandidate>> SearchCustomersAsync(string query, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CustomerCandidate>>(_customers.Search(query, limit).Select(h => ToCandidate(h.Item, E(h.Evidence, h.Score))).ToList());

    public Task<IReadOnlyList<CustomerCandidate>> FindCustomersByEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CustomerCandidate>>(Customers.Where(c => c.Email.Length > 0 && c.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            .Select(c => ToCandidate(c, new MatchEvidenceInfo(true, false, true, false, 100))).ToList());

    public Task<CustomerCandidate?> GetCustomerAsync(int id, CancellationToken ct) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Id == id) is { } c ? ToCandidate(c, new MatchEvidenceInfo(false, false, false, false, 0)) : null);

    public Task<IReadOnlyList<ProductCandidate>> SearchProductsAsync(string query, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProductCandidate>>(_products.Search(query, limit).Select(h => ToCandidate(h.Item, E(h.Evidence, h.Score))).ToList());

    public Task<ProductCandidate?> GetProductAsync(int id, CancellationToken ct) =>
        Task.FromResult(Products.FirstOrDefault(p => p.Id == id) is { } p ? ToCandidate(p, new MatchEvidenceInfo(false, false, false, false, 0)) : null);

    public Task<decimal?> GetCurrentRateAsync(int productId, CancellationToken ct) => Task.FromResult(Products.FirstOrDefault(p => p.Id == productId)?.Rate);

    public Task<IReadOnlyList<QuotationHistoryItem>> GetQuotationHistoryAsync(int? customerId, string? query, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<QuotationHistoryItem>>([]);

    public int ProductId(string name) => Products.Single(p => p.Name == name).Id;
    public int CustomerId(string name) => Customers.Single(c => c.Name == name).Id;
}
