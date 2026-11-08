using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class DashboardViewModel : ViewModelBase, IReturnablePage
{
    private readonly AppSession _session;
    private readonly Action<string> _navigate;

    [ObservableProperty] private DashboardDto? _data;

    /// <summary>Administrators: old backup, copy folder missing, low disk, no Tally sync for a day.</summary>
    public ObservableCollection<HealthWarningDto> Warnings { get; } = [];
    public bool HasWarnings => Warnings.Count > 0;
    public bool ShowFollowUp => Data?.TrackOutcomes == true;

    public DashboardViewModel(AppSession session, Action<string> navigate)
    {
        _session = session;
        _navigate = navigate;
        _ = LoadAsync();
    }

    public string TallyText => Data?.Status.TallyConnected == true ? "Connected" : "Disconnected";
    public string GmailText => Data?.Status.GmailConnected == true ? "Connected" : "Not connected";
    public string AiText => Data?.Status.AiConfigured == true ? "Configured" : "Not configured";
    public string LastSyncText => Formatting.LocalDateTime(Data?.Status.LastSuccessfulSyncUtc) ?? "Never";

    partial void OnDataChanged(DashboardDto? value)
    {
        OnPropertyChanged(nameof(TallyText));
        OnPropertyChanged(nameof(GmailText));
        OnPropertyChanged(nameof(AiText));
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(ShowFollowUp));
    }

    public void OnReturnedTo() => _ = LoadAsync();

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Data = await _session.Api.DashboardAsync();
        if (!_session.IsAdmin) return;
        List<HealthWarningDto> list;
        try { list = await _session.Api.HealthWarningsAsync(); }
        catch { return; } // an older server without the check: just show nothing
        Warnings.Clear();
        foreach (var w in list) Warnings.Add(w);
        OnPropertyChanged(nameof(HasWarnings));
    });

    [RelayCommand]
    private void Go(string key) => _navigate(key);
}
