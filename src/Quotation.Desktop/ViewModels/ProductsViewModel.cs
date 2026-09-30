using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

/// <summary>Product browser with search-as-you-type over the synchronized Tally catalogue.</summary>
public sealed partial class ProductsViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly Debouncer _debounce = new(TimeSpan.FromMilliseconds(120));

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private ProductSearchHitDto? _selected;
    [ObservableProperty] private ProductDetailDto? _detail;
    [ObservableProperty] private string _resultInfo = "";

    public ObservableCollection<ProductSearchHitDto> Results { get; } = [];

    public ProductsViewModel(AppSession session)
    {
        _session = session;
        _ = SearchAsync("");
    }

    partial void OnQueryChanged(string value) => _debounce.Run(ct => SearchAsync(value, ct));

    partial void OnSelectedChanged(ProductSearchHitDto? value)
    {
        Detail = null;
        if (value is not null) _ = RunAsync(async () => Detail = await _session.Api.ProductAsync(value.Product.Id));
    }

    internal async Task SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var hits = await _session.Api.SearchProductsAsync(query, 50, ct);
            if (ct.IsCancellationRequested) return;
            Results.Clear();
            foreach (var h in hits) Results.Add(h);
            ResultInfo = string.IsNullOrWhiteSpace(query) ? $"First {hits.Count} products" : $"{hits.Count} match(es)";
            ErrorMessage = null;
            if (Results.Count > 0 && Selected is null) Selected = Results[0];
        }
        catch (OperationCanceledException) { }
        catch (ApiClient.ApiException ex) { ErrorMessage = ex.FullMessage; }
    }
}
