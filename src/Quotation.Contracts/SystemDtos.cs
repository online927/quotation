using Quotation.Core.Domain;

namespace Quotation.Contracts;

public sealed record LoginRequest(string Username, string Password, string? ClientMachine);

public sealed record LoginResponse(string Token, DateTime ExpiresUtc, UserDto User);

public sealed record UserDto(int Id, string Username, string DisplayName, UserRole Role, bool IsActive, bool MustChangePassword);

public sealed record CreateUserRequest(string Username, string DisplayName, string Password, UserRole Role);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record HealthDto(string Status, string Version, DateTime ServerTimeUtc);

/// <summary>Connection and data-freshness information shown on the dashboard and status bar.</summary>
public sealed record SystemStatusDto(
    bool TallyConnected,
    string? TallyCompany,
    string? TallyError,
    DateTime? TallyLastCheckedUtc,
    DateTime? LastSuccessfulSyncUtc,
    bool DataIsStale,
    string? ActiveFinancialYear,
    string ActiveFinancialYearSource,
    int ProductCount,
    int CustomerCount,
    bool GmailConnected,
    string? GmailAccount,
    bool AiConfigured,
    bool SyncRunning);

public sealed record DashboardDto(
    int TodaysQuotations,
    int Drafts,
    int PendingReview,
    int InboxNeedsAttention,
    SystemStatusDto Status);

public sealed record AuditEntryDto(long Id, DateTime AtUtc, string User, string Machine, string Action,
    string EntityType, string EntityId, string Details);

public sealed record ApiError(string Error, IReadOnlyList<string>? Details = null);
