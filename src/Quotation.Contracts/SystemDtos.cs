using Quotation.Core.Domain;

namespace Quotation.Contracts;

public sealed record LoginRequest(string Username, string Password, string? ClientMachine);

public sealed record LoginResponse(string Token, DateTime ExpiresUtc, UserDto User);

public sealed record UserDto(int Id, string Username, string DisplayName, UserRole Role, bool IsActive, bool MustChangePassword,
    bool WithoutGstNext = false, string Theme = AppTheme.StartTheme, string? PdfLayout = null,
    string TextSize = AppDisplay.Normal, string Spacing = AppDisplay.Comfortable, bool ReduceMotion = false,
    string PdfFileOrder = PdfFileOrders.NumberFirst, bool HasSealImage = false, bool HasSealPin = false, bool OpenPdfAfterSave = true,
    bool UsdNext = false, decimal UsdRate = 0, bool ProductLinksOnPdf = true, bool AttachCatalogues = true)
{
    /// <summary>A user (not an administrator) with both a seal picture and a seal PIN may seal PDFs.</summary>
    public bool CanSeal => Role != UserRole.Admin && HasSealImage && HasSealPin;
    /// <summary>The signed-in user makes quotations and proforma invoices (administrators do not).</summary>
    public bool MakesDocuments { get; init; } = Role != UserRole.Admin;
    /// <summary>"Administrator" or "Standard".</summary>
    public string RoleName => UserRoles.Name(Role);
}

/// <summary>The order in PDF file names, chosen by each user (My Settings).</summary>
public static class PdfFileOrders
{
    public const string NumberFirst = "NumberFirst", CustomerFirst = "CustomerFirst";
    public static bool IsValid(string? order) => order is NumberFirst or CustomerFirst;
}

/// <summary>An administrator sets a user's seal PIN (4 digits).</summary>
public sealed record SealPinRequest(string Pin);

/// <summary>The screens' theme, chosen by each user (PDFs are always white).</summary>
public static class AppTheme
{
    /// <summary>The plain looks every theme starts from. As choices they were removed in Round 32 (still read from older settings).</summary>
    public const string Light = "Light";
    public const string Dark = "Dark";
    /// <summary>Followed the Windows setting (removed in Round 32).</summary>
    public const string System = "System";
    // Colour themes (My Settings > Appearance).
    // (Lagoon, Cyberpunk and the colourful Glass were removed: users who had them get the start theme.
    //  More Maroon was removed in Round 19: its users get Glass (Maroon). Round 27 removed Sunset and Frost (→ the start
    //  theme) and Neon Night and Glass (Black) (→ Midnight Stars). Round 31 removed Maroon Mahal (→ Glass (Maroon)).
    //  Round 32 removed Default Light and Same as Windows (→ Glass (Blue)) and Default Dark (→ Midnight Stars).
    //  Round 35 removed Graphite (→ Glass (Blue)).)
    public const string GlassBlue = "GlassBlue", GlassGreen = "GlassGreen", GlassMaroon = "GlassMaroon",
        Parchment = "Parchment", ForestGold = "ForestGold", HeroLight = "HeroLight", Ocean = "Ocean", Midnight = "Midnight",
        Tally = "Tally";
    /// <summary>Removed themes (still read from older settings).</summary>
    public const string Sunset = "Sunset", Frost = "Frost", GlassBlack = "GlassBlack", NeonNight = "NeonNight", MaroonMahal = "MaroonMahal",
        Graphite = "Graphite";

    /// <summary>The theme of new users and of the sign-in screen.</summary>
    public const string StartTheme = GlassBlue;

    public static readonly string[] All = [GlassBlue, GlassGreen, GlassMaroon, Parchment, ForestGold, HeroLight, Ocean, Midnight, Tally];
    public static bool IsValid(string? theme) => theme is not null && All.Contains(theme);
    /// <summary>
    /// A saved theme that still exists: More Maroon and Maroon Mahal → Glass (Maroon); Default Dark, Neon Night and Glass
    /// (Black) → Midnight Stars; Default Light, Same as Windows, Graphite and any other removed theme → Glass (Blue).
    /// </summary>
    public static string Normalize(string? theme) => theme switch
    {
        "MoreMaroon" or MaroonMahal => GlassMaroon,
        Dark or NeonNight or GlassBlack => Midnight,
        _ when IsValid(theme) => theme!,
        _ => StartTheme,
    };
    /// <summary>Themes drawn on dark screens.</summary>
    public static bool IsDarkTheme(string? theme) => theme is Dark or ForestGold or Midnight;
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
    string? TextSize = null, string? Spacing = null, bool? ReduceMotion = null, string? PdfFileOrder = null, bool? OpenPdfAfterSave = null,
    bool? UsdNext = null, decimal? UsdRate = null, bool? ProductLinksOnPdf = null, bool? AttachCatalogues = null);

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

/// <summary>The names of the two roles: one Administrator, and Standard users.</summary>
public static class UserRoles
{
    public static string Name(UserRole role) => role == UserRole.Admin ? "Administrator" : "Standard";
}

/// <summary>
/// Round 36 "Who did what" (administrators): one thing someone did, from the audit log. <see cref="Kind"/> colours the chip:
/// ok (green), bad (red), warn (orange) or plain (grey).
/// </summary>
public sealed record ActivityDto(DateTime AtLocal, string User, string Chip, string Kind, string What);

/// <summary>Round 36: the evening summary of a day (text ready for WhatsApp or e-mail) and when it was last e-mailed.</summary>
public sealed record EveningSummaryDto(DateOnly Day, string Title, string Text, string? LastSent = null, string? Error = null);

/// <summary>Round 41 (DB-1): the dashboard's numbers for one period (today / week / month / fy). Amounts in rupees; approved
/// quotations only (not drafts, cancelled or proforma invoices).</summary>
public sealed record DashboardStatsDto(
    string Period,
    DateOnly From,
    DateOnly To,
    decimal QuotedValue,
    decimal PreviousValue,
    int Count,
    int PreviousCount,
    decimal AverageValue,
    decimal PreviousAverage,
    IReadOnlyList<decimal> ValueTrend,
    IReadOnlyList<int> CountTrend,
    IReadOnlyList<decimal> AverageTrend,
    DashboardBiggestDto? Biggest,
    IReadOnlyList<DashboardCustomerDto> CustomersByValue,
    IReadOnlyList<DashboardCustomerDto> CustomersByCount,
    IReadOnlyList<DashboardItemDto> Items,
    IReadOnlyList<DashboardTeamDto> Team,
    DashboardOutcomesDto? Outcomes,
    IReadOnlyList<DashboardDayDto> Activity,
    int Streak,
    string BusiestDay,
    string BusiestHours,
    string BestDay);

public sealed record DashboardBiggestDto(Guid Id, string Number, string Customer, decimal Value);
public sealed record DashboardCustomerDto(string Name, int? CustomerId, decimal Value, int Count);
public sealed record DashboardItemDto(string Name, int Times, decimal Value);
public sealed record DashboardTeamDto(int Rank, string UserName, string DisplayName, int Count, decimal Value, bool Me);
public sealed record DashboardOutcomesDto(int Won, int Lost, int Open, decimal WonValue, int FollowUpsDue);
public sealed record DashboardDayDto(DateOnly Date, int Count);
