using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Core.Calculation;

namespace Quotation.Mobile.ViewModels;

/// <summary>Products: the same search as the PC; tap one for part number, brand, HSN / GST, stock group and the Tally rate.</summary>
public sealed partial class ProductsViewModel(MainViewModel main) : PageViewModel
{
    private CancellationTokenSource? _typing;
    public override string Title => "Products";
    public ObservableCollection<ProductRow> Items { get; } = [];
    public string Initial => (main.Session.User?.DisplayName is { Length: > 0 } n ? n[..1] : "?").ToUpperInvariant();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _isEmpty;
    public bool NothingTyped => Search.Trim().Length == 0;

    partial void OnSearchChanged(string value)
    {
        OnPropertyChanged(nameof(NothingTyped));
        _typing?.Cancel();
        var cts = _typing = new CancellationTokenSource();
        _ = Task.Delay(QuotationsViewModel.TypingPause, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadAsync());
        }, TaskScheduler.Default);
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var q = Search.Trim();
        Items.Clear();
        IsEmpty = false;
        if (q.Length == 0) return;
        foreach (var hit in await main.Session.Api.SearchProductsAsync(q, 30)) Items.Add(new ProductRow(hit.Product));
        IsEmpty = Items.Count == 0;
    });

    /// <summary>Tap: the details open under the name (tap again to close).</summary>
    [RelayCommand]
    private Task ToggleAsync(ProductRow? row) => row is null ? Task.CompletedTask : RunAsync(async () =>
    {
        if (row.IsOpen) { row.IsOpen = false; return; }
        foreach (var other in Items) other.IsOpen = false;
        row.Detail ??= await main.Session.Api.ProductAsync(row.Product.Id);
        row.IsOpen = true;
    });

    [RelayCommand]
    private Task MoreAsync() => main.ShowTabAsync(Tabs.More);
}

public sealed partial class ProductRow(ProductSummaryDto p) : ObservableObject
{
    public ProductSummaryDto Product { get; } = p;
    public string Name => Product.Name;
    public string Sub => string.Join(" · ", new[] { Product.Brand, Product.Unit, Product.GstRate is { } g ? $"GST {g:0.##}%" : "" }.Where(s => s.Length > 0));
    public string RateText => Product.Rate is { } r ? "₹ " + IndianFormat.Amount(r) : "";
    public bool IsHidden => Product.Hidden;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private ProductDetailDto? _detail;

    public IReadOnlyList<InfoRow> Info => Detail is not { } d ? [] :
    [
        new("Part number", d.PartNumber.Length > 0 ? d.PartNumber : "—"),
        new("Brand", d.Brand.Length > 0 ? d.Brand : "—"),
        new("HSN / GST", $"{(d.Hsn.Length > 0 ? d.Hsn : "—")} · {(d.GstRate is { } g ? $"{g:0.##}%" : "—")}"),
        new("Stock group", d.StockGroup.Length > 0 ? d.StockGroup : "—"),
        new("Rate (Tally)", d.Rate is { } r ? $"₹ {IndianFormat.Amount(r)} / {d.Unit}" : "—"),
        .. d.Description.Length > 0 ? new[] { new InfoRow("Description", d.Description) } : [],
    ];

    partial void OnDetailChanged(ProductDetailDto? value) => OnPropertyChanged(nameof(Info));
}
