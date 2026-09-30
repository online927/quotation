using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly Action<string> _navigate;

    [ObservableProperty] private DashboardDto? _data;

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
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () => Data = await _session.Api.DashboardAsync());

    [RelayCommand]
    private void Go(string key) => _navigate(key);
}
