using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public enum QuotationListPreset
{
    Drafts,
    Today,
    History,
    Proforma,
}

public sealed record DocTypeFilter(string Label, DocumentType? Type)
{
    public override string ToString() => Label;
}

public sealed class QuotationRow(QuotationSummaryDto s)
{
    public QuotationSummaryDto Summary { get; } = s;
    public Guid Id => Summary.Id;
    public string Number => Summary.Number ?? "(not numbered)";
    public bool IsProforma => Summary.DocType == DocumentType.ProformaInvoice;
    public string Type => IsProforma ? "Proforma" : "Quotation";
    public Avalonia.Media.IBrush TypeBrush => IsProforma
        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8944AB"))
        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0071E3"));
    public string Date => Summary.Date.ToString("dd-MMM-yyyy");
    public string Customer => Summary.CustomerName;
    public string Item => Summary.LineCount > 1 ? $"{Summary.FirstItem} (+{Summary.LineCount - 1} more)" : Summary.FirstItem;
    public string Total => IndianFormat.Amount(Summary.GrandTotal);
    public string Status => Summary.Status == QuotationStatus.PendingReview ? "Pending review" : Summary.Status.ToString();
    public bool IsFinal => Summary.Status is QuotationStatus.Approved or QuotationStatus.Generated;
    public bool IsPending => Summary.Status == QuotationStatus.PendingReview;
    public bool IsCancelled => Summary.Status == QuotationStatus.Cancelled;
    public string Source => Summary.Source.ToString();
    public string CreatedBy => Summary.CreatedBy;
}

/// <summary>Drafts, today's quotations, proforma invoices and full history with search (number, customer, GSTIN, item, reference).</summary>
public sealed partial class QuotationListViewModel : ViewModelBase, ITitledPage, IReturnablePage
{
    private readonly AppSession _session;
    private readonly INavigator _navigator;
    private readonly Debouncer _debounce = new(TimeSpan.FromMilliseconds(250));

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private QuotationRow? _selected;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private DocTypeFilter _typeFilter;

    public static IReadOnlyList<DocTypeFilter> TypeFilters { get; } =
    [
        new("All documents", null),
        new("Quotations", DocumentType.Quotation),
        new("Proforma invoices", DocumentType.ProformaInvoice),
    ];

    public QuotationListPreset Preset { get; }
    public ObservableCollection<QuotationRow> Rows { get; } = [];
    public string Title => Preset switch
    {
        QuotationListPreset.Drafts => "Draft Quotations",
        QuotationListPreset.Today => "Today's Quotations",
        QuotationListPreset.Proforma => "Proforma Invoices",
        _ => "Quotation History",
    };
    public bool IsProformaList => Preset == QuotationListPreset.Proforma;
    public string NewButtonText => IsProformaList ? "New Proforma Invoice" : "New Quotation";

    public QuotationListViewModel(AppSession session, INavigator navigator, QuotationListPreset preset, string? initialQuery = null)
    {
        _session = session;
        _navigator = navigator;
        Preset = preset;
        _query = initialQuery ?? "";
        _typeFilter = TypeFilters[preset == QuotationListPreset.Proforma ? 2 : 0];
        _ = LoadAsync();
    }

    partial void OnQueryChanged(string value) => _debounce.Run(_ => LoadAsync());
    partial void OnTypeFilterChanged(DocTypeFilter value) => _ = LoadAsync();

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var type = TypeFilter.Type;
        List<QuotationSummaryDto> list = Preset switch
        {
            QuotationListPreset.Drafts => [
                .. await _session.Api.QuotationsAsync(Query, QuotationStatus.PendingReview, take: 500, type: type),
                .. await _session.Api.QuotationsAsync(Query, QuotationStatus.Draft, take: 500, type: type)],
            QuotationListPreset.Today => await _session.Api.QuotationsAsync(Query, from: today, to: today, take: 500, type: type),
            _ => await _session.Api.QuotationsAsync(Query, take: 300, type: type),
        };
        var selectedId = Selected?.Id;
        Rows.Clear();
        foreach (var s in list) Rows.Add(new QuotationRow(s));
        Selected = Rows.FirstOrDefault(r => r.Id == selectedId);
        var active = list.Where(s => s.Status != QuotationStatus.Cancelled).ToList();
        Summary = $"{list.Count} document(s)" + (active.Count > 0 ? $", total ₹ {IndianFormat.Amount(active.Sum(s => s.GrandTotal))}" : "");
    });

    /// <summary>Back from a quotation: reload (it may have been approved or changed) with the same search and filter.</summary>
    public void OnReturnedTo() => _ = LoadAsync();

    [RelayCommand]
    private void Open(QuotationRow? row)
    {
        row ??= Selected;
        if (row is not null) _navigator.OpenQuotation(row.Id);
    }

    [RelayCommand]
    private Task DuplicateAsync(QuotationRow? row) => RunAsync(async () =>
    {
        row ??= Selected;
        if (row is null) return;
        var copy = await _session.Api.DuplicateQuotationAsync(row.Id);
        _navigator.OpenQuotation(copy.Id);
    });

    [RelayCommand]
    private void New() => _navigator.Navigate(IsProformaList ? "newproforma" : "new");
}
