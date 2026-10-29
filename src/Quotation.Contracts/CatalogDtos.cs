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
