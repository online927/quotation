using Quotation.Core.Domain;

namespace Quotation.Pdf;

/// <summary>Party block (buyer or consignee) as printed.</summary>
public sealed record PdfParty(
    string Name,
    IReadOnlyList<string> AddressLines,
    string Gstin,
    string StateName,
    string StateCode,
    string ContactPerson = "",
    string Phone = "",
    string Email = "");

public sealed record PdfLine(
    int SlNo,
    string ItemName,
    IReadOnlyList<string> DescriptionLines,
    string Hsn,
    decimal? GstRate,
    string DueOn,
    decimal Quantity,
    string Unit,
    decimal Rate,
    decimal DiscountPercent,
    decimal Amount);

/// <summary>Additional ledger-style rows printed after the items (P&amp;F, taxes, round off).</summary>
public sealed record PdfSummaryRow(string Label, string RateText, decimal Amount);

/// <summary>Everything needed to print one quotation. Built by the server from the saved quotation.</summary>
public sealed class QuotationDocument
{
    public string Title { get; init; } = "QUOTATION";
    public required CompanySettings Company { get; init; }
    public byte[]? Logo { get; init; }

    public required string Number { get; init; }
    public required DateOnly Date { get; init; }
    public string BuyersReference { get; init; } = "";
    public DateOnly? BuyersReferenceDate { get; init; }
    public string DispatchedThrough { get; init; } = "";
    public string Destination { get; init; } = "";
    public string PaymentTerms { get; init; } = "";
    public string OtherReferences { get; init; } = "";
    public string TermsOfDelivery { get; init; } = "";

    public required PdfParty Buyer { get; init; }
    public required PdfParty Consignee { get; init; }

    public required IReadOnlyList<PdfLine> Lines { get; init; }
    public decimal Subtotal { get; init; }
    public IReadOnlyList<PdfSummaryRow> SummaryRows { get; init; } = [];
    public decimal GrandTotal { get; init; }
    public decimal TotalQuantity { get; init; }
    public string TotalQuantityUnit { get; init; } = "";
    public required string AmountInWords { get; init; }

    public string Remarks { get; init; } = "";
    public IReadOnlyList<string> TermsAndConditions { get; init; } = [];
    public string PreparedBy { get; init; } = "";
    public string VerifiedBy { get; init; } = "";

    /// <summary>Diagonal watermark (e.g. "DRAFT" for previews of unapproved quotations).</summary>
    public string? Watermark { get; init; }
}
