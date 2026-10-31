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
    string Email = "",
    string Pan = "");

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
    decimal Amount,
    string PartNumber = "");

/// <summary>
/// Additional ledger-style rows printed after the items (P&amp;F, taxes, round off). <paramref name="Less"/> prints
/// "Less :" and the amount as "(-)0.08", as Tally does for a negative round off.
/// </summary>
public sealed record PdfSummaryRow(string Label, string RateText, decimal Amount, bool Less = false);

/// <summary>One row of the HSN/SAC-wise tax table of a proforma invoice.</summary>
public sealed record PdfHsnRow(string Hsn, decimal TaxableValue, decimal CentralRate, decimal CentralAmount, decimal StateRate,
    decimal StateAmount, decimal IntegratedRate, decimal IntegratedAmount)
{
    public decimal TotalTax => CentralAmount + StateAmount + IntegratedAmount;
}

/// <summary>"Printed on 23-Oct-26 at 18:11   By: keerthana" at the top of the first page.</summary>
public sealed record PrintStamp(DateTime LocalTime, string User);

/// <summary>Everything needed to print one quotation. Built by the server from the saved quotation.</summary>
public sealed record QuotationDocument
{
    public string Title { get; init; } = "QUOTATION";
    public DocumentType Kind { get; init; } = DocumentType.Quotation;
    public PrintStamp? Printed { get; init; }
    public required CompanySettings Company { get; init; }
    public byte[]? Logo { get; init; }
    /// <summary>Company seal stamped over "Authorised Signatory" on the last page (only on request, with the seal PIN).</summary>
    public byte[]? Seal { get; init; }

    public required string Number { get; init; }
    public required DateOnly Date { get; init; }
    public string BuyersReference { get; init; } = "";
    public DateOnly? BuyersReferenceDate { get; init; }
    public string DispatchedThrough { get; init; } = "";
    public string Destination { get; init; } = "";
    public string PaymentTerms { get; init; } = "";
    public string OtherReferences { get; init; } = "";
    public string TermsOfDelivery { get; init; } = "";
    // Proforma invoice references.
    public string DeliveryNote { get; init; } = "";
    public string DispatchDocNo { get; init; } = "";
    public DateOnly? DeliveryNoteDate { get; init; }
    /// <summary>HSN/SAC-wise tax table (proforma invoices).</summary>
    public IReadOnlyList<PdfHsnRow> HsnSummary { get; init; } = [];
    public bool InterState { get; init; }
    public string TaxAmountInWords { get; init; } = "";

    public required PdfParty Buyer { get; init; }
    /// <summary>Null when consignee details are not printed.</summary>
    public PdfParty? Consignee { get; init; }

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
