using Quotation.Core.Domain;

namespace Quotation.Contracts;

public sealed record LoginRequest(string Username, string Password, string? ClientMachine);

public sealed record LoginResponse(string Token, DateTime ExpiresUtc, UserDto User);

public sealed record UserDto(int Id, string Username, string DisplayName, UserRole Role, bool IsActive, bool MustChangePassword,
    bool WithoutGstNext = false, string Theme = AppTheme.Light, string? PdfLayout = null,
    string TextSize = AppDisplay.Normal, string Spacing = AppDisplay.Comfortable, bool ReduceMotion = false);

/// <summary>Light or dark screens, chosen by each user (PDFs are always white).</summary>
public static class AppTheme
{
    public const string Light = "Light";
    public const string Dark = "Dark";
    /// <summary>Follows the Windows setting (light or dark apps).</summary>
    public const string System = "System";
    // Colour themes (My Settings > Appearance).
    public const string Sunset = "Sunset", Lagoon = "Lagoon", Graphite = "Graphite", Frost = "Frost", Glass = "Glass", GlassBlue = "GlassBlue", GlassBlack = "GlassBlack", GlassGreen = "GlassGreen", GlassMaroon = "GlassMaroon", NeonNight = "NeonNight", Cyberpunk = "Cyberpunk";

    public static readonly string[] All = [Light, Dark, System, Sunset, Lagoon, Graphite, Frost, Glass, GlassBlue, GlassBlack, GlassGreen, GlassMaroon, NeonNight, Cyberpunk];
    public static bool IsValid(string? theme) => theme is not null && All.Contains(theme);
    /// <summary>Themes drawn on dark screens.</summary>
    public static bool IsDarkTheme(string? theme) => theme is Dark or NeonNight or Cyberpunk or GlassBlack;
}

/// <summary>Text size and spacing, chosen by each user.</summary>
public static class AppDisplay
{
    public const string Normal = "Normal", Large = "Large";
    public const string Comfortable = "Comfortable", Compact = "Compact";
    public static bool IsValidTextSize(string? v) => v is Normal or Large;
    public static bool IsValidSpacing(string? v) => v is Comfortable or Compact;
}

/// <summary>Administrator's changes to a user (login name, display name, role, active).</summary>
public sealed record UpdateUserRequest(string Username, string DisplayName, UserRole Role, bool IsActive);

/// <summary>The current user's own switches ("My settings"); a null value is left unchanged.</summary>
public sealed record MyPreferencesDto(bool? WithoutGstNext = null, string? Theme = null, string? PdfLayout = null,
    string? TextSize = null, string? Spacing = null, bool? ReduceMotion = null);

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
    bool SyncRunning,
    /// <summary>Response time of Tally's last connection check, in milliseconds.</summary>
    double? TallyResponseMs = null,
    /// <summary>Tally answers slowly: background reading is paused (Tally stays responsive for its users).</summary>
    bool TallyBusy = false,
    string? TallyLoadText = null);

public sealed record DashboardDto(
    int TodaysQuotations,
    int Drafts,
    int PendingReview,
    int InboxNeedsAttention,
    SystemStatusDto Status,
    /// <summary>Open quotations whose follow-up date has come (only with outcome tracking on).</summary>
    int FollowUpDue = 0,
    bool TrackOutcomes = false);

public sealed record AuditEntryDto(long Id, DateTime AtUtc, string User, string Machine, string Action,
    string EntityType, string EntityId, string Details);

public sealed record ApiError(string Error, IReadOnlyList<string>? Details = null, string? Code = null);

public static class ApiErrorCodes
{
    public const string Validation = "VALIDATION";
    public const string StaleData = "STALE_DATA";
    public const string RatesChanged = "RATES_CHANGED";
    public const string Concurrency = "CONCURRENCY";
    public const string InvalidState = "INVALID_STATE";
    /// <summary>Too old to be changed by a user who is not an administrator.</summary>
    public const string Locked = "LOCKED";
    public const string Forbidden = "FORBIDDEN";
    public const string WrongPin = "WRONG_PIN";
    public const string SealBlocked = "SEAL_BLOCKED";
}

public sealed record RecentItemDto(int Id, string Name);

public sealed record RecentItemsDto(IReadOnlyList<RecentItemDto> Customers, IReadOnlyList<RecentItemDto> Products);

public sealed record DiagnosticsDto(
    string ServerVersion,
    string Runtime,
    string OperatingSystem,
    string MachineName,
    string DataDirectory,
    long DatabaseBytes,
    int Products,
    int Customers,
    int Quotations,
    int PdfFiles,
    SystemStatusDto Status,
    IReadOnlyList<string> LogFiles);

/// <summary>
/// A request that took longer than a second: <paramref name="TotalMs"/> measured on the client PC (click to answer),
/// <paramref name="ServerMs"/> by the server; <paramref name="Busy"/> is what else the server was doing (e.g. "Tally sync").
/// </summary>
public sealed record SlowRequestDto(string Id, DateTime WhenUtc, string User, string Machine, string What, double? TotalMs,
    double? ServerMs, string Busy);

/// <summary>Sent by a client PC after a slow request.</summary>
public sealed record SlowRequestReport(string? RequestId, string What, double TotalMs, double? ServerMs, string? ServerBusy);

/// <summary>Something an administrator should look at (dashboard): "warn" (orange) or "bad" (red).</summary>
public sealed record HealthWarningDto(string Level, string Text)
{
    public bool IsBad => Level == "bad";
}

/// <summary>What a restore brought back.</summary>
public sealed record RestoreSummaryDto(int Quotations, int Pdfs, bool Seal, bool Logo);

/// <summary>The client version the server hands out (null when it has none), whether clients must update, and its size.</summary>
public sealed record ClientUpdateDto(string? Version, bool Required, long Size);
