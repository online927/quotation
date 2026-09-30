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
public sealed partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly AppSession _session;
    private readonly Action _onLogout;
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private SystemStatusDto? _status;
    [ObservableProperty] private string _serverState = "Connecting…";
    [ObservableProperty] private bool _serverReachable = true;

    public ObservableCollection<NavItem> NavItems { get; } = [];
    public string UserDisplay { get; }

    public ShellViewModel(AppSession session, Action onLogout)
    {
        _session = session;
        _onLogout = onLogout;
        var user = session.Api.CurrentUser;
        UserDisplay = user is null ? "" : $"{user.DisplayName} ({user.Role})";

        NavItems.Add(new NavItem("dashboard", "Dashboard", "Ctrl+D", () => new DashboardViewModel(_session, Navigate)));
        NavItems.Add(new NavItem("new", "New Quotation", "Ctrl+N", () => new PlaceholderViewModel("New Quotation", "Phase 5")));
        NavItems.Add(new NavItem("inbox", "AI Inbox", "Ctrl+I", () => new PlaceholderViewModel("AI Inbox", "Phase 11")));
        NavItems.Add(new NavItem("drafts", "Draft Quotations", "", () => new PlaceholderViewModel("Draft Quotations", "Phase 5")));
        NavItems.Add(new NavItem("today", "Today's Quotations", "", () => new PlaceholderViewModel("Today's Quotations", "Phase 12")));
        NavItems.Add(new NavItem("history", "Quotation History", "Ctrl+H", () => new PlaceholderViewModel("Quotation History", "Phase 12")));
        NavItems.Add(new NavItem("customers", "Customers", "", () => new PlaceholderViewModel("Customers", "Phase 4")));
        NavItems.Add(new NavItem("products", "Products", "", () => new PlaceholderViewModel("Products", "Phase 4")));
        NavItems.Add(new NavItem("tally", "Tally Connection", "", () => new PlaceholderViewModel("Tally Connection & Sync", "Phase 2–3")));
        NavItems.Add(new NavItem("gmail", "Gmail Connection", "", () => new PlaceholderViewModel("Gmail Connection", "Phase 10")));
        NavItems.Add(new NavItem("settings", "Settings", "", () => new SettingsViewModel(_session)));

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
