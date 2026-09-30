using Quotation.Core.Domain;

namespace Quotation.Contracts;

/// <summary>Connection to the TallyPrime XML/HTTP interface.</summary>
public sealed class TallySettings
{
    /// <summary>URL of a TallyPrime instance with the company open and the XML port enabled.</summary>
    public string Url { get; set; } = "http://localhost:9000";
    /// <summary>Exact company name as shown in Tally. Empty = use the company currently loaded.</summary>
    public string CompanyName { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
    /// <summary>Ledger groups (and their sub-groups) that contain customers.</summary>
    public List<string> CustomerGroups { get; set; } = ["Sundry Debtors"];
    /// <summary>StandardPrice (Standard Selling Price) or PriceLevel.</summary>
    public string RateSource { get; set; } = "StandardPrice";
    /// <summary>Price level name when RateSource = PriceLevel.</summary>
    public string PriceLevel { get; set; } = "";
    public int IncrementalSyncMinutes { get; set; } = 15;
    /// <summary>Local time (HH:mm) of the nightly full sync. Empty disables it.</summary>
    public string NightlyFullSyncTime { get; set; } = "02:00";
    public int ConnectionCheckSeconds { get; set; } = 60;
    /// <summary>"Selected" requests only the needed fields; "All" requests every field (slower, for troubleshooting).</summary>
    public string FetchMode { get; set; } = "Selected";
    /// <summary>Tally field used as the product brand: Category, StockGroup or None.</summary>
    public string BrandField { get; set; } = "Category";
    /// <summary>Tally field used as the product manufacturer: Category, StockGroup or None.</summary>
    public string ManufacturerField { get; set; } = "None";
    /// <summary>Derive a part number from item names such as "187-901-10-UNIVERSAL BEVEL PROTRACTOR" when Tally has none.</summary>
    public bool DerivePartNumberFromName { get; set; } = true;
    /// <summary>Optional override of the active financial year start (e.g. 2025). Null = detect from Tally.</summary>
    public int? ActiveFinancialYearOverride { get; set; }
}

public sealed class AiSettings
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Claude";
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>Write-only from the client: returned masked.</summary>
    public string? ApiKey { get; set; }
    public bool ApiKeyConfigured { get; set; }
    public int MaxCandidates { get; set; } = 8;
}

public sealed class GmailSettings
{
    public bool Enabled { get; set; }
    /// <summary>Gmail search query that selects quotation requests.</summary>
    public string Query { get; set; } = "label:Quotations newer_than:14d";
    public int PollMinutes { get; set; } = 3;
    /// <summary>OAuth client secret JSON (desktop app type). Write-only.</summary>
    public string? ClientSecretJson { get; set; }
    public bool ClientSecretConfigured { get; set; }
    public string? ConnectedAccount { get; set; }
}

public sealed class AllSettingsDto
{
    public CompanySettings Company { get; set; } = new();
    public QuotationSettings Quotation { get; set; } = new();
    public TallySettings Tally { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public GmailSettings Gmail { get; set; } = new();
}
