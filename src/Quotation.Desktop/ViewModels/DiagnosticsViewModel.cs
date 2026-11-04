using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

/// <summary>Administrator diagnostics: connections, data, audit log and the server log.</summary>
public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly AppSession _session;

    [ObservableProperty] private DiagnosticsDto? _info;
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _auditFilter = "";
    public ObservableCollection<AuditEntryDto> Audit { get; } = [];
    /// <summary>Requests slower than a second, newest first (client and server times, what the server was doing).</summary>
    public ObservableCollection<SlowRequestRow> SlowRequests { get; } = [];

    public DiagnosticsViewModel(AppSession session)
    {
        _session = session;
        _ = LoadAsync();
    }

    public string DatabaseSize => Info is null ? "" : $"{Info.DatabaseBytes / 1024.0 / 1024.0:0.0} MB";
    public string ClientInfo => $"{Environment.MachineName} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription} · client {typeof(App).Assembly.GetName().Version}";

    partial void OnInfoChanged(DiagnosticsDto? value) => OnPropertyChanged(nameof(DatabaseSize));

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        Info = await _session.Api.DiagnosticsAsync();
        await LoadAuditCoreAsync();
        SlowRequests.Clear();
        foreach (var s in await _session.Api.SlowRequestsAsync()) SlowRequests.Add(new SlowRequestRow(s));
        LogText = await _session.Api.ServerLogAsync(300);
    });

    [RelayCommand]
    private Task BackupAsync() => RunAsync(async () =>
    {
        var path = await _session.Api.BackupNowAsync();
        InfoMessage = "Backup written on the server: " + path;
    });

    [RelayCommand]
    private Task LoadAuditAsync() => RunAsync(LoadAuditCoreAsync);

    private async Task LoadAuditCoreAsync()
    {
        Audit.Clear();
        var entries = await _session.Api.AuditAsync(take: 500);
        foreach (var a in entries.Where(a => AuditFilter.Length == 0
                     || a.Action.Contains(AuditFilter, StringComparison.OrdinalIgnoreCase)
                     || a.User.Contains(AuditFilter, StringComparison.OrdinalIgnoreCase)
                     || a.Details.Contains(AuditFilter, StringComparison.OrdinalIgnoreCase)))
        {
            Audit.Add(a);
        }
    }
}

/// <summary>One slow request as listed in Diagnostics, with the likely cause.</summary>
public sealed class SlowRequestRow(SlowRequestDto s)
{
    public string When => s.WhenUtc.ToLocalTime().ToString("dd-MMM HH:mm:ss");
    public string Who => string.Join(" / ", new[] { s.Machine, s.User }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string What => s.What;
    public string Total => Seconds(s.TotalMs);
    public string Server => Seconds(s.ServerMs);
    public string Busy => string.IsNullOrWhiteSpace(s.Busy) ? "—" : s.Busy;

    /// <summary>Total slow but the server quick: the network or finding the server PC; the server slow: the server PC.</summary>
    public string Cause => s switch
    {
        { ServerMs: >= 1000 } => string.IsNullOrWhiteSpace(s.Busy) ? "Server PC slow" : "Server busy: " + s.Busy,
        { TotalMs: not null, ServerMs: null } => "No answer (network / server off)",
        { TotalMs: not null, ServerMs: < 1000 } => "Network or PC name",
        _ => "",
    };

    private static string Seconds(double? ms) => ms is null ? "—" : $"{ms.Value / 1000:0.00} s";
}
