namespace Quotation.Contracts;

public sealed record TallyTestResultDto(
    bool Success,
    string Message,
    string? Company,
    IReadOnlyList<string> LoadedCompanies,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? ActiveFinancialYear,
    long ResponseMilliseconds);
