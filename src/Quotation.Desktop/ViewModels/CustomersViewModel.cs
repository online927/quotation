using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Quotation.Contracts;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class CustomersViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly Debouncer _debounce = new(TimeSpan.FromMilliseconds(120));

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private CustomerSearchHitDto? _selected;
    [ObservableProperty] private CustomerDetailDto? _detail;
    [ObservableProperty] private string _resultInfo = "";

    public ObservableCollection<CustomerSearchHitDto> Results { get; } = [];

    public CustomersViewModel(AppSession session)
    {
        _session = session;
        _ = SearchAsync("");
    }

    public string AddressText => Detail is null ? "" : string.Join(Environment.NewLine, Detail.AddressLines);
    public string ShipToText => Detail is null || Detail.ShipToAddresses.Count == 0
        ? "Same as billing address"
        : string.Join(Environment.NewLine + Environment.NewLine,
            Detail.ShipToAddresses.Select(a => $"{a.Name}{Environment.NewLine}{string.Join(Environment.NewLine, a.Lines)}{Environment.NewLine}{a.StateName} {a.Pincode}"));

    partial void OnQueryChanged(string value) => _debounce.Run(ct => SearchAsync(value, ct));

    partial void OnDetailChanged(CustomerDetailDto? value)
    {
        OnPropertyChanged(nameof(AddressText));
        OnPropertyChanged(nameof(ShipToText));
    }

    partial void OnSelectedChanged(CustomerSearchHitDto? value)
    {
        Detail = null;
        if (value is not null) _ = RunAsync(async () => Detail = await _session.Api.CustomerAsync(value.Customer.Id));
    }

    internal async Task SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var hits = await _session.Api.SearchCustomersAsync(query, 50, ct);
            if (ct.IsCancellationRequested) return;
            Results.Clear();
            foreach (var h in hits) Results.Add(h);
            ResultInfo = string.IsNullOrWhiteSpace(query) ? $"First {hits.Count} customers" : $"{hits.Count} match(es)";
            ErrorMessage = null;
            if (Results.Count > 0 && Selected is null) Selected = Results[0];
        }
        catch (OperationCanceledException) { }
        catch (ApiClient.ApiException ex) { ErrorMessage = ex.FullMessage; }
    }
}
