using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class TallyViewModel : ViewModelBase
{
    private readonly AppSession _session;

    [ObservableProperty] private SystemStatusDto? _status;
    [ObservableProperty] private TallyTestResultDto? _testResult;
    [ObservableProperty] private string _tallyUrl = "";

    public TallyViewModel(AppSession session)
    {
        _session = session;
        _ = LoadAsync();
    }

    public bool IsAdmin => _session.IsAdmin;
    public string LastSyncText => Formatting.LocalDateTime(Status?.LastSuccessfulSyncUtc) ?? "Never";
    public string ConnectionText => Status?.TallyConnected == true ? "CONNECTED" : "DISCONNECTED";
    public string LastCheckedText => Formatting.LocalDateTime(Status?.TallyLastCheckedUtc) ?? "–";

    partial void OnStatusChanged(SystemStatusDto? value)
    {
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(LastCheckedText));
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Status = await _session.Api.StatusAsync();
        var settings = await _session.Api.SettingsAsync();
        TallyUrl = settings.Tally.Url + (string.IsNullOrWhiteSpace(settings.Tally.CompanyName) ? "" : $"  —  {settings.Tally.CompanyName}");
    });

    [RelayCommand]
    private Task TestConnectionAsync() => RunAsync(async () =>
    {
        TestResult = await _session.Api.TestTallyAsync();
        Status = await _session.Api.StatusAsync();
    });
}
