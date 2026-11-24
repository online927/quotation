using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

/// <summary>Administrator diagnostics: connections, data, audit log and the server log.</summary>
public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly INavigator? _navigator;
    private readonly Action? _signOut;

    [ObservableProperty] private DiagnosticsDto? _info;
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _auditFilter = "";
    public ObservableCollection<AuditEntryDto> Audit { get; } = [];
    /// <summary>Requests slower than a second, newest first (client and server times, what the server was doing).</summary>
    public ObservableCollection<SlowRequestRow> SlowRequests { get; } = [];

    public DiagnosticsViewModel(AppSession session, INavigator? navigator = null, Action? signOut = null)
    {
        _session = session;
        _navigator = navigator;
        _signOut = signOut;
        _zipMonth = Months[0];
        _ = LoadAsync();
    }

    public string DatabaseSize => Info is null ? "" : $"{Info.DatabaseBytes / 1024.0 / 1024.0:0.0} MB";
    public string ClientInfo => $"{Environment.MachineName} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription} · client {typeof(App).Assembly.GetName().Version}";

    partial void OnInfoChanged(DiagnosticsDto? value) => OnPropertyChanged(nameof(DatabaseSize));

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        Info = await _session.Api.DiagnosticsAsync();
        await LoadAuditCoreAsync();
        SlowRequests.Clear();
        foreach (var s in await _session.Api.SlowRequestsAsync()) SlowRequests.Add(new SlowRequestRow(s));
        LogText = await _session.Api.ServerLogAsync(300);
        await LoadActivityUsersAsync();
        await LoadActivityCoreAsync();
    });

    private async Task LoadActivityCoreAsync()
    {
        var rows = await _session.Api.ActivityAsync(DateOnly.FromDateTime(ActivityDay ?? DateTime.Today), ActivityUser == AllUsers ? null : ActivityUser);
        Activity.Clear();
        foreach (var a in rows.Where(a => !ActivityImportantOnly || ImportantChips.Contains(a.Chip))) Activity.Add(new ActivityRow(a));
        OnPropertyChanged(nameof(NoActivity));
    }

    [RelayCommand]
    private Task BackupAsync() => RunAsync(async () =>
    {
        var path = await _session.Api.BackupNowAsync();
        InfoMessage = "Backup written on the server: " + path;
    });

    /// <summary>Where "Export everything" writes on this PC (Documents).</summary>
    public string ExportFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>One .tsq file with the database, PDFs, seal and logo: copy it to a USB drive or another PC.</summary>
    [RelayCommand]
    private Task ExportAsync() => RunAsync(async () =>
    {
        Directory.CreateDirectory(ExportFolder);
        var path = Path.Combine(ExportFolder, $"Quotation-Export-{DateTime.Now:yyyy-MM-dd-HHmm}.tsq");
        try
        {
            await using (var file = File.Create(path)) await _session.Api.ExportEverythingAsync(file);
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { }
            throw;
        }
        InfoMessage = $"Everything exported to {path} ({new FileInfo(path).Length / 1024.0 / 1024.0:0.0} MB). Keep it on a USB drive or another PC.";
    });

    /// <summary>Round 36 "Who did what": the chosen day (today at first) and user ("All users" = everyone).</summary>
    [ObservableProperty] private DateTime? _activityDay = DateTime.Today;
    [ObservableProperty] private string _activityUser = AllUsers;
    public const string AllUsers = "All users";
    public ObservableCollection<string> ActivityUsers { get; } = [AllUsers];
    public ObservableCollection<ActivityRow> Activity { get; } = [];
    public bool NoActivity => Activity.Count == 0;

    /// <summary>Round 36: "Approvals and cancels only" hides the many Saved / Created / PDF lines.</summary>
    [ObservableProperty] private bool _activityImportantOnly;
    partial void OnActivityImportantOnlyChanged(bool value) => _ = LoadActivityAsync();

    /// <summary>The chips kept by "Approvals and cancels only".</summary>
    public static readonly string[] ImportantChips = ["Approved", "Cancelled", "Deleted", "Restored", "Edited", "Won / Lost"];

    partial void OnActivityDayChanged(DateTime? value) => _ = LoadActivityAsync();
    partial void OnActivityUserChanged(string value) => _ = LoadActivityAsync();

    [RelayCommand]
    public Task LoadActivityAsync() => RunAsync(async () =>
    {
        await LoadActivityCoreAsync();
    });

    private async Task LoadActivityUsersAsync()
    {
        foreach (var u in (await _session.Api.UsersAsync()).Select(u => u.Username).Order(StringComparer.OrdinalIgnoreCase))
            if (!ActivityUsers.Contains(u)) ActivityUsers.Add(u);
    }

    /// <summary>Round 36: the evening summary of the chosen day (Who did what → Summary).</summary>
    [ObservableProperty] private string? _summaryText;
    public bool HasSummary => !string.IsNullOrEmpty(SummaryText);
    partial void OnSummaryTextChanged(string? value) => OnPropertyChanged(nameof(HasSummary));

    [RelayCommand]
    private Task ShowSummaryAsync() => RunAsync(async () =>
        SummaryText = (await _session.Api.EveningSummaryAsync(DateOnly.FromDateTime(ActivityDay ?? DateTime.Today))).Text);

    [RelayCommand]
    private void SummaryToWhatsApp()
    {
        if (SummaryText is { Length: > 0 } text && !_session.Documents.OpenWhatsAppText(text))
            ErrorMessage = "WhatsApp could not be opened on this PC.";
    }

    /// <summary>Round 36 "PDFs of a month": this month and the 23 before it, newest first.</summary>
    public IReadOnlyList<MonthChoice> Months { get; } = Enumerable.Range(0, 24)
        .Select(i => new MonthChoice(DateOnly.FromDateTime(DateTime.Today).AddDays(1 - DateTime.Today.Day).AddMonths(-i))).ToList();

    [ObservableProperty] private MonthChoice? _zipMonth;
    [ObservableProperty] private bool _zipQuotations = true;
    [ObservableProperty] private bool _zipProforma = true;

    /// <summary>Round 36: the month's approved PDFs as one ZIP, saved in Documents (for the accountant or filing).</summary>
    [RelayCommand]
    private Task DownloadMonthZipAsync() => RunAsync(async () =>
    {
        var month = ZipMonth ?? Months[0];
        if (!ZipQuotations && !ZipProforma)
        {
            ErrorMessage = "Tick Quotations or Proforma invoices.";
            return;
        }
        var (zip, name) = await _session.Api.MonthPdfsAsync(month.First.Year, month.First.Month, ZipQuotations, ZipProforma);
        Directory.CreateDirectory(ExportFolder);
        var path = Path.Combine(ExportFolder, name);
        await File.WriteAllBytesAsync(path, zip);
        LastZipPath = path;
        InfoMessage = $"{name} saved in {ExportFolder} ({zip.Length / 1024.0 / 1024.0:0.0} MB).";
    });

    /// <summary>Where the last ZIP was saved (tests).</summary>
    public string? LastZipPath { get; private set; }

    /// <summary>
    /// Replaces the server's quotations with a backup (.tsq from Export everything, or a .db backup).
    /// A copy of the current data is made first on the server ("before-restore"). Everyone signs in again afterwards.
    /// </summary>
    public Task RestoreAsync(string path) => RunAsync(async () =>
    {
        var name = Path.GetFileName(path);
        if (_navigator is not null && !await _navigator.ConfirmAsync("Restore from backup?",
                $"All quotations on the server will be replaced by the ones in {name}. " +
                "A copy of the current data is saved on the server first (before-restore). Everyone will have to sign in again.",
                "Restore", "Cancel"))
            return;
        RestoreSummaryDto result;
        await using (var file = File.OpenRead(path)) result = await _session.Api.RestoreFromBackupAsync(file);
        var extra = string.Join(", ", new[] { result.Pdfs > 0 ? $"{result.Pdfs} PDF(s)" : null, result.Seal ? "seal" : null, result.Logo ? "logo" : null }
            .Where(x => x is not null));
        _navigator?.Toast($"Restored {result.Quotations} quotation(s)" + (extra.Length > 0 ? ", " + extra : "") + ". Please sign in again.", 10);
        _signOut?.Invoke();
    });

    [RelayCommand]
    private Task LoadAuditAsync() => RunAsync(LoadAuditCoreAsync);

    private async Task LoadAuditCoreAsync()
    {
        Audit.Clear();
        var entries = await _session.Api.AuditAsync(take: 500);
        foreach (var a in entries.Where(a => AuditFilter.Length == 0
                     || a.Action.Contains(AuditFilter, StringComparison.OrdinalIgnoreCase)
                     || a.User.Contains(AuditFilter, StringComparison.OrdinalIgnoreCase)
                     || a.Details.Contains(AuditFilter, StringComparison.OrdinalIgnoreCase)))
        {
            Audit.Add(a);
        }
    }
}

/// <summary>One row of "Who did what": time, user, chip and what.</summary>
public sealed class ActivityRow(ActivityDto a)
{
    public string Time => a.AtLocal.ToString("HH:mm");
    public string User => a.User;
    public string Chip => a.Chip;
    public bool IsOk => a.Kind == "ok";
    public bool IsBad => a.Kind == "bad";
    public bool IsWarn => a.Kind == "warn";
    public string What => a.What;
}

/// <summary>A month in the "PDFs of a month" list ("November 2026").</summary>
public sealed record MonthChoice(DateOnly First)
{
    public override string ToString() => First.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One slow request as listed in Diagnostics, with the likely cause.</summary>
public sealed class SlowRequestRow(SlowRequestDto s)
{
    public string When => s.WhenUtc.ToLocalTime().ToString("dd-MMM HH:mm:ss");
    public string Who => string.Join(" / ", new[] { s.Machine, s.User }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string What => s.What;
    public string Total => Seconds(s.TotalMs);
    public string Server => Seconds(s.ServerMs);
    public string Busy => string.IsNullOrWhiteSpace(s.Busy) ? "—" : s.Busy;

    /// <summary>Total slow but the server quick: the network or finding the server PC; the server slow: the server PC.</summary>
    public string Cause => s switch
    {
        { ServerMs: >= 1000 } => string.IsNullOrWhiteSpace(s.Busy) ? "Server PC slow" : "Server busy: " + s.Busy,
        { TotalMs: not null, ServerMs: null } => "No answer (network / server off)",
        { TotalMs: not null, ServerMs: < 1000 } => "Network or PC name",
        _ => "",
    };

    private static string Seconds(double? ms) => ms is null ? "—" : $"{ms.Value / 1000:0.00} s";
}
