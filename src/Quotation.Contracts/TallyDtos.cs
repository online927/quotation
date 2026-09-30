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

public sealed record SyncRunDto(
    int Id,
    Quotation.Core.Domain.SyncKind Kind,
    Quotation.Core.Domain.SyncStatus Status,
    DateTime StartedUtc,
    DateTime? FinishedUtc,
    int ProductsChanged,
    int CustomersChanged,
    int ProductsDeleted,
    int CustomersDeleted,
    string TriggeredBy,
    string? Message);

public sealed record SyncStateDto(bool Running, string? Progress, SyncRunDto? LastRun, SyncRunDto? LastSuccessfulRun);
