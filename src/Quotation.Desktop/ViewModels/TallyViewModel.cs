using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.ApiClient;
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
    /// <summary>"Changes are synchronized automatically every 3 hours …" with the times set in Settings → Tally.</summary>
    [ObservableProperty] private string _autoSyncText = AutoSyncTextFor(180, "02:00");

    public ObservableCollection<SyncRunRow> Runs { get; } = [];
    public IReadOnlyList<string> SampleTypes { get; } = ["StockItem", "Ledger", "StockGroup", "Unit"];
    public bool SyncRunning => SyncState?.Running == true;

    public TallyViewModel(AppSession session)
    {
        _session = session;
        _ = LoadAsync();
    }

    public bool IsAdmin => _session.IsAdmin;

    /// <summary>The help line under Synchronization, from Settings → Tally (every 180 minutes → "every 3 hours").</summary>
    public static string AutoSyncTextFor(int minutes, string? nightly)
    {
        minutes = Math.Max(1, minutes);
        var every = minutes switch
        {
            60 => "every hour",
            _ when minutes % 60 == 0 => $"every {minutes / 60} hours",
            1 => "every minute",
            _ => $"every {minutes} minutes",
        };
        var night = TimeOnly.TryParseExact(nightly ?? "", "HH:mm", out var t) ? $" and a full sync runs every night at {t:HH:mm}" : "";
        return $"Changes are synchronized automatically {every}{night}.";
    }
    public string LastSyncText => Formatting.LocalDateTime(Status?.LastSuccessfulSyncUtc) ?? "Never";
    public string ConnectionText => Status?.TallyConnected == true ? "Connected" : "Disconnected";
    public bool IsConnected => Status?.TallyConnected == true;
    public string LastCheckedText => Formatting.LocalDateTime(Status?.TallyLastCheckedUtc) ?? "–";

    partial void OnSyncStateChanged(SyncStateDto? value) => OnPropertyChanged(nameof(SyncRunning));

    // Round 39 (RD-1): "If Tally is disconnected" as a checklist that is checked with every refresh.
    public bool TallyOpenOk => IsConnected && !string.IsNullOrWhiteSpace(Status?.TallyCompany);
    public bool PortOk => IsConnected;
    public bool FirewallOk => IsConnected;
    public string TallyOpenMark => TallyOpenOk ? "✓" : "✗";
    public string PortMark => PortOk ? "✓" : "✗";
    public string FirewallMark => FirewallOk ? "✓" : "?";
    /// <summary>The raw Tally data tool is folded until opened (Round 39, RD-1).</summary>
    [ObservableProperty] private bool _rawDataOpen;

    partial void OnStatusChanged(SystemStatusDto? value)
    {
        foreach (var n in new[] { nameof(TallyOpenOk), nameof(PortOk), nameof(FirewallOk), nameof(TallyOpenMark), nameof(PortMark), nameof(FirewallMark) })
            OnPropertyChanged(n);
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(LastCheckedText));
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Status = await _session.Api.StatusAsync();
        var settings = await _session.Api.SettingsAsync();
        TallyUrl = settings.Tally.Url + (string.IsNullOrWhiteSpace(settings.Tally.CompanyName) ? "" : $"  —  {settings.Tally.CompanyName}");
        AutoSyncText = AutoSyncTextFor(settings.Tally.IncrementalSyncMinutes, settings.Tally.NightlyFullSyncTime);
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

    [RelayCommand]
    private Task ReadLastRatesAsync() => StartSyncAsync(Core.Domain.SyncKind.LastRates);

    [RelayCommand]
    private async Task StopLastRatesAsync()
    {
        try
        {
            await _session.Api.StopLastRatesAsync();
            InfoMessage = "Stopping after the current request…";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

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
    public string Status { get; } = r.Status switch
    {
        Core.Domain.SyncStatus.Succeeded => "Done",
        Core.Domain.SyncStatus.Failed => "Failed",
        _ => r.Status.ToString(),
    };
    /// <summary>Round 39 (RD-1): the result as a green / red chip.</summary>
    public bool IsOk { get; } = r.Status == Core.Domain.SyncStatus.Succeeded;
    public bool IsFailed { get; } = r.Status == Core.Domain.SyncStatus.Failed;
    public string Changes { get; } = $"{r.ProductsChanged} products, {r.CustomersChanged} customers; deleted {r.ProductsDeleted}/{r.CustomersDeleted}";
    public string By { get; } = r.TriggeredBy;
    public string Message { get; } = r.Message ?? "";
}
