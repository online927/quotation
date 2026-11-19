using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;

namespace Quotation.Mobile.ViewModels;

/// <summary>Dashboard: today's numbers, Tally and Gmail, and the latest quotations.</summary>
public sealed partial class HomeViewModel(MainViewModel main) : PageViewModel
{
    /// <summary>"Today at a glance", or "Eid Mubarak!  ·  Today at a glance" / "Happy Diwali!  ·  …" on the festival days.</summary>
    public string GlanceTitle => Quotation.Desktop.Services.FestivalLooks.GlanceTitle;
    public bool IsFestival => Quotation.Desktop.Services.ThemeService.Festival is not null;

    /// <summary>The festival look came on or went off.</summary>
    public void FestivalChanged()
    {
        OnPropertyChanged(nameof(GlanceTitle));
        OnPropertyChanged(nameof(IsFestival));
    }

    public override string Title => "Dashboard";

    [ObservableProperty] private DashboardDto? _data;
    public ObservableCollection<QuotationRow> Latest { get; } = [];

    public string Initial => (main.Session.User?.DisplayName is { Length: > 0 } n ? n[..1] : "?").ToUpperInvariant();
    public bool TallyOk => Data?.Status.TallyConnected == true;
    public string TallyText => Data is null ? "–" : TallyOk ? "Connected" : "Disconnected";
    public bool GmailOk => Data?.Status.GmailConnected == true;
    public string GmailText => Data is null ? "–" : GmailOk ? "Connected" : "Not connected";
    public string LastSync => Data?.Status.LastSuccessfulSyncUtc is { } t ? t.ToLocalTime().ToString("dd-MMM HH:mm") : "–";

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var api = main.Session.Api;
        var dashboard = api.DashboardAsync();
        var latest = api.QuotationsAsync(take: 5);
        Data = await dashboard;
        Latest.Clear();
        foreach (var q in await latest) Latest.Add(new QuotationRow(q));
        OnPropertyChanged(nameof(TallyOk));
        OnPropertyChanged(nameof(TallyText));
        OnPropertyChanged(nameof(GmailOk));
        OnPropertyChanged(nameof(GmailText));
        OnPropertyChanged(nameof(LastSync));
        OnPropertyChanged(nameof(Initial));
    });

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    /// <summary>A number box: the Quotations page on that list.</summary>
    [RelayCommand]
    private Task GoAsync(string? list) => main.ShowQuotationsAsync(list ?? QuotationsViewModel.Today);

    [RelayCommand]
    private Task OpenAsync(QuotationRow? row) => row is null ? Task.CompletedTask : main.PushAsync(new QuotationDetailViewModel(main, row.Id));

    [RelayCommand]
    private Task MoreAsync() => main.ShowTabAsync(Tabs.More);
}
