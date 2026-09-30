using Quotation.Core.Domain;
using Quotation.Data.Entities;

namespace Quotation.Server.Services;

/// <summary>Placeholder until the PDF engine is registered.</summary>
public sealed class UnavailableRenderer : IQuotationDocumentRenderer
{
    public byte[] Render(QuotationHeader quotation, CompanySettings company, QuotationSettings settings) =>
        throw new InvalidOperationException("The PDF engine is not installed.");
}
