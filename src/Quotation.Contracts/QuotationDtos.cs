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
    public string PartNumber { get; set; } = "";
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
    /// <summary>Round 38 (PL-1): the item's product web page (from the loaded links; read-only, filled by the server).</summary>
    public string Link { get; set; } = "";
}

/// <summary>Editable content of a quotation, sent by the client when creating/updating.</summary>
public sealed class SaveQuotationRequest
{
    /// <summary>Revision the client edited (optimistic concurrency). Ignored on create.</summary>
    public int Revision { get; set; }
    /// <summary>Quotation or proforma invoice; set on create, cannot change afterwards.</summary>
    public DocumentType DocType { get; set; }
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
    public string DeliveryNote { get; set; } = "";
    public string DispatchDocNo { get; set; } = "";
    public DateOnly? DeliveryNoteDate { get; set; }
    public string Remarks { get; set; } = "";
    public string TermsAndConditions { get; set; } = "";
    public decimal PackingForwarding { get; set; }
    public decimal? PackingForwardingPercent { get; set; }
    public string PreparedBy { get; set; } = "";
    public string VerifiedBy { get; set; } = "";
    /// <summary>"Without GST" (0 % on all lines and P&amp;F, no tax lines). Set on create; the document keeps it.</summary>
    public bool WithoutGst { get; set; }
    /// <summary>GST by the buyer's state (true) or the ship-to state (false) when the two differ; null keeps the
    /// quotation's own choice (a new one follows the ship-to state, as before this choice existed).</summary>
    public bool? GstByBuyer { get; set; }
    /// <summary>Round 36: "USD" = rates in US dollars, without GST (set on create; the document keeps it). Null/"" = rupees.</summary>
    public string? Currency { get; set; }
    /// <summary>Round 36: 1 USD = this many rupees (USD quotations).</summary>
    public decimal? ExchangeRate { get; set; }
    /// <summary>The PDF layout chosen for this quotation (Formater page: Tally or Zoho); null = the user's own layout.</summary>
    public string? PdfLayout { get; set; }
    /// <summary>Made on the Formater page (set on create): the customer and items may be typed instead of picked from Tally.</summary>
    public bool FromFormater { get; set; }
    /// <summary>Round 38 (PL-1): print the "View product" links on this quotation's PDF (when Settings has them on).</summary>
    public bool ShowProductLinks { get; set; } = true;
    /// <summary>
    /// Round 38 (Q10): the PC's own key for a new quotation, so a create sent again (the answer was lost while the server was
    /// unreachable) returns the quotation already made instead of a second copy.
    /// </summary>
    public Guid? ClientKey { get; set; }
    public List<QuotationLineDto> Lines { get; set; } = [];
}

public sealed record TaxLineDto(string Name, decimal RatePercent, decimal TaxableValue, decimal Amount);

public sealed class QuotationDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public DocumentType DocType { get; set; }
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
    public string DeliveryNote { get; set; } = "";
    public string DispatchDocNo { get; set; } = "";
    public DateOnly? DeliveryNoteDate { get; set; }
    public string Remarks { get; set; } = "";
    public string TermsAndConditions { get; set; } = "";
    public decimal PackingForwarding { get; set; }
    public decimal? PackingForwardingPercent { get; set; }
    public string PreparedBy { get; set; } = "";
    public string VerifiedBy { get; set; } = "";
    public bool WithoutGst { get; set; }
    /// <summary>GST by the buyer's state (true) or the ship-to state (false) when the two differ.</summary>
    public bool GstByBuyer { get; set; }
    /// <summary>Round 36: "USD" or "" (rupees).</summary>
    public string Currency { get; set; } = "";
    public decimal? ExchangeRate { get; set; }
    public bool IsUsd => Currency == Currencies.Usd;
    /// <summary>The PDF layout chosen for this quotation (Formater page: Tally or Zoho); null = the user's own layout.</summary>
    public string? PdfLayout { get; set; }
    /// <summary>Round 38 (PL-1): print the "View product" links on this quotation's PDF.</summary>
    public bool ShowProductLinks { get; set; } = true;
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
    /// <summary>Approved at least once: only the administrator can cancel it.</summary>
    public bool EverApproved { get; set; }
    /// <summary>Who it belongs to (null: a Gmail draft nobody has saved yet).</summary>
    public string? Owner { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime? CancelledUtc { get; set; }
    /// <summary>Last day (office time) on which users who are not administrators may still edit or cancel it.</summary>
    public DateOnly EditableUntil { get; set; }
    /// <summary>Items of this quotation that are now marked "Do Not Use" (DNU) in Tally.</summary>
    public List<string> DoNotUseItems { get; set; } = [];
    public QuotationOutcome Outcome { get; set; }
    public string LostReason { get; set; } = "";
    public DateOnly? FollowUpDate { get; set; }
}

/// <summary>Won / Lost / Open, the reason when lost, and the follow-up date.</summary>
public sealed record SetOutcomeRequest(QuotationOutcome Outcome, string? LostReason = null, DateOnly? FollowUpDate = null);

/// <summary>A quotation in the administrator's Deleted list (kept 30 days).</summary>
public sealed record DeletedQuotationDto(
    Guid Id,
    string Number,
    DocumentType DocType,
    DateOnly Date,
    string CustomerName,
    decimal GrandTotal,
    string DeletedBy,
    DateTime DeletedUtc,
    DateTime PurgeUtc,
    /// <summary>False when the number was given to a new quotation meanwhile: a restore makes an un-numbered draft.</summary>
    bool NumberStillFree);

public sealed record RestoreResultDto(QuotationDto Quotation, bool NumberKept, string Message);

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
    bool HasPdf,
    DocumentType DocType = DocumentType.Quotation,
    QuotationOutcome Outcome = QuotationOutcome.Open,
    DateOnly? FollowUpDate = null,
    string? Owner = null,
    string Currency = "")
{
    /// <summary>A USD quotation (its amounts are in US dollars).</summary>
    public bool IsUsd => Currencies.IsUsd(Currency);
    /// <summary>"$ 390.00" or "₹ 31,200.00".</summary>
    public string TotalText => Currencies.Money(GrandTotal, Currency);
}

public sealed record ApproveRequest(bool OverrideStaleData = false, int? Revision = null);

public sealed record NextNumberDto(string FinancialYear, string Number, int Sequence, bool Provisional);

public sealed record NumberSeriesDto(string FinancialYear, int FinancialYearStart, int NextSequence, int MinimumAllowed, string NextNumberPreview);

public sealed record SetNextSequenceRequest(int FinancialYearStart, int NextSequence, DocumentType DocType = DocumentType.Quotation);

public sealed record ValidationErrorDto(string Field, string Message, int? LineNo);

/// <summary>Someone else who has the same quotation open: display name and PC.</summary>
public sealed record PresenceDto(string User, string Machine);

/// <summary>Round 36: currencies of quotations ("" = rupees).</summary>
public static class Currencies
{
    public const string Usd = "USD";
    /// <summary>Round 36: the rupees for 1 USD must be within this range (a typing slip like 0.84 or 8420 is refused).</summary>
    public const decimal MinUsdRate = 1m, MaxUsdRate = 500m;
    public static bool IsUsd(string? currency) => string.Equals(currency?.Trim(), Usd, StringComparison.OrdinalIgnoreCase);

    /// <summary>Round 36: an amount with its sign: "$ 390.00" (USD) or "₹ 31,200.00".</summary>
    public static string Money(decimal amount, string? currency, int decimals = 2) =>
        (IsUsd(currency) ? "$ " : "₹ ") + Quotation.Core.Calculation.IndianFormat.Amount(amount, decimals);

    /// <summary>
    /// A total of several documents: rupees and dollars are never added together ("₹ 6,20,400 + $ 390.00").
    /// </summary>
    public static string Total(IEnumerable<(decimal Amount, string? Currency)> amounts)
    {
        var list = amounts.ToList();
        var rupees = list.Where(a => !IsUsd(a.Currency)).Sum(a => a.Amount);
        var dollars = list.Where(a => IsUsd(a.Currency)).ToList();
        var text = "₹ " + Quotation.Core.Calculation.IndianFormat.Amount(rupees);
        return dollars.Count == 0 ? text : text + " + $ " + Quotation.Core.Calculation.IndianFormat.Amount(dollars.Sum(a => a.Amount));
    }
}
