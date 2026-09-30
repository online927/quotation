using System.Collections.ObjectModel;
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
    [ObservableProperty] private SyncStateDto? _syncState;
    [ObservableProperty] private string _sampleType = "StockItem";
    [ObservableProperty] private string _sampleName = "";
    [ObservableProperty] private string _sampleXml = "";

    public ObservableCollection<SyncRunRow> Runs { get; } = [];
    public IReadOnlyList<string> SampleTypes { get; } = ["StockItem", "Ledger", "StockGroup", "Unit"];
    public bool SyncRunning => SyncState?.Running == true;

    public TallyViewModel(AppSession session)
    {
        _session = session;
        _ = LoadAsync();
    }

    public bool IsAdmin => _session.IsAdmin;
    public string LastSyncText => Formatting.LocalDateTime(Status?.LastSuccessfulSyncUtc) ?? "Never";
    public string ConnectionText => Status?.TallyConnected == true ? "CONNECTED" : "DISCONNECTED";
    public string LastCheckedText => Formatting.LocalDateTime(Status?.TallyLastCheckedUtc) ?? "–";

    partial void OnSyncStateChanged(SyncStateDto? value) => OnPropertyChanged(nameof(SyncRunning));

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
        await LoadRunsAsync();
    });

    private async Task LoadRunsAsync()
    {
        SyncState = await _session.Api.SyncStateAsync();
        Runs.Clear();
        foreach (var r in await _session.Api.SyncRunsAsync(20)) Runs.Add(new SyncRunRow(r));
    }

    [RelayCommand]
    private Task FullSyncAsync() => StartSyncAsync(Core.Domain.SyncKind.Full);

    [RelayCommand]
    private Task IncrementalSyncAsync() => StartSyncAsync(Core.Domain.SyncKind.Incremental);

    private Task StartSyncAsync(Core.Domain.SyncKind kind) => RunAsync(async () =>
    {
        InfoMessage = null;
        await _session.Api.StartSyncAsync(kind);
        // Poll until finished; the server does the work in the background.
        do
        {
            await Task.Delay(1000);
            SyncState = await _session.Api.SyncStateAsync();
            InfoMessage = SyncState.Progress ?? "Synchronizing…";
        } while (SyncState.Running);
        InfoMessage = SyncState.LastRun?.Message;
        Status = await _session.Api.StatusAsync();
        await LoadRunsAsync();
    });

    [RelayCommand]
    private Task LoadSampleAsync() => RunAsync(async () =>
    {
        SampleXml = string.IsNullOrWhiteSpace(SampleName) ? "" : await _session.Api.TallySampleAsync(SampleType, SampleName.Trim());
    });

    [RelayCommand]
    private Task TestConnectionAsync() => RunAsync(async () =>
    {
        TestResult = await _session.Api.TestTallyAsync();
        Status = await _session.Api.StatusAsync();
    });
}

public sealed class SyncRunRow(SyncRunDto r)
{
    public string Started { get; } = Formatting.LocalDateTime(r.StartedUtc) ?? "";
    public string Kind { get; } = r.Kind.ToString();
    public string Status { get; } = r.Status.ToString();
    public string Changes { get; } = $"{r.ProductsChanged} products, {r.CustomersChanged} customers; deleted {r.ProductsDeleted}/{r.CustomersDeleted}";
    public string By { get; } = r.TriggeredBy;
    public string Message { get; } = r.Message ?? "";
}
