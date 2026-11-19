namespace Quotation.Contracts;

public sealed record ProductSummaryDto(
    int Id,
    string Name,
    string PartNumber,
    string Brand,
    string StockGroup,
    string Unit,
    string Hsn,
    decimal? GstRate,
    decimal? Rate,
    string Description,
    /// <summary>Empty when <see cref="Rate"/> is the Tally selling price; otherwise where the last rate came from.</summary>
    string RateNote = "",
    /// <summary>Marked "Do Not Use" in its Tally description (e.g. DNU): not offered when making quotations.</summary>
    bool Hidden = false);

public sealed record SearchMatchDto(bool AllTermsMatched, bool ExactName, bool ExactIdentifier, bool UsedFuzzy, IReadOnlyList<string> MatchedFields);

public sealed record ProductSearchHitDto(ProductSummaryDto Product, double Score, SearchMatchDto Match)
{
    /// <summary>Starred by this user (shown first in searches when Stars is on).</summary>
    public bool IsStarred { get; init; }

    /// <summary>A heading over this row in a pick list ("★ YOUR STARRED …" / "OTHER MATCHES"; set by the app, not sent).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Heading { get; init; }

    /// <summary>Shown wherever a hit is displayed as text, so a raw record dump never reaches the user.</summary>
    public override string ToString() => Product.Name;
}

public sealed record ProductDetailDto(
    int Id,
    string Name,
    IReadOnlyList<string> Aliases,
    string PartNumber,
    string Brand,
    string Manufacturer,
    string Category,
    string StockGroup,
    string Unit,
    string Description,
    string Hsn,
    decimal? GstRate,
    string GstSource,
    decimal? Rate,
    DateOnly? RateDate,
    string RateSource,
    bool IsDeleted,
    DateTime LastSyncedUtc,
    bool Hidden = false);

public sealed record AddressDto(string Name, IReadOnlyList<string> Lines, string StateName, string StateCode, string Pincode);

public sealed record CustomerSummaryDto(
    int Id,
    string Name,
    string MailingName,
    string Address,
    string StateName,
    string StateCode,
    string Gstin,
    string ContactPerson,
    string Phone,
    string Email);

public sealed record CustomerSearchHitDto(CustomerSummaryDto Customer, double Score, SearchMatchDto Match)
{
    /// <summary>Starred by this user (shown first in searches when Stars is on).</summary>
    public bool IsStarred { get; init; }

    /// <summary>A heading over this row in a pick list ("★ YOUR STARRED …" / "OTHER MATCHES"; set by the app, not sent).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Heading { get; init; }

    /// <summary>Shown wherever a hit is displayed as text, so a raw record dump never reaches the user.</summary>
    public override string ToString() => Customer.Name;
}

public sealed record CustomerDetailDto(
    int Id,
    string Name,
    IReadOnlyList<string> Aliases,
    string MailingName,
    string LedgerGroup,
    IReadOnlyList<string> AddressLines,
    string StateName,
    string StateCode,
    string Pincode,
    string Country,
    string Gstin,
    bool GstinValid,
    string GstRegistrationType,
    string Pan,
    string ContactPerson,
    string Phone,
    string Mobile,
    string Email,
    IReadOnlyList<AddressDto> ShipToAddresses,
    bool IsDeleted,
    DateTime LastSyncedUtc);

/// <summary>Formater page: a typed customer or item name and the Tally ledger / item chosen for it ("Use"), for every PC.</summary>
public sealed record FormaterMatchDto(string Kind, string Typed, int TallyId, string TallyName = "")
{
    public const string Customer = "Customer", Product = "Product";

    /// <summary>The key a typed name is remembered by: lower case, single spaces.</summary>
    public static string Normalize(string typed) =>
        string.Join(' ', typed.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>The items and customers a user starred (Settings > Quotation > Stars, administrator switch).</summary>
public sealed record StarsDto(IReadOnlyList<int> ProductIds, IReadOnlyList<int> CustomerIds, bool Enabled = true);

/// <summary>Star (On) or unstar an item ("product") or a customer ("customer").</summary>
public sealed record StarRequest(string Kind, int Id, bool On);
