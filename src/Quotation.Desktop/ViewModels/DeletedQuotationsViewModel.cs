using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed class DeletedRow(DeletedQuotationDto d)
{
    public DeletedQuotationDto Dto { get; } = d;
    public string Number => d.Number;
    public string Type => d.DocType == DocumentType.ProformaInvoice ? "Proforma" : "Quotation";
    public string Date => d.Date.ToString("dd-MMM-yyyy");
    public string Customer => d.CustomerName;
    public string Total => IndianFormat.Amount(d.GrandTotal);
    public string Deleted => $"{Formatting.LocalDateTime(d.DeletedUtc)} by {d.DeletedBy}";
    public string RemovedOn => d.PurgeUtc.ToLocalTime().ToString("dd-MMM-yyyy");
    public string NumberNote => d.NumberStillFree ? "" : "number given to a new document";
}

/// <summary>Administrator: quotations and proforma invoices deleted in the last 30 days, with Restore and Delete permanently.</summary>
public sealed partial class DeletedQuotationsViewModel : ViewModelBase, ITitledPage, IReturnablePage
{
    private readonly AppSession _session;
    private readonly INavigator _navigator;

    public ObservableCollection<DeletedRow> Rows { get; } = [];
    [ObservableProperty] private DeletedRow? _selected;
    public string Title => "Deleted";

    public DeletedQuotationsViewModel(AppSession session, INavigator navigator)
    {
        _session = session;
        _navigator = navigator;
        _ = LoadAsync();
    }

    public void OnReturnedTo() => _ = LoadAsync();

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Rows.Clear();
        foreach (var d in await _session.Api.DeletedQuotationsAsync()) Rows.Add(new DeletedRow(d));
    });

    [RelayCommand]
    private async Task RestoreAsync(DeletedRow? row)
    {
        row ??= Selected;
        if (row is null) return;
        var message = row.Dto.NumberStillFree
            ? $"Restore {row.Number} ({row.Customer})?"
            : $"{row.Number} was given to a new document meanwhile. {row.Customer}'s document comes back as a draft without a number and gets the next number when saved. Restore it?";
        if (!await _navigator.ConfirmAsync("Restore", message, "Restore", "Cancel")) return;
        await RunAsync(async () =>
        {
            var result = await _session.Api.RestoreQuotationAsync(row.Dto.Id);
            InfoMessage = result.Message;
            Rows.Remove(row);
        });
    }

    [RelayCommand]
    private async Task PurgeAsync(DeletedRow? row)
    {
        row ??= Selected;
        if (row is null) return;
        if (!await _navigator.ConfirmAsync("Delete permanently",
                $"Delete {row.Number} ({row.Customer}, ₹ {row.Total}) permanently? This cannot be undone.", "Delete permanently", "Cancel")) return;
        await RunAsync(async () =>
        {
            await _session.Api.PurgeQuotationAsync(row.Dto.Id);
            InfoMessage = $"{row.Number} deleted permanently.";
            Rows.Remove(row);
        });
    }
}
