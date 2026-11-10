using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class NavItem(string key, string title, string shortcut, Func<ViewModelBase> factory, string section = "") : ObservableObject
{
    /// <summary>Line icon (24×24 outline) drawn in the app, so it looks the same on every PC.</summary>
    public Avalonia.Media.Geometry? Icon { get; } = NavIcons.For(key);

    /// <summary>Group heading shown above this item in the sidebar (empty: same group as the item before).</summary>
    public string Section { get; } = section;
    public bool HasSection => Section.Length > 0;
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Shortcut { get; } = shortcut;
    public Func<ViewModelBase> Factory { get; } = factory;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Outline icons of the sidebar (24×24, drawn as lines).</summary>
public static class NavIcons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["dashboard"] = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z",
        ["new"] = "M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19 M12.5,11 V18 M9,14.5 H16",
        ["newproforma"] = "M6,3 H18 V21 L15,19 L12,21 L9,19 L6,21 Z M12,8 V14 M9,11 H15",
        ["inbox"] = "M12,3 L13.8,9.2 L20,11 L13.8,12.8 L12,19 L10.2,12.8 L4,11 L10.2,9.2 Z",
        ["drafts"] = "M4,20 V16 L15,5 L19,9 L8,20 Z M13,7 L17,11",
        ["today"] = "M4,6 H20 V20 H4 Z M4,10 H20 M8,3 V7 M16,3 V7",
        ["history"] = "M12,3 A9,9 0 1 1 11.99,3 Z M12,7 V12 L15,14",
        ["proforma"] = "M6,3 H18 V21 L15,19 L12,21 L9,19 L6,21 Z M9,8 H15 M9,12 H15 M9,16 H13",
        ["deleted"] = "M5,7 H19 M10,7 V4 H14 V7 M7,7 L8,20 H16 L17,7 M10,11 V16 M14,11 V16",
        ["customers"] = "M9,4 A3.5,3.5 0 1 1 8.99,4 Z M3,20 C3,16 6,14 9,14 C12,14 15,16 15,20 M16,4.5 A3,3 0 0 1 16,10.5 M17,14 C19.5,14.5 21,16.5 21,20",
        ["products"] = "M12,3 L20,7.5 V16.5 L12,21 L4,16.5 V7.5 Z M4,7.5 L12,12 L20,7.5 M12,12 V21",
        ["tally"] = "M9,15 L15,9 M10,6 L12,4 A4,4 0 0 1 18,10 L16,12 M14,18 L12,20 A4,4 0 0 1 6,14 L8,12",
        ["gmail"] = "M3,6 H21 V18 H3 Z M3,6 L12,13 L21,6",
        ["mysettings"] = "M12,4 A4,4 0 1 1 11.99,4 Z M4,21 C4,16.5 7.5,14.5 12,14.5 C16.5,14.5 20,16.5 20,21",
        ["settings"] = "M4,7 H20 M4,12 H20 M4,17 H20 M9,5 V9 M15,10 V14 M7,15 V19",
        ["diagnostics"] = "M3,12 H7 L10,5 L14,19 L17,12 H21",
    };

    public static Avalonia.Media.Geometry? For(string key) =>
        Paths.TryGetValue(key, out var data) ? Avalonia.Media.StreamGeometry.Parse(data) : null;

    // For pages that show the same icons (dashboard tiles).
    public static Avalonia.Media.Geometry? Today => For("today");
    public static Avalonia.Media.Geometry? Drafts => For("drafts");
    public static Avalonia.Media.Geometry? History => For("history");
    public static Avalonia.Media.Geometry? Inbox => For("inbox");
}

/// <summary>A step in the breadcrumb trail; <see cref="StepsBack"/> is 0 for the page shown now.</summary>
public sealed record Crumb(string Title, int StepsBack)
{
    public bool IsCurrent => StepsBack == 0;
}

/// <summary>Main application frame: navigation with history, current page and connection status bar.</summary>
public sealed partial class ShellViewModel : ViewModelBase, IDisposable, INavigator
{
    private readonly AppSession _session;
    private readonly Action _onLogout;
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private SystemStatusDto? _status;
    [ObservableProperty] private string _serverState = "Connecting…";
    [ObservableProperty] private bool _serverReachable = true;
    [ObservableProperty] private string _globalSearch = "";

    public ObservableCollection<NavItem> NavItems { get; } = [];
    public string LastSyncText => "Last sync: " + (Formatting.LocalDateTime(Status?.LastSuccessfulSyncUtc) ?? "never");

    /// <summary>Installed client version (e.g. "App v0.9.18"), so a user can confirm an update reached this PC.</summary>
    public static string AppVersionText
    {
        get
        {
            var v = typeof(App).Assembly.GetName().Version;
            return v is null ? "" : $"App v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    partial void OnStatusChanged(SystemStatusDto? value)
    {
        OnPropertyChanged(nameof(LastSyncText));
        NotifyHealth();
    }

    // ================================================================= status dot (top bar) and warning strip

    /// <summary>Green: all well. Orange: Tally busy, data getting old or slow answers. Red: Tally or the server cannot be reached.</summary>
    public bool HealthBad => !ServerReachable || Status is { TallyConnected: false };
    public bool HealthWarn => !HealthBad && (Status is { DataIsStale: true } or { TallyBusy: true } || SpeedSlow);
    public bool HealthOk => !HealthBad && !HealthWarn;

    /// <summary>Shown when pointing at the status dot.</summary>
    public string HealthDetails => string.Join(Environment.NewLine, new[]
    {
        !ServerReachable ? "Quotation Server: no answer" : $"Quotation Server: answers in {(ServerMs is { } ms ? ms + " ms" : "…")}",
        Status is null ? "Tally: …" : Status.TallyConnected ? $"Tally: connected ({Status.TallyCompany})" : "Tally: disconnected",
        LastSyncText,
    }.Where(s => s.Length > 0));

    /// <summary>The word beside the status dot.</summary>
    public string HealthLabel => HealthBad ? "Disconnected" : "Connected";

    /// <summary>One line under the top bar, only while something is wrong; null when all is well.</summary>
    public string? WarningText =>
        !ServerReachable ? "Quotation Server unreachable — check the server PC and the network. Unsaved work is kept on this PC."
        : Status is { TallyConnected: false } ? $"Tally disconnected — using the items and customers synced {Formatting.LocalDateTime(Status.LastSuccessfulSyncUtc) ?? "earlier"}."
        : Status is { DataIsStale: true } ? $"Tally data is old — {LastSyncText.ToLowerInvariant()}. Open Tally and use Sync changes."
        : Status is { TallyBusy: true } ? "Tally is busy — background reading paused until it answers quickly again."
        : null;

    /// <summary>Which problem the strip is about (closing it hides it until a different problem, or the same one again later).</summary>
    private string? WarningKey =>
        !ServerReachable ? "server" : Status is { TallyConnected: false } ? "tally" : Status is { DataIsStale: true } ? "stale" : Status is { TallyBusy: true } ? "busy" : null;

    private string? _closedWarning;

    /// <summary>The strip as shown: the warning, unless the user closed it for this problem.</summary>
    public string? VisibleWarning => WarningKey is { } key && key != _closedWarning ? WarningText : null;

    [RelayCommand]
    private void CloseWarning()
    {
        _closedWarning = WarningKey;
        OnPropertyChanged(nameof(VisibleWarning));
    }

    private void NotifyHealth()
    {
        if (WarningKey is null) _closedWarning = null; // fixed: the next problem shows again
        OnPropertyChanged(nameof(VisibleWarning));
        OnPropertyChanged(nameof(HealthBad));
        OnPropertyChanged(nameof(HealthWarn));
        OnPropertyChanged(nameof(HealthOk));
        OnPropertyChanged(nameof(HealthDetails));
        OnPropertyChanged(nameof(HealthLabel));
        OnPropertyChanged(nameof(WarningText));
    }
    public string UserDisplay { get; }
    public string UserRole { get; }
    public string UserInitials { get; }

    private static readonly Avalonia.Media.Geometry MoonIcon = Avalonia.Media.Geometry.Parse("M20,14.5 A8.5,8.5 0 1 1 9.5,4 A6.5,6.5 0 0 0 20,14.5 Z");
    private static readonly Avalonia.Media.Geometry SunIcon = Avalonia.Media.Geometry.Parse(
        "M12,7.5 A4.5,4.5 0 1 1 11.99,7.5 Z M12,1.5 V4 M12,20 V22.5 M4.6,4.6 L6.3,6.3 M17.7,17.7 L19.4,19.4 M1.5,12 H4 M20,12 H22.5 M4.6,19.4 L6.3,17.7 M17.7,6.3 L19.4,4.6");

    /// <summary>The top-bar button shows the moon in light mode (switch to dark) and the sun in dark mode.</summary>
    public Avalonia.Media.Geometry ThemeIcon => ThemeService.IsDark ? SunIcon : MoonIcon;
    public string ThemeTip => ThemeService.IsDark ? "Light mode" : "Dark mode";

    /// <summary>Switches between light and dark for this user (saved with their login, so it follows them to any PC).</summary>
    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        var theme = ThemeService.IsDark ? AppTheme.Light : AppTheme.Dark;
        try
        {
            await _session.SetThemeAsync(theme);
        }
        catch (Quotation.ApiClient.ApiException)
        {
            // The screens have switched; the choice is saved again the next time.
        }
    }

    /// <summary>The screens changed light/dark (this button, My Settings, or Windows when following Windows).</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ThemeIcon));
        OnPropertyChanged(nameof(ThemeTip));
    }

    public ShellViewModel(AppSession session, Action onLogout)
    {
        _session = session;
        _sidebarCollapsed = session.Config.SidebarCollapsed;
        _onLogout = onLogout;
        var user = session.Api.CurrentUser;
        UserDisplay = user is null ? "" : user.DisplayName;
        UserRole = user is null ? "" : user.Role == Core.Domain.UserRole.Admin ? "Admin" : "Standard";
        UserInitials = user is null ? "" : string.Concat(user.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        if (user is not null) session.ApplyAppearance(user);
        if (Avalonia.Application.Current is { } app) app.ActualThemeVariantChanged += OnThemeChanged;

        BuildMenu();
        session.SettingsChanged += OnSettingsChanged;
        session.BackgroundPictureChanged += OnBackgroundPictureChanged;
        LoadBackgroundPicture();
        _ = ReadSwitchesAsync();

        Navigate("dashboard");
        _ = OfferRecoveryAsync();
        _ = PollStatusAsync(_cts.Token);
        _ = CheckForUpdateAsync();
    }

    // ================================================================= background picture (this PC)

    /// <summary>The picture chosen in My Settings, shown behind the pages (decoded to screen width to keep memory low).</summary>
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _backgroundPicture;
    public bool HasBackgroundPicture => BackgroundPicture is not null;
    partial void OnBackgroundPictureChanged(Avalonia.Media.Imaging.Bitmap? value) => OnPropertyChanged(nameof(HasBackgroundPicture));

    private void LoadBackgroundPicture()
    {
        var old = BackgroundPicture;
        Avalonia.Media.Imaging.Bitmap? picture = null;
        if (_session.BackgroundPicturePath is { } path)
        {
            try
            {
                using var file = File.OpenRead(path);
                picture = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(file, 1920);
            }
            catch (Exception)
            {
                picture = null; // a damaged file: no picture
            }
        }
        BackgroundPicture = picture;
        old?.Dispose();
    }

    private void OnBackgroundPictureChanged(object? sender, EventArgs e)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) LoadBackgroundPicture();
        else Avalonia.Threading.Dispatcher.UIThread.Post(LoadBackgroundPicture);
    }

    // ================================================================= the side menu

    /// <summary>
    /// The side menu. The administrator makes no quotations (no "New Quotation" / "New Proforma Invoice"); proforma
    /// invoices show only while they are switched on in Settings → Quotation.
    /// </summary>
    private void BuildMenu()
    {
        var makes = _session.MakesDocuments;
        var proforma = _session.ProformaEnabled;
        var items = new List<NavItem> { new("dashboard", "Dashboard", "", () => new DashboardViewModel(_session, Navigate)) };
        if (makes)
        {
            items.Add(new NavItem("new", "New Quotation", "", () => new QuotationEditorViewModel(_session, this), "Create"));
            if (proforma)
                items.Add(new NavItem("newproforma", "New Proforma Invoice", "",
                    () => new QuotationEditorViewModel(_session, this, docType: DocumentType.ProformaInvoice)));
        }
        items.Add(new NavItem("inbox", "AI Inbox", "", () => new AiInboxViewModel(_session, this), makes ? "" : "Requests"));
        items.Add(new NavItem("drafts", "Draft Quotations", "", () => new QuotationListViewModel(_session, this, QuotationListPreset.Drafts), "Documents"));
        items.Add(new NavItem("today", "Today's Quotations", "", () => new QuotationListViewModel(_session, this, QuotationListPreset.Today)));
        items.Add(new NavItem("history", "Quotation History", "", () => new QuotationListViewModel(_session, this, QuotationListPreset.History)));
        if (proforma) items.Add(new NavItem("proforma", "Proforma Invoices", "", () => new QuotationListViewModel(_session, this, QuotationListPreset.Proforma)));
        if (_session.IsAdmin) items.Add(new NavItem("deleted", "Deleted", "", () => new DeletedQuotationsViewModel(_session, this)));
        items.Add(new NavItem("customers", "Customers", "", () => new CustomersViewModel(_session), "Tally masters"));
        items.Add(new NavItem("products", "Products", "", () => new ProductsViewModel(_session)));
        items.Add(new NavItem("tally", "Tally Connection", "", () => new TallyViewModel(_session), "System"));
        items.Add(new NavItem("gmail", "Gmail Connection", "", () => new GmailViewModel(_session, this)));
        items.Add(new NavItem("mysettings", "My Settings", "", () => new MySettingsViewModel(_session)));
        items.Add(new NavItem("settings", "Settings", "", () => new SettingsViewModel(_session)));
        if (_session.IsAdmin) items.Add(new NavItem("diagnostics", "Diagnostics", "", () => new DiagnosticsViewModel(_session, this, () => _onLogout())));

        if (items.Select(n => n.Key).SequenceEqual(NavItems.Select(n => n.Key))) return;
        NavItems.Clear();
        foreach (var n in items)
        {
            n.IsSelected = n.Key == _current?.NavKey;
            NavItems.Add(n);
        }
    }

    /// <summary>Reads Settings once after signing in, for the proforma switch.</summary>
    private async Task ReadSwitchesAsync()
    {
        try
        {
            await _session.GetSettingsAsync();
        }
        catch (Exception)
        {
            // The menu stays without proforma invoices until settings can be read.
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) BuildMenu();
        else Avalonia.Threading.Dispatcher.UIThread.Post(BuildMenu);
    }

    // ================================================================= automatic client updates (A2)

    /// <summary>Closes the program (replaced in tests).</summary>
    public static Action ExitApp { get; set; } = () =>
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    /// <summary>This program's version (e.g. 0.9.52).</summary>
    public static Version CurrentVersion { get; set; } = typeof(App).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>
    /// The Quotation Server carries the newest client installer. When it is newer than this program:
    /// "Update now" / "Later" ("Later" is not offered when an administrator set "Clients must update").
    /// Update now downloads it, keeps unsaved work for next time, installs silently and opens the program again.
    /// </summary>
    public async Task CheckForUpdateAsync()
    {
        ClientUpdateDto update;
        try { update = await _session.Api.ClientUpdateAsync(_cts.Token); }
        catch { return; } // older server or no connection: try again next sign-in
        if (!Version.TryParse(update.Version, out var offered) || Normal(offered) <= Normal(CurrentVersion)) return;
        try
        {
            while (PendingPrompt is not null || PendingDialog is not null) await Task.Delay(500, _cts.Token);
        }
        catch (OperationCanceledException) { return; }
        var size = update.Size >= 1024 * 1024 ? $" ({update.Size / 1024.0 / 1024.0:0} MB)" : "";
        var now = await ConfirmAsync($"New version {Normal(offered)} is available",
            update.Required
                ? $"An administrator asks everyone to update now{size}. Unsaved work is kept and opens again after the update."
                : $"You have {Normal(CurrentVersion)}. Update now{size}? Unsaved work is kept and opens again after the update.",
            "Update now", update.Required ? "Close app" : "Later");
        if (!now)
        {
            if (update.Required) { KeepWorkForNextTime(); ExitApp(); }
            return;
        }
        Toast("Downloading the update…", 30);
        var path = Path.Combine(Path.GetTempPath(), $"TSQuotation-Setup-{Normal(offered)}.exe");
        try
        {
            await using (var file = File.Create(path)) await _session.Api.DownloadClientAsync(file, _cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Toast("The update could not be downloaded: " + ex.Message, 10);
            return;
        }
        KeepWorkForNextTime();
        if (!_session.Documents.RunInstaller(path, _session.Config.ServerUrl))
        {
            Toast("The update could not be started. Ask the administrator to install it by hand.", 10);
            return;
        }
        ExitApp();
    }

    private static Version Normal(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    // ================================================================= navigation history
    // Works like a web browser: every page opened is remembered, Back (Alt+←, Esc, mouse back button) returns to the
    // previous page exactly as it was left (search text, filters, selection), Forward (Alt+→) undoes a Back.

    private sealed record PageEntry(string? NavKey, ViewModelBase Page);
    private enum Leave { Keep, Discard, Cancel }
    private const int MaxHistory = 30;
    private readonly List<PageEntry> _back = [];
    private readonly List<PageEntry> _forward = [];
    private PageEntry? _current;

    /// <summary>Shown while asking Save / Don't save / Cancel before leaving a page with unsaved changes.</summary>
    [ObservableProperty] private LeavePrompt? _pendingPrompt;

    public ObservableCollection<Crumb> Breadcrumbs { get; } = [];
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public string BackToolTip => CanGoBack ? $"Back to {TitleOf(_back[^1])} (Alt+← or Esc)" : "Back";
    public string ForwardToolTip => CanGoForward ? $"Forward to {TitleOf(_forward[^1])} (Alt+→)" : "Forward";

    // ================================================================= folded menu (U1)

    [ObservableProperty] private bool _sidebarCollapsed;
    public string SidebarToggleTip => SidebarCollapsed ? "Show the menu" : "Hide the menu (icons only)";
    public string SidebarToggleText => SidebarCollapsed ? "Show menu" : "Hide menu";
    public string SidebarToggleIcon => SidebarCollapsed ? "M9,5 L16,12 L9,19 M4,5 L4,19" : "M15,5 L8,12 L15,19 M20,5 L20,19";
    partial void OnSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarToggleTip));
        OnPropertyChanged(nameof(SidebarToggleIcon));
        OnPropertyChanged(nameof(SidebarToggleText));
        if (_session.Config.SidebarCollapsed == value) return;
        _session.Config.SidebarCollapsed = value;
        try
        {
            _session.Config.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only this PC's menu width depends on it.
        }
    }

    [RelayCommand]
    private void ToggleSidebar() => SidebarCollapsed = !SidebarCollapsed;

    [RelayCommand]
    public void Navigate(string key)
    {
        if (key == "followup")
        {
            _ = OpenAsync(null, () => new QuotationListViewModel(_session, this, QuotationListPreset.FollowUp));
            return;
        }
        var item = NavItems.FirstOrDefault(n => n.Key == key);
        if (item is null) return;
        _ = OpenAsync(item.Key, item.Factory);
    }

    public void OpenQuotation(Guid id) => _ = OpenAsync(null, () => new QuotationEditorViewModel(_session, this, id));

    public void NewQuotation()
    {
        if (_session.MakesDocuments) Navigate("new");
    }

    /// <summary>Back one page; with no history, Back leads to the dashboard (like Esc to Gateway in Tally).</summary>
    [RelayCommand]
    public void GoBack()
    {
        if (CanGoBack) _ = StepBackAsync(1);
        else if (_current?.NavKey != "dashboard") Navigate("dashboard");
    }

    [RelayCommand]
    public void GoForward() => _ = StepForwardAsync();

    /// <summary>Clicking a breadcrumb goes back to that page.</summary>
    [RelayCommand]
    private void GoToCrumb(Crumb? crumb)
    {
        if (crumb is { StepsBack: > 0 }) _ = StepBackAsync(crumb.StepsBack);
    }

    [RelayCommand]
    private void AnswerPrompt(LeaveChoice choice) => PendingPrompt?.Choose(choice);

    // ================================================================= questions over the window

    /// <summary>The Yes/No question or input (e.g. seal PIN) shown over the window.</summary>
    [ObservableProperty] private ShellDialog? _pendingDialog;

    public async Task<bool> ConfirmAsync(string title, string message, string yes = "Yes", string no = "No") =>
        await ShowDialogAsync(new ShellDialog(title, message, yes, no)) is not null;

    public async Task<string?> AskAsync(string title, string message, string inputLabel, bool secret = false, string ok = "OK") =>
        await ShowDialogAsync(new ShellDialog(title, message, ok, "Cancel", inputLabel, secret));

    private async Task<string?> ShowDialogAsync(ShellDialog dialog)
    {
        PendingDialog?.Choose(false);
        PendingDialog = dialog;
        try { return await dialog.Answer ? dialog.Input : null; }
        finally { if (PendingDialog == dialog) PendingDialog = null; }
    }

    [RelayCommand]
    private void AnswerDialog(bool primary) => PendingDialog?.Choose(primary);

    /// <summary>Top-right menu: change my own password.</summary>
    [RelayCommand]
    private void ChangeMyPassword() => _ = OpenAsync(null, () => new ChangePasswordViewModel(_session, forced: false, onDone: GoBack));

    private async Task OpenAsync(string? navKey, Func<ViewModelBase> factory)
    {
        if (PendingPrompt is not null) return;
        var leave = await LeaveCurrentAsync();
        if (leave == Leave.Cancel) return;
        if (_current is { } current)
        {
            if (leave == Leave.Keep) _back.Add(current);
            else Release(current);
        }
        while (_back.Count > MaxHistory)
        {
            Release(_back[0]);
            _back.RemoveAt(0);
        }
        foreach (var e in _forward) Release(e);
        _forward.Clear();
        Show(new PageEntry(navKey, factory()), returned: false);
    }

    private async Task StepBackAsync(int steps)
    {
        if (PendingPrompt is not null || steps < 1 || _back.Count < steps || _current is null) return;
        var leave = await LeaveCurrentAsync();
        if (leave == Leave.Cancel) return;
        if (leave == Leave.Keep) _forward.Add(_current);
        else Release(_current);
        for (var i = 1; i < steps; i++)
        {
            _forward.Add(_back[^1]);
            _back.RemoveAt(_back.Count - 1);
        }
        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Show(target, returned: true);
    }

    private async Task StepForwardAsync()
    {
        if (PendingPrompt is not null || _forward.Count == 0 || _current is null) return;
        var leave = await LeaveCurrentAsync();
        if (leave == Leave.Cancel) return;
        if (leave == Leave.Keep) _back.Add(_current);
        else Release(_current);
        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        Show(target, returned: true);
    }

    /// <summary>Asks before leaving unsaved work. Completes synchronously when there is nothing to ask.</summary>
    private async Task<Leave> LeaveCurrentAsync()
    {
        if (_current?.Page is not IUnsavedChangesPage { HasUnsavedChanges: true } page) return Leave.Keep;
        var prompt = new LeavePrompt(TitleOf(_current));
        PendingPrompt = prompt;
        LeaveChoice choice;
        try { choice = await prompt.Answer; }
        finally { PendingPrompt = null; }
        return choice switch
        {
            LeaveChoice.Save => await page.SaveChangesAsync() ? Leave.Keep : Leave.Cancel,
            LeaveChoice.DontSave => Discard(page),
            _ => Leave.Cancel,
        };
    }

    private static Leave Discard(IUnsavedChangesPage page)
    {
        page.DiscardChanges();
        return Leave.Discard;
    }

    // ================================================================= messages in the corner

    public ObservableCollection<ToastItem> Toasts { get; } = [];

    /// <summary>Shows a short message in the bottom-right corner for a few seconds.</summary>
    public void Toast(string message, int seconds = 5)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Show(new ToastItem(message), seconds);
    }

    public void Toast(string message, string actionText, Action action, int seconds = 8) =>
        Show(new ToastItem(message, actionText, action), seconds);

    private void Show(ToastItem item, int seconds)
    {
        Toasts.Add(item);
        while (Toasts.Count > 3) Toasts.RemoveAt(0);
        _ = RemoveLaterAsync(item, seconds);
    }

    /// <summary>The button on a corner message (Undo …): runs it and closes the message.</summary>
    [RelayCommand]
    private void ToastAction(ToastItem? item)
    {
        if (item is null) return;
        Toasts.Remove(item);
        item.Action?.Invoke();
    }

    private async Task RemoveLaterAsync(ToastItem item, int seconds)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(seconds), _cts.Token); } catch (OperationCanceledException) { }
        item.IsLeaving = true; // fades out
        try { await Task.Delay(TimeSpan.FromMilliseconds(220), _cts.Token); } catch (OperationCanceledException) { }
        Toasts.Remove(item);
    }

    // ================================================================= unsaved work after a crash

    /// <summary>
    /// After a power cut, a crash or closing without saving: the quotation that was being worked on (kept on this PC) opens by
    /// itself, still unsaved, with a message in the corner. No question is asked and no number is used until it is saved.
    /// </summary>
    private Task OfferRecoveryAsync()
    {
        if (!_session.MakesDocuments) return Task.CompletedTask;
        if (_session.Api.CurrentUser?.Username is not { } user || RecoveryStore.For(_session.Config).Load(user) is not { } snap) return Task.CompletedTask;
        var q = snap.Quotation;
        var what = string.IsNullOrWhiteSpace(q.Buyer.Name) ? (q.Number ?? "your quotation") : q.Buyer.Name;
        _ = OpenAsync(null, () => new QuotationEditorViewModel(_session, this, snap));
        Toast($"Last quotation restored: {what}, {q.Lines.Count} line{(q.Lines.Count == 1 ? "" : "s")}.", 8);
        return Task.CompletedTask;
    }

    private void Show(PageEntry entry, bool returned)
    {
        _current = entry;
        foreach (var n in NavItems) n.IsSelected = n.Key == entry.NavKey;
        CurrentPage = entry.Page;
        if (returned && entry.Page is IReturnablePage page) page.OnReturnedTo();
        UpdateHistoryState();
    }

    partial void OnCurrentPageChanged(ViewModelBase? oldValue, ViewModelBase? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnPagePropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnPagePropertyChanged;
    }

    private void OnPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITitledPage.Title)) UpdateHistoryState();
        // Page messages ("Saved …", "PDF saved to …") appear in the corner; errors stay on the page.
        // (AI Inbox, Tally and Gmail show progress messages on their own page instead.)
        if (e.PropertyName == nameof(ViewModelBase.InfoMessage) && sender is ViewModelBase { InfoMessage: { Length: > 0 } info }
            && sender is not (AiInboxViewModel or TallyViewModel or GmailViewModel)) Toast(info);
    }

    private void UpdateHistoryState()
    {
        Breadcrumbs.Clear();
        var shown = Math.Min(_back.Count, 3);
        for (var i = _back.Count - shown; i < _back.Count; i++) Breadcrumbs.Add(new Crumb(TitleOf(_back[i]), _back.Count - i));
        if (_current is not null) Breadcrumbs.Add(new Crumb(TitleOf(_current), 0));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(BackToolTip));
        OnPropertyChanged(nameof(ForwardToolTip));
    }

    private string TitleOf(PageEntry entry) =>
        entry.Page is ITitledPage { Title.Length: > 0 } titled ? titled.Title
            : NavItems.FirstOrDefault(n => n.Key == entry.NavKey)?.Title ?? "";

    private static void Release(PageEntry entry)
    {
        if (entry.Page is IDisposable d) d.Dispose();
    }

    /// <summary>Global search: quotation number, customer, GSTIN, product, reference or date.</summary>
    [RelayCommand]
    private void Search()
    {
        if (string.IsNullOrWhiteSpace(GlobalSearch)) return;
        var query = GlobalSearch.Trim();
        _ = OpenAsync("history", () => new QuotationListViewModel(_session, this, QuotationListPreset.History, query));
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            if (PendingPrompt is not null || PendingDialog is not null) return;
            if (!await ConfirmAsync("Leaving already, bro?", "", "Yes", "No")) return;
            KeepWorkForNextTime(); // unsaved work opens again at the next sign-in
        }
        Dispose();
        await _session.Api.LogoutAsync();
        _onLogout();
    }

    [RelayCommand]
    private Task RefreshStatusAsync() => LoadStatusAsync();

    private async Task PollStatusAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await LoadStatusAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>The speed check: how long the server took to answer the last status check (every 30 seconds).</summary>
    [ObservableProperty] private int? _serverMs;

    public string SpeedText => !ServerReachable ? "⚡ no answer" : ServerMs is { } ms ? $"⚡ {ms} ms" : "";
    public bool SpeedOk => ServerReachable && ServerMs is < 300;
    public bool SpeedSlow => ServerReachable && ServerMs is >= 1000;
    public bool SpeedBad => !ServerReachable;

    partial void OnServerMsChanged(int? value) => NotifySpeed();
    partial void OnServerReachableChanged(bool value) => NotifySpeed();

    private void NotifySpeed()
    {
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(SpeedOk));
        OnPropertyChanged(nameof(SpeedSlow));
        OnPropertyChanged(nameof(SpeedBad));
        NotifyHealth();
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            Status = await _session.Api.StatusAsync(_cts.Token);
            ServerReachable = true;
            ServerState = ""; // the server address is in My Settings → Connection
            ServerMs = _session.Api.LastStatusTime is { } t ? (int)Math.Round(t.TotalMilliseconds) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ServerReachable = false;
            ServerState = "Quotation Server unreachable";
            ServerMs = null;
        }
    }

    /// <summary>
    /// Closing the window: "Bro, are you trying to close?" (No / Yes). Unsaved work is not asked about: it is kept
    /// on this PC and opens by itself next time. True when the app may close.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (_cts.IsCancellationRequested) return true;
        if (PendingPrompt is not null || PendingDialog is not null) return false; // already asking something
        if (!await ConfirmAsync("Bro, are you trying to close?", "", "Yes", "No")) return false;
        KeepWorkForNextTime();
        return true;
    }

    /// <summary>Windows is shutting down: no questions; unsaved work is kept on the PC for next time.</summary>
    public void KeepWorkForNextTime()
    {
        if (_current?.Page is QuotationEditorViewModel editor) editor.KeepRecoveryCopy();
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested) _cts.Cancel();
        _session.SettingsChanged -= OnSettingsChanged;
        _session.BackgroundPictureChanged -= OnBackgroundPictureChanged;
        if (Avalonia.Application.Current is { } app) app.ActualThemeVariantChanged -= OnThemeChanged;
        PendingPrompt?.Choose(LeaveChoice.Cancel);
        PendingDialog?.Choose(false);
    }
}
