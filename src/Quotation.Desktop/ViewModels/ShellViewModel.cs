using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class NavItem(string key, string title, string shortcut, Func<ViewModelBase> factory) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Shortcut { get; } = shortcut;
    public Func<ViewModelBase> Factory { get; } = factory;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Main application frame: navigation, current page and connection status bar.</summary>
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

    partial void OnStatusChanged(SystemStatusDto? value) => OnPropertyChanged(nameof(LastSyncText));
    public string UserDisplay { get; }

    public ShellViewModel(AppSession session, Action onLogout)
    {
        _session = session;
        _onLogout = onLogout;
        var user = session.Api.CurrentUser;
        UserDisplay = user is null ? "" : $"{user.DisplayName} ({user.Role})";

        NavItems.Add(new NavItem("dashboard", "Dashboard", "Ctrl+D", () => new DashboardViewModel(_session, Navigate)));
        NavItems.Add(new NavItem("new", "New Quotation", "Ctrl+N", () => new QuotationEditorViewModel(_session, this)));
        NavItems.Add(new NavItem("inbox", "AI Inbox", "Ctrl+I", () => new AiInboxViewModel(_session, this)));
        NavItems.Add(new NavItem("drafts", "Draft Quotations", "", () => new QuotationListViewModel(_session, this, QuotationListPreset.Drafts)));
        NavItems.Add(new NavItem("today", "Today's Quotations", "", () => new QuotationListViewModel(_session, this, QuotationListPreset.Today)));
        NavItems.Add(new NavItem("history", "Quotation History", "Ctrl+H", () => new QuotationListViewModel(_session, this, QuotationListPreset.History)));
        NavItems.Add(new NavItem("customers", "Customers", "", () => new CustomersViewModel(_session)));
        NavItems.Add(new NavItem("products", "Products", "", () => new ProductsViewModel(_session)));
        NavItems.Add(new NavItem("tally", "Tally Connection", "", () => new TallyViewModel(_session)));
        NavItems.Add(new NavItem("gmail", "Gmail Connection", "", () => new GmailViewModel(_session, this)));
        NavItems.Add(new NavItem("settings", "Settings", "", () => new SettingsViewModel(_session)));
        if (session.IsAdmin) NavItems.Add(new NavItem("diagnostics", "Diagnostics", "", () => new DiagnosticsViewModel(_session)));

        Navigate("dashboard");
        _ = PollStatusAsync(_cts.Token);
    }

    [RelayCommand]
    public void Navigate(string key)
    {
        var item = NavItems.FirstOrDefault(n => n.Key == key);
        if (item is null) return;
        foreach (var n in NavItems) n.IsSelected = n == item;
        CurrentPage = item.Factory();
    }

    public void OpenQuotation(Guid id)
    {
        foreach (var n in NavItems) n.IsSelected = false;
        CurrentPage = new QuotationEditorViewModel(_session, this, id);
    }

    public void NewQuotation() => Navigate("new");

    /// <summary>Global search: quotation number, customer, GSTIN, product, reference or date.</summary>
    [RelayCommand]
    private void Search()
    {
        if (string.IsNullOrWhiteSpace(GlobalSearch)) return;
        foreach (var n in NavItems) n.IsSelected = n.Key == "history";
        CurrentPage = new QuotationListViewModel(_session, this, QuotationListPreset.History, GlobalSearch.Trim());
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
    }
}
