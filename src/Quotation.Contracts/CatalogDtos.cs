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
    string Description);

public sealed record SearchMatchDto(bool AllTermsMatched, bool ExactName, bool ExactIdentifier, bool UsedFuzzy, IReadOnlyList<string> MatchedFields);

public sealed record ProductSearchHitDto(ProductSummaryDto Product, double Score, SearchMatchDto Match);

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
    DateTime LastSyncedUtc);

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

public sealed record CustomerSearchHitDto(CustomerSummaryDto Customer, double Score, SearchMatchDto Match);

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
