using Quotation.Core.Domain;

namespace Quotation.Data.Entities;

/// <summary>Stock item synchronized from Tally. Tally is authoritative; never edited locally.</summary>
public class Product
{
    public int Id { get; set; }
    public string TallyGuid { get; set; } = "";
    public long AlterId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Aliases, newline separated.</summary>
    public string Aliases { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Stock item description (may be multi-line).</summary>
    public string Description { get; set; } = "";
    public string StockGroup { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Hsn { get; set; } = "";
    public decimal? GstRate { get; set; }
    /// <summary>Where HSN/GST came from: Item, Group, Company, or empty if unknown.</summary>
    public string GstSource { get; set; } = "";
    public decimal? Rate { get; set; }
    public DateOnly? RateDate { get; set; }
    public string RateSource { get; set; } = "";
    public string Moq { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime LastSyncedUtc { get; set; }
}

public class StockGroup
{
    public int Id { get; set; }
    public string TallyGuid { get; set; } = "";
    public long AlterId { get; set; }
    public string Name { get; set; } = "";
    public string Parent { get; set; } = "";
    public string Hsn { get; set; } = "";
    public decimal? GstRate { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime LastSyncedUtc { get; set; }
}

public class Unit
{
    public int Id { get; set; }
    public string TallyGuid { get; set; } = "";
    public long AlterId { get; set; }
    public string Name { get; set; } = "";
    public string FormalName { get; set; } = "";
    public int DecimalPlaces { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime LastSyncedUtc { get; set; }
}

/// <summary>Customer ledger synchronized from Tally.</summary>
public class Customer
{
    public int Id { get; set; }
    public string TallyGuid { get; set; } = "";
    public long AlterId { get; set; }
    public string Name { get; set; } = "";
    public string Aliases { get; set; } = "";
    public string MailingName { get; set; } = "";
    public string LedgerGroup { get; set; } = "";
    /// <summary>Address lines, newline separated.</summary>
    public string Address { get; set; } = "";
    public string StateName { get; set; } = "";
    public string StateCode { get; set; } = "";
    public string Pincode { get; set; } = "";
    public string Country { get; set; } = "";
    public string Gstin { get; set; } = "";
    public string GstRegistrationType { get; set; } = "";
    public string Pan { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime LastSyncedUtc { get; set; }
}

/// <summary>Party block printed on a quotation (buyer or consignee). Stored as a snapshot.</summary>
public class PartySnapshot
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

public class QuotationHeader
{
    public Guid Id { get; set; }
    /// <summary>Null until a user saves/approves the quotation.</summary>
    public string? Number { get; set; }
    public int? FinancialYearStart { get; set; }
    public int? Sequence { get; set; }
    public DateOnly Date { get; set; }
    public QuotationStatus Status { get; set; }
    public QuotationSource Source { get; set; }

    public int? CustomerId { get; set; }
    public PartySnapshot Buyer { get; set; } = new();
    public PartySnapshot Consignee { get; set; } = new();
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
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TotalQuantity { get; set; }
    public string AmountInWords { get; set; } = "";

    public string PreparedBy { get; set; } = "";
    public string VerifiedBy { get; set; } = "";

    public string? PdfPath { get; set; }
    public string? PdfError { get; set; }

    /// <summary>Set when the quotation was generated while Tally data was stale (override).</summary>
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

    /// <summary>Optimistic concurrency token; incremented on every update.</summary>
    public int Revision { get; set; }

    public List<QuotationLine> Lines { get; set; } = [];
}

public class QuotationLine
{
    public int Id { get; set; }
    public Guid QuotationId { get; set; }
    public int LineNo { get; set; }
    public int? ProductId { get; set; }
    public string ItemName { get; set; } = "";
    /// <summary>Additional description lines printed under the item name (brand, model, MOQ …).</summary>
    public string Description { get; set; } = "";
    public string Hsn { get; set; } = "";
    public decimal GstRate { get; set; }
    public string DueOn { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal Rate { get; set; }
    /// <summary>Rate from Tally at the time the line was created, to show overrides.</summary>
    public decimal? TallyRate { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal Amount { get; set; }
}

public class NumberSeries
{
    public int Id { get; set; }
    public string SeriesKey { get; set; } = "QUOTATION";
    public int FinancialYearStart { get; set; }
    public int NextSequence { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public class EmailMessage
{
    public int Id { get; set; }
    public string GmailMessageId { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public DateTime ReceivedUtc { get; set; }
    public string From { get; set; } = "";
    public string Subject { get; set; } = "";
    public string BodyText { get; set; } = "";
    public EmailProcessingStatus Status { get; set; }
    public Guid? QuotationId { get; set; }
    public string ProcessingResult { get; set; } = "";
    /// <summary>JSON of the AI extraction + candidate lists shown in the AI Inbox.</summary>
    public string? AnalysisJson { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
}

public class SyncRun
{
    public int Id { get; set; }
    public SyncKind Kind { get; set; }
    public SyncStatus Status { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public int ProductsChanged { get; set; }
    public int CustomersChanged { get; set; }
    public int ProductsDeleted { get; set; }
    public int CustomersDeleted { get; set; }
    public long? MaxAlterId { get; set; }
    public string? Message { get; set; }
}

public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
}

public class ApiSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    /// <summary>SHA-256 of the bearer token; the token itself is never stored.</summary>
    public string TokenHash { get; set; } = "";
    public string ClientMachine { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

public class SettingEntry
{
    public string Key { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTime UpdatedUtc { get; set; }
    public string UpdatedBy { get; set; } = "";
}

public class AuditEntry
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; }
    public string User { get; set; } = "";
    public string Machine { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Details { get; set; } = "";
}
