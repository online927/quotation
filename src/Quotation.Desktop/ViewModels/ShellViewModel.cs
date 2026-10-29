using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class NavItem(string key, string title, string shortcut, Func<ViewModelBase> factory, string section = "") : ObservableObject
{
    /// <summary>Group heading shown above this item in the sidebar (empty: same group as the item before).</summary>
    public string Section { get; } = section;
    public bool HasSection => Section.Length > 0;
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Shortcut { get; } = shortcut;
    public Func<ViewModelBase> Factory { get; } = factory;
    [ObservableProperty] private bool _isSelected;
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
