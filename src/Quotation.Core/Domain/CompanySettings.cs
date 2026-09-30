namespace Quotation.Core.Domain;

/// <summary>
/// Company details printed on the quotation. Every value is configurable from the
/// Settings screen; nothing company-specific is hard-coded in the application.
/// </summary>
public sealed class CompanySettings
{
    public string CompanyName { get; set; } = "";
    /// <summary>Address lines exactly as they should print under the company name.</summary>
    public List<string> AddressLines { get; set; } = [];
    public string Mobile { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Gstin { get; set; } = "";
    public string StateName { get; set; } = "";
    public string StateCode { get; set; } = "";
    public string Email { get; set; } = "";
    public string Pan { get; set; } = "";
    public string MsmeNumber { get; set; } = "";
    public string Iec { get; set; } = "";

    public string BankAccountHolder { get; set; } = "";
    public string BankName { get; set; } = "";
    public string BankAccountNumber { get; set; } = "";
    public string BankBranch { get; set; } = "";
    public string BankIfsc { get; set; } = "";

    /// <summary>Optional logo file (PNG/JPG) stored on the server.</summary>
    public string? LogoPath { get; set; }

    public string AuthorisedSignatoryLabel { get; set; } = "Authorised Signatory";
    public string DefaultPreparedBy { get; set; } = "";
    public string DefaultVerifiedBy { get; set; } = "";

    public string Declaration { get; set; } =
        "We declare that this quotation shows the actual price of the goods described and that all particulars are true and correct.";
    public int QuotationValidityDays { get; set; } = 30;
    /// <summary>Text printed for validity; {days} and {date} are replaced.</summary>
    public string ValidityText { get; set; } = "This quotation is valid for {days} days from the date of issue.";
    public List<string> TermsAndConditions { get; set; } = [];
    public string DefaultPaymentTerms { get; set; } = "";
    public string DefaultTermsOfDelivery { get; set; } = "";
    public string FooterText { get; set; } = "This is a Computer Generated Document";
    public string Jurisdiction { get; set; } = "";
}

/// <summary>Quotation behaviour settings.</summary>
public sealed class QuotationSettings
{
    /// <summary>
    /// Number pattern. Tokens: {FY} = 2526, {FY_LABEL} = 2025-26, {FYS2}/{FYE2} = 25/26,
    /// {FYS4}/{FYE4} = 2025/2026, {SEQ} or {SEQ:n} = sequence (optionally zero-padded to n digits).
    /// </summary>
    public string NumberPattern { get; set; } = "TSQ{FY}-{SEQ}";
    /// <summary>First sequence number used when a new financial year starts.</summary>
    public int DefaultStartSequence { get; set; } = 1;
    /// <summary>Financial year start month (4 = April).</summary>
    public int FinancialYearStartMonth { get; set; } = 4;

    public TaxPresentation TaxPresentation { get; set; } = TaxPresentation.RateOnly;
    public bool RoundOffTotal { get; set; } = false;

    /// <summary>Default packing &amp; forwarding: either a fixed amount or a percentage of the subtotal.</summary>
    public decimal DefaultPackingForwardingAmount { get; set; }
    public decimal DefaultPackingForwardingPercent { get; set; }

    /// <summary>When duplicating a quotation, refresh rates from the latest synchronized Tally data.</summary>
    public bool RefreshRatesOnDuplicate { get; set; } = true;

    /// <summary>Allow generating a quotation when the last Tally sync is stale (with warning + audit).</summary>
    public bool AllowStaleDataOverride { get; set; } = true;
    /// <summary>Tally data older than this (hours) is considered stale.</summary>
    public int StaleDataHours { get; set; } = 24;

    /// <summary>Require HSN/SAC for every line before a PDF can be generated.</summary>
    public bool RequireHsn { get; set; } = true;

    public string CurrencyWordsPrefix { get; set; } = "INR";
}
