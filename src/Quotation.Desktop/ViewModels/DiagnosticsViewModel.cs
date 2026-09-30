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
