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
    /// <summary>Also list items marked "Do Not Use" (DNU) in Tally; they are never offered when making quotations.</summary>
    [ObservableProperty] private bool _showHidden;
    partial void OnShowHiddenChanged(bool value) => _debounce.Run(ct => SearchAsync(Query, ct));

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
        Datasheet = null;
        if (value is not null) _ = RunAsync(async () =>
        {
            Detail = await _session.Api.ProductAsync(value.Product.Id);
            try { Datasheet = await _session.Api.DatasheetAsync(value.Product.Id); }
            catch (ApiClient.ApiException) { Datasheet = null; } // an older server
        });
    }

    // ---------------------------------------------------------------- Round 36: datasheet

    /// <summary>The item's PDF datasheet, if any.</summary>
    [ObservableProperty] private DatasheetDto? _datasheet;
    public bool IsAdmin => _session.IsAdmin;
    public bool HasDatasheet => Datasheet is not null;
    public bool NoDatasheet => Datasheet is null;
    public bool CanUploadDatasheet => IsAdmin && Detail is not null;
    public string DatasheetInfo => Datasheet is { } d
        ? $"{d.Pages} page{(d.Pages == 1 ? "" : "s")} · added {d.UploadedUtc.ToLocalTime():dd MMM} by {d.UploadedBy}"
        : "";
    /// <summary>"Add to the quotation PDF when this item is quoted".</summary>
    public bool DatasheetOnPdf
    {
        get => Datasheet?.AddToPdf ?? false;
        set
        {
            if (Datasheet is not { } d || d.AddToPdf == value || !IsAdmin) return;
            _ = RunAsync(async () => Datasheet = await _session.Api.SetDatasheetOnPdfAsync(d.ProductId, value));
        }
    }

    partial void OnDatasheetChanged(DatasheetDto? value)
    {
        OnPropertyChanged(nameof(HasDatasheet));
        OnPropertyChanged(nameof(NoDatasheet));
        OnPropertyChanged(nameof(DatasheetInfo));
        OnPropertyChanged(nameof(DatasheetOnPdf));
    }

    partial void OnDetailChanged(ProductDetailDto? value)
    {
        OnPropertyChanged(nameof(CanUploadDatasheet));
        foreach (var name in new[] { nameof(HasPartNumber), nameof(HasBrand), nameof(HasAliases), nameof(HasDescription), nameof(HasRateSource),
                     nameof(GstText), nameof(HasRate), nameof(NoRate), nameof(RateText), nameof(RateSourceText), nameof(SyncedText) })
            OnPropertyChanged(name);
    }

    // Round 39 (RD-6): the details card shows only the rows that have something, in plain words and local time.
    public bool HasPartNumber => !string.IsNullOrWhiteSpace(Detail?.PartNumber);
    public bool HasBrand => !string.IsNullOrWhiteSpace(Detail?.Brand);
    public bool HasAliases => Detail?.Aliases.Count > 0;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Detail?.Description);
    public bool HasRateSource => !string.IsNullOrWhiteSpace(Detail?.RateSource);
    public string GstText => Detail?.GstRate is { } g
        ? $"{g:0.##}%" + (string.IsNullOrWhiteSpace(Detail.GstSource) ? "" : $" (from {Detail.GstSource.ToLowerInvariant()})")
        : "not set in Tally";
    public bool HasRate => Detail?.Rate is not null;
    public bool NoRate => Detail is not null && Detail.Rate is null;
    public string RateText => Detail?.Rate is { } r ? $"₹ {r:N2}" : "";
    public string RateSourceText => Detail is null ? "" : Detail.RateSource + (Detail.RateDate is { } d ? $" (from {d:dd-MMM-yyyy})" : "");
    public string SyncedText => Detail is null ? "" : DateTime.SpecifyKind(Detail.LastSyncedUtc, DateTimeKind.Utc).ToLocalTime().ToString("dd-MMM-yyyy HH:mm");

    /// <summary>Uploads (or replaces) the datasheet of the selected item from a PDF on this PC (administrators).</summary>
    public Task UploadDatasheetAsync(string path) => RunAsync(async () =>
    {
        if (Detail is not { } item) return;
        Datasheet = await _session.Api.UploadDatasheetAsync(item.Id, Path.GetFileName(path), await File.ReadAllBytesAsync(path));
        InfoMessage = "Datasheet added. Quoting this item adds its pages after the quotation.";
    });

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task RemoveDatasheetAsync() => RunAsync(async () =>
    {
        if (Datasheet is not { } d) return;
        await _session.Api.RemoveDatasheetAsync(d.ProductId);
        Datasheet = null;
    });

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenDatasheetAsync() => RunAsync(async () =>
    {
        if (Datasheet is not { } d) return;
        var bytes = await _session.Api.DatasheetFileAsync(d.ProductId);
        var path = Path.Combine(Path.GetTempPath(), "Quotation-datasheet-" + d.ProductId + ".pdf");
        await File.WriteAllBytesAsync(path, bytes);
        _session.Documents.Open(path);
    });

    /// <summary>F5: the list again from the server.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task Refresh() => SearchAsync(Query);

    internal async Task SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var hits = await _session.Api.SearchProductsAsync(query, 50, ct, includeHidden: ShowHidden);
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
