using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Mobile.Services;

namespace Quotation.Mobile.ViewModels;

/// <summary>Customers (Tally ledgers): the same search as the PC; tap one for the details page.</summary>
public sealed partial class CustomersViewModel(MainViewModel main) : PageViewModel
{
    private CancellationTokenSource? _typing;
    public override string Title => "Customers";
    public ObservableCollection<CustomerSummaryDto> Items { get; } = [];
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
        foreach (var hit in await main.Session.Api.SearchCustomersAsync(q, 30)) Items.Add(hit.Customer);
        IsEmpty = Items.Count == 0;
    });

    [RelayCommand]
    private Task OpenAsync(CustomerSummaryDto? c) => c is null ? Task.CompletedTask : main.PushAsync(new CustomerDetailViewModel(main, c.Id, c.Name));

    [RelayCommand]
    private Task MoreAsync() => main.ShowTabAsync(Tabs.More);
}

/// <summary>A customer: Call / WhatsApp / E-mail in one tap, GSTIN, contact, address, and the quotations made to them.</summary>
public sealed partial class CustomerDetailViewModel(MainViewModel main, int id, string name) : PageViewModel
{
    public override string Title => Customer?.Name ?? name;
    public override bool HasBack => true;

    [ObservableProperty] private CustomerDetailDto? _customer;
    public ObservableCollection<InfoRow> Info { get; } = [];
    public ObservableCollection<QuotationRow> Quotations { get; } = [];

    public string Subtitle => Customer is { } c ? string.Join(" · ", new[] { c.StateName, c.LedgerGroup }.Where(s => s.Length > 0)) : "";
    public string Phone => Customer is { } c ? PhoneNumbers.First(c.Mobile.Length > 0 ? c.Mobile : c.Phone) : "";
    public bool HasPhone => Phone.Any(char.IsDigit);
    public bool HasEmail => Customer?.Email.Contains('@') == true;
    public bool HasQuotations => Quotations.Count > 0;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var customer = main.Session.Api.CustomerAsync(id);
        var quotations = main.Session.Api.QuotationsAsync(customerId: id, take: 20);
        var c = await customer;
        Customer = c;
        Info.Clear();
        if (c.Gstin.Length > 0) Info.Add(new InfoRow("GSTIN", c.Gstin));
        if (c.ContactPerson.Length > 0) Info.Add(new InfoRow("Contact", c.ContactPerson));
        foreach (var number in new[] { c.Mobile, c.Phone }.Where(n => n.Any(char.IsDigit)).Distinct())
            Info.Add(new InfoRow("Phone", number, "tel"));
        if (c.Email.Length > 0) Info.Add(new InfoRow("E-mail", c.Email, "mail"));
        var address = string.Join(", ", c.AddressLines.Where(l => l.Trim().Length > 0)) + (c.Pincode.Length > 0 && !string.Join(" ", c.AddressLines).Contains(c.Pincode) ? " " + c.Pincode : "");
        if (address.Trim().Length > 0) Info.Add(new InfoRow("Address", address.Trim()));
        Quotations.Clear();
        foreach (var q in await quotations) Quotations.Add(new QuotationRow(q));
        foreach (var n in new[] { nameof(Title), nameof(Subtitle), nameof(Phone), nameof(HasPhone), nameof(HasEmail), nameof(HasQuotations) }) OnPropertyChanged(n);
    });

    [RelayCommand]
    private void Call() { if (HasPhone) main.Session.Phone.Dial(Phone); }

    [RelayCommand]
    private void WhatsApp() { if (HasPhone) main.Session.Phone.WhatsAppChat(PhoneNumbers.ForWhatsApp(Phone)); }

    [RelayCommand]
    private void Email() { if (HasEmail) main.Session.Phone.Email(Customer!.Email.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)[0], ""); }

    /// <summary>A phone number or e-mail in the details: call or write.</summary>
    [RelayCommand]
    private void Tap(InfoRow? row)
    {
        if (row?.Link == "tel") main.Session.Phone.Dial(PhoneNumbers.First(row.Value));
        else if (row?.Link == "mail") main.Session.Phone.Email(row.Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)[0], "");
    }

    [RelayCommand]
    private Task OpenAsync(QuotationRow? row) => row is null ? Task.CompletedTask : main.PushAsync(new QuotationDetailViewModel(main, row.Id));
}
