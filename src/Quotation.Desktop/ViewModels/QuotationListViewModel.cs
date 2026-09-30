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
}

public sealed class QuotationRow(QuotationSummaryDto s)
{
    public QuotationSummaryDto Summary { get; } = s;
    public Guid Id => Summary.Id;
    public string Number => Summary.Number ?? "(not numbered)";
    public string Date => Summary.Date.ToString("dd-MMM-yyyy");
    public string Customer => Summary.CustomerName;
    public string Item => Summary.LineCount > 1 ? $"{Summary.FirstItem} (+{Summary.LineCount - 1} more)" : Summary.FirstItem;
    public string Total => IndianFormat.Amount(Summary.GrandTotal);
    public string Status => Summary.Status == QuotationStatus.PendingReview ? "Pending review" : Summary.Status.ToString();
    public string Source => Summary.Source.ToString();
    public string CreatedBy => Summary.CreatedBy;
}

/// <summary>Drafts, today's quotations and full history with search (number, customer, GSTIN, item, reference).</summary>
public sealed partial class QuotationListViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly INavigator _navigator;
    private readonly Debouncer _debounce = new(TimeSpan.FromMilliseconds(250));

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private QuotationRow? _selected;
    [ObservableProperty] private string _summary = "";

    public QuotationListPreset Preset { get; }
    public ObservableCollection<QuotationRow> Rows { get; } = [];
    public string Title => Preset switch
    {
        QuotationListPreset.Drafts => "Draft Quotations",
        QuotationListPreset.Today => "Today's Quotations",
        _ => "Quotation History",
    };

    public QuotationListViewModel(AppSession session, INavigator navigator, QuotationListPreset preset, string? initialQuery = null)
    {
        _session = session;
        _navigator = navigator;
        Preset = preset;
        _query = initialQuery ?? "";
        _ = LoadAsync();
    }

    partial void OnQueryChanged(string value) => _debounce.Run(_ => LoadAsync());

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        List<QuotationSummaryDto> list = Preset switch
        {
            QuotationListPreset.Drafts => [
                .. await _session.Api.QuotationsAsync(Query, QuotationStatus.PendingReview, take: 500),
                .. await _session.Api.QuotationsAsync(Query, QuotationStatus.Draft, take: 500)],
            QuotationListPreset.Today => await _session.Api.QuotationsAsync(Query, from: today, to: today, take: 500),
            _ => await _session.Api.QuotationsAsync(Query, take: 300),
        };
        Rows.Clear();
        foreach (var s in list) Rows.Add(new QuotationRow(s));
        var active = list.Where(s => s.Status != QuotationStatus.Cancelled).ToList();
        Summary = $"{list.Count} quotation(s)" + (active.Count > 0 ? $", total ₹ {IndianFormat.Amount(active.Sum(s => s.GrandTotal))}" : "");
    });

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
    private void New() => _navigator.NewQuotation();
}
