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

    partial void OnStatusChanged(SystemStatusDto? value) => OnPropertyChanged(nameof(LastSyncText));
    public string UserDisplay { get; }
    public string UserRole { get; }
    public string UserInitials { get; }

    public ShellViewModel(AppSession session, Action onLogout)
    {
        _session = session;
        _onLogout = onLogout;
        var user = session.Api.CurrentUser;
        UserDisplay = user is null ? "" : user.DisplayName;
        UserRole = user?.Role.ToString() ?? "";
        UserInitials = user is null ? "" : string.Concat(user.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

        NavItems.Add(new NavItem("dashboard", "Dashboard", "Ctrl+D", () => new DashboardViewModel(_session, Navigate)));
        NavItems.Add(new NavItem("new", "New Quotation", "Ctrl+N", () => new QuotationEditorViewModel(_session, this), "Create"));
        NavItems.Add(new NavItem("newproforma", "New Proforma Invoice", "Ctrl+Shift+N",
            () => new QuotationEditorViewModel(_session, this, docType: DocumentType.ProformaInvoice)));
        NavItems.Add(new NavItem("inbox", "AI Inbox", "Ctrl+I", () => new AiInboxViewModel(_session, this)));
        NavItems.Add(new NavItem("drafts", "Draft Quotations", "", () => new QuotationListViewModel(_session, this, QuotationListPreset.Drafts), "Documents"));
        NavItems.Add(new NavItem("today", "Today's Quotations", "", () => new QuotationListViewModel(_session, this, QuotationListPreset.Today)));
        NavItems.Add(new NavItem("history", "Quotation History", "Ctrl+H", () => new QuotationListViewModel(_session, this, QuotationListPreset.History)));
        NavItems.Add(new NavItem("proforma", "Proforma Invoices", "", () => new QuotationListViewModel(_session, this, QuotationListPreset.Proforma)));
        if (session.IsAdmin) NavItems.Add(new NavItem("deleted", "Deleted", "", () => new DeletedQuotationsViewModel(_session, this)));
        NavItems.Add(new NavItem("customers", "Customers", "", () => new CustomersViewModel(_session), "Tally masters"));
        NavItems.Add(new NavItem("products", "Products", "", () => new ProductsViewModel(_session)));
        NavItems.Add(new NavItem("tally", "Tally Connection", "", () => new TallyViewModel(_session), "System"));
        NavItems.Add(new NavItem("gmail", "Gmail Connection", "", () => new GmailViewModel(_session, this)));
        NavItems.Add(new NavItem("mysettings", "My Settings", "", () => new MySettingsViewModel(_session)));
        NavItems.Add(new NavItem("settings", "Settings", "", () => new SettingsViewModel(_session)));
        if (session.IsAdmin) NavItems.Add(new NavItem("diagnostics", "Diagnostics", "", () => new DiagnosticsViewModel(_session)));

        Navigate("dashboard");
        _ = PollStatusAsync(_cts.Token);
    }

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

    [RelayCommand]
    public void Navigate(string key)
    {
        var item = NavItems.FirstOrDefault(n => n.Key == key);
        if (item is null) return;
        _ = OpenAsync(item.Key, item.Factory);
    }

    public void OpenQuotation(Guid id) => _ = OpenAsync(null, () => new QuotationEditorViewModel(_session, this, id));

    public void NewQuotation() => Navigate("new");

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
            LeaveChoice.DontSave => Leave.Discard,
            _ => Leave.Cancel,
        };
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

    private async Task LoadStatusAsync()
    {
        try
        {
            Status = await _session.Api.StatusAsync(_cts.Token);
            ServerReachable = true;
            ServerState = $"Server: {_session.Config.ServerUrl}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ServerReachable = false;
            ServerState = "Quotation Server unreachable";
        }
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested) _cts.Cancel();
        PendingPrompt?.Choose(LeaveChoice.Cancel);
        PendingDialog?.Choose(false);
    }
}
