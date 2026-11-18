using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Core.Formater;
using Quotation.Core.Gst;
using Quotation.Desktop.Services;

namespace Quotation.Mobile.ViewModels;

/// <summary>
/// New from a message (like the PC's Formater): paste a WhatsApp or e-mail enquiry; the customer and items are found in Tally
/// with the same rules as the PC; then Save draft or Approve.
/// </summary>
public sealed partial class MessageViewModel(MainViewModel main) : PageViewModel
{
    private readonly Dictionary<(string Kind, string Typed), FormaterMatchDto> _remembered = [];
    private bool _rememberedLoaded;
    private FormaterParse? _parse;
    private CustomerSummaryDto? _customer;

    public override string Title => "New from a message";
    public override bool HasBack => true;

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _customerName = "";
    [ObservableProperty] private string _customerState = "";
    [ObservableProperty] private bool _customerIsTally;
    [ObservableProperty] private string _grandTotal = "";
    public ObservableCollection<MessageLine> Lines { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];
    public ObservableCollection<string> Notes { get; } = [];
    public bool HasProblems => Problems.Count > 0;
    public bool HasNotes => Notes.Count > 0;
    public bool MakesDocuments => main.Session.User?.MakesDocuments == true;
    public bool CanSave => HasResult && !HasProblems && Lines.Count > 0 && MakesDocuments;

    partial void OnTextChanged(string value)
    {
        if (!HasResult) return;
        HasResult = false; // the text changed: "Make quotation" reads it again
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>Pasted text: WhatsApp times and names and greeting lines are taken out (as on the PC).</summary>
    public void Paste(string? pasted)
    {
        if (string.IsNullOrWhiteSpace(pasted)) return;
        var cleaned = FormaterText.CleanPasted(pasted);
        Text = cleaned.Text;
        InfoMessage = cleaned.Removed > 0 ? "Taken out: " + cleaned.Describe() + "." : null;
    }

    [RelayCommand]
    private Task MakeAsync() => RunAsync(async () =>
    {
        InfoMessage = null;
        var api = main.Session.Api;
        if (!_rememberedLoaded)
        {
            foreach (var m in await api.FormaterMatchesAsync()) _remembered[(m.Kind, m.Typed)] = m;
            _rememberedLoaded = true;
        }
        var parse = FormaterText.Parse(Text);
        CustomerSummaryDto? customer = null;
        if (parse.BuyerName.Length > 0)
        {
            var hits = await api.SearchCustomersAsync(parse.BuyerName, 6);
            (customer, _, _) = FormaterMatching.Decide(parse.BuyerName, hits, h => h.Customer.Id, h => h.Match, h => h.Score, [],
                Remembered(FormaterMatchDto.Customer, parse.BuyerName), h => h.Customer);
        }
        var company = main.Session.Company?.Company;
        var withoutGst = parse.GstMode == FormaterGst.None;
        var typedRate = parse.GstPercent is > 0 and var g ? g : 18m;
        var stateCode = customer?.StateCode ?? parse.StateCode;
        var interState = company?.StateCode is { Length: > 0 } ours && stateCode.Length > 0 && ours != stateCode;

        Lines.Clear();
        Notes.Clear();
        var no = 0;
        foreach (var item in parse.Items)
        {
            var hits = (await api.SearchProductsAsync(item.Name, 6)).Where(h => !h.Product.Hidden).ToList();
            var (product, suggestion, _) = FormaterMatching.Decide(item.Name, hits, h => h.Product.Id, h => h.Match, h => h.Score, [],
                Remembered(FormaterMatchDto.Product, item.Name), h => h.Product);
            var rate = item.Rate > 0 ? item.Rate : product?.Rate ?? 0;
            var line = new MessageLine(++no, item, product, rate, withoutGst ? 0 : product?.GstRate ?? typedRate);
            Lines.Add(line);
            if (product is null)
                Notes.Add($"Item {no} is not in Tally: printed as typed" + (suggestion is not null ? $" (Tally has \"{suggestion.Name}\")." : "."));
        }
        _parse = parse;
        _customer = customer;
        CustomerIsTally = customer is not null;
        CustomerName = customer is not null ? (customer.MailingName.Length > 0 ? customer.MailingName : customer.Name) : parse.BuyerName;
        CustomerState = customer?.StateName ?? IndianStates.NameForCode(parse.StateCode) ?? "";
        if (parse.BuyerName.Length > 0 && customer is null) Notes.Add("The customer is not in Tally: printed as typed.");
        var totals = Lines.Count == 0 ? null : QuotationCalculator.Calculate(new CalcInput(
            Lines.Select(l => new CalcLine(l.Item.Qty, l.Rate, l.Item.DiscountPercent, l.GstRate, l.Unit)).ToList(),
            0, null, TaxPresentation.ComputeTax, main.Session.Company?.Quotation.RoundOffTotal ?? true, interState, withoutGst));
        GrandTotal = totals is null ? "" : "₹ " + IndianFormat.Amount(totals.GrandTotal);
        Problems.Clear();
        foreach (var e in parse.Errors) Problems.Add(e.ToString());
        if (parse.Items.Count == 0 && parse.Errors.Count == 0) Problems.Add("No items found. Write each item as: name, quantity, rate (and discount %).");
        HasResult = true;
        foreach (var n in new[] { nameof(HasProblems), nameof(HasNotes), nameof(CanSave) }) OnPropertyChanged(n);
    });

    private int? Remembered(string kind, string typed) =>
        _remembered.TryGetValue((kind, FormaterMatchDto.Normalize(typed)), out var m) ? m.TallyId : null;

    private SaveQuotationRequest ToRequest()
    {
        var p = _parse!;
        var c = _customer;
        var company = main.Session.Company?.Company ?? new CompanySettings();
        var buyer = c is not null
            ? new PartyDto
            {
                Name = c.MailingName.Length > 0 ? c.MailingName : c.Name, Address = c.Address, Gstin = c.Gstin, StateName = c.StateName,
                StateCode = c.StateCode, ContactPerson = c.ContactPerson, Phone = c.Phone, Email = c.Email,
            }
            : new PartyDto
            {
                Name = p.BuyerName, Address = string.Join(Environment.NewLine, p.Address), Gstin = p.Gstin,
                StateName = IndianStates.NameForCode(p.StateCode) ?? "", StateCode = p.StateCode, Phone = p.Phone, Email = p.Email,
            };
        return new SaveQuotationRequest
        {
            DocType = DocumentType.Quotation, Date = p.Date ?? DateOnly.FromDateTime(DateTime.Today), CustomerId = c?.Id, Buyer = buyer,
            Consignee = new PartyDto(), ConsigneeSameAsBuyer = true, BuyersReference = p.OrderNo, BuyersReferenceDate = p.OrderDate,
            OtherReferences = p.OtherRef, PaymentTerms = company.DefaultPaymentTerms, TermsOfDelivery = company.DefaultTermsOfDelivery,
            TermsAndConditions = string.Join(Environment.NewLine, company.TermsAndConditions),
            PreparedBy = company.DefaultPreparedBy.Length > 0 ? company.DefaultPreparedBy : main.Session.User?.DisplayName ?? "",
            VerifiedBy = company.DefaultVerifiedBy, WithoutGst = p.GstMode == FormaterGst.None, FromFormater = true,
            Lines = Lines.Select(l => new QuotationLineDto
            {
                LineNo = l.No, ProductId = l.Product?.Id, ItemName = l.Product?.Name ?? l.Item.Name, PartNumber = l.Product?.PartNumber ?? "",
                Description = string.Join(Environment.NewLine, l.Item.Desc), Hsn = l.Product?.Hsn ?? "", GstRate = l.GstRate,
                Quantity = l.Item.Qty, Unit = l.Unit, Rate = l.Rate, TallyRate = l.Product?.Rate, DiscountPercent = l.Item.DiscountPercent,
                Amount = l.Amount,
            }).ToList(),
        };
    }

    [RelayCommand]
    private Task SaveDraftAsync() => !CanSave ? Task.CompletedTask : RunAsync(async () =>
    {
        var saved = await main.Session.Api.CreateQuotationAsync(ToRequest());
        Reset();
        await main.PushAsync(new QuotationDetailViewModel(main, saved.Id) { InfoMessage = "Saved as a draft." });
    });

    [RelayCommand]
    private Task ApproveAsync() => !CanSave ? Task.CompletedTask : RunAsync(async () =>
    {
        if (ThemeService.Current == AppTheme.Tally && !await main.AskAsync("Approve ?", "Yes or No")) return; // like Tally's "Accept?"
        var saved = await main.Session.Api.CreateQuotationAsync(ToRequest());
        var approved = await main.Session.Api.ApproveQuotationAsync(saved.Id, revision: saved.Revision);
        Reset();
        await main.PushAsync(new QuotationDetailViewModel(main, approved.Id) { InfoMessage = $"Approved as {approved.Number}." });
    });

    private void Reset()
    {
        HasResult = false;
        Text = "";
        Lines.Clear();
        Problems.Clear();
        Notes.Clear();
        OnPropertyChanged(nameof(CanSave));
    }
}

/// <summary>An item read from the message: the Tally item when found (✓), else as typed.</summary>
public sealed class MessageLine(int no, FormaterItem item, ProductSummaryDto? product, decimal rate, decimal gstRate)
{
    public int No { get; } = no;
    public FormaterItem Item { get; } = item;
    public ProductSummaryDto? Product { get; } = product;
    public decimal Rate { get; } = rate;
    public decimal GstRate { get; } = gstRate;
    public string Unit => Item.Unit.Length > 0 ? Item.Unit : Product?.Unit ?? "";
    public decimal Amount => QuotationCalculator.LineAmount(Item.Qty, Rate, Item.DiscountPercent);
    public bool IsTally => Product is not null;
    public bool IsTyped => Product is null;
    public string Name => Product?.Name ?? Item.Name;
    public string Detail => $"{Item.Qty:0.###} × ₹{IndianFormat.Amount(Rate)}" + (Item.DiscountPercent > 0 ? $" · {Item.DiscountPercent:0.##} %" : "") +
                            (IsTyped ? " · not in Tally" : "");
    public string AmountText => IndianFormat.Amount(Amount);
}
