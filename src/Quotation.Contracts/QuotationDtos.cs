using Quotation.Core.Domain;

namespace Quotation.Contracts;

public sealed class PartyDto
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Gstin { get; set; } = "";
    public string StateName { get; set; } = "";
    public string StateCode { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class QuotationLineDto
{
    public int LineNo { get; set; }
    public int? ProductId { get; set; }
    public string ItemName { get; set; } = "";
    /// <summary>Extra description lines printed under the item (brand, model, MOQ …).</summary>
    public string Description { get; set; } = "";
    public string Hsn { get; set; } = "";
    public decimal? GstRate { get; set; }
    public string DueOn { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal Rate { get; set; }
    /// <summary>Selling rate from Tally when the line was created (read-only; shows overrides).</summary>
    public decimal? TallyRate { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Editable content of a quotation, sent by the client when creating/updating.</summary>
public sealed class SaveQuotationRequest
{
    /// <summary>Revision the client edited (optimistic concurrency). Ignored on create.</summary>
    public int Revision { get; set; }
    public DateOnly Date { get; set; }
    public int? CustomerId { get; set; }
    public PartyDto Buyer { get; set; } = new();
    public PartyDto Consignee { get; set; } = new();
    public bool ConsigneeSameAsBuyer { get; set; } = true;
    public string BuyersReference { get; set; } = "";
    public DateOnly? BuyersReferenceDate { get; set; }
    public string DispatchedThrough { get; set; } = "";
    public string PaymentTerms { get; set; } = "";
    public string OtherReferences { get; set; } = "";
    public string Destination { get; set; } = "";
    public string TermsOfDelivery { get; set; } = "";
    public string Remarks { get; set; } = "";
    public string TermsAndConditions { get; set; } = "";
    public decimal PackingForwarding { get; set; }
    public decimal? PackingForwardingPercent { get; set; }
    public string PreparedBy { get; set; } = "";
    public string VerifiedBy { get; set; } = "";
    public List<QuotationLineDto> Lines { get; set; } = [];
}

public sealed record TaxLineDto(string Name, decimal RatePercent, decimal TaxableValue, decimal Amount);

public sealed class QuotationDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public string? FinancialYear { get; set; }
    public DateOnly Date { get; set; }
    public QuotationStatus Status { get; set; }
    public QuotationSource Source { get; set; }
    public int Revision { get; set; }
    public int? CustomerId { get; set; }
    public PartyDto Buyer { get; set; } = new();
    public PartyDto Consignee { get; set; } = new();
    public bool ConsigneeSameAsBuyer { get; set; }
    public string BuyersReference { get; set; } = "";
    public DateOnly? BuyersReferenceDate { get; set; }
    public string DispatchedThrough { get; set; } = "";
    public string PaymentTerms { get; set; } = "";
    public string OtherReferences { get; set; } = "";
    public string Destination { get; set; } = "";
    public string TermsOfDelivery { get; set; } = "";
    public string Remarks { get; set; } = "";
    public string TermsAndConditions { get; set; } = "";
    public decimal PackingForwarding { get; set; }
    public decimal? PackingForwardingPercent { get; set; }
    public string PreparedBy { get; set; } = "";
    public string VerifiedBy { get; set; } = "";
    public List<QuotationLineDto> Lines { get; set; } = [];

    public decimal Subtotal { get; set; }
    public List<TaxLineDto> Taxes { get; set; } = [];
    public decimal TaxTotal { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TotalQuantity { get; set; }
    public string TotalQuantityUnit { get; set; } = "";
    public string AmountInWords { get; set; } = "";

    public bool HasPdf { get; set; }
    public string? PdfError { get; set; }
    public string? DataFreshnessWarning { get; set; }
    public Guid? DuplicatedFromId { get; set; }
    public int? SourceEmailId { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime? CancelledUtc { get; set; }
}

public sealed record QuotationSummaryDto(
    Guid Id,
    string? Number,
    DateOnly Date,
    QuotationStatus Status,
    QuotationSource Source,
    string CustomerName,
    string CustomerGstin,
    string FirstItem,
    int LineCount,
    decimal GrandTotal,
    string CreatedBy,
    DateTime CreatedUtc,
    string? ApprovedBy,
    bool HasPdf);

public sealed record ApproveRequest(bool OverrideStaleData = false, int? Revision = null);

public sealed record NextNumberDto(string FinancialYear, string Number, int Sequence, bool Provisional);

public sealed record NumberSeriesDto(string FinancialYear, int FinancialYearStart, int NextSequence, int MinimumAllowed, string NextNumberPreview);

public sealed record SetNextSequenceRequest(int FinancialYearStart, int NextSequence);

public sealed record ValidationErrorDto(string Field, string Message, int? LineNo);
