using Quotation.Contracts;

namespace Quotation.AI;

/// <summary>Read-only data access the AI tools are allowed to use. Implemented by the server.</summary>
public interface IQuotationDataTools
{
    Task<IReadOnlyList<CustomerCandidate>> SearchCustomersAsync(string query, int limit, CancellationToken ct);
    Task<IReadOnlyList<CustomerCandidate>> FindCustomersByEmailAsync(string email, CancellationToken ct);
    Task<CustomerCandidate?> GetCustomerAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<ProductCandidate>> SearchProductsAsync(string query, int limit, CancellationToken ct);
    Task<ProductCandidate?> GetProductAsync(int id, CancellationToken ct);
    Task<decimal?> GetCurrentRateAsync(int productId, CancellationToken ct);
    Task<IReadOnlyList<QuotationHistoryItem>> GetQuotationHistoryAsync(int? customerId, string? query, int limit, CancellationToken ct);
}

