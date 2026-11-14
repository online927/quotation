using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Core.Formater;
using Quotation.Core.Gst;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

/// <summary>An item of the Formater page as the app understood it: matched to a Tally item, or typed (not in Tally).</summary>
public sealed partial class FormaterLineViewModel : ObservableObject
{
    public required int No { get; init; }
    public required FormaterItem Typed { get; init; }
    public ProductSummaryDto? Product { get; init; }
    /// <summary>A close Tally item offered when the typed name did not match one clearly.</summary>
    public ProductSummaryDto? Suggestion { get; init; }
    public required decimal GstRate { get; init; }
    public required decimal Amount { get; init; }
    /// <summary>Matched because this typing was matched to this Tally item before ("Use"), on any PC.</summary>
    public bool IsRemembered { get; init; }
    public string RememberedText => IsRemembered ? $"typed \"{Typed.Name}\"" : "";
    /// <summary>"Keep as typed" for a match the app made itself; a remembered one has "Forget" instead.</summary>
    public bool CanKeepTyped => IsTally && !IsRemembered;
    public bool IsEven => No % 2 == 0;

    public bool IsTally => Product is not null;
    public bool IsTyped => Product is null;
    public bool HasSuggestion => Product is null && Suggestion is not null;
    public string ItemName => Product?.Name ?? Typed.Name;
    public string PartNumber => Product?.PartNumber ?? "";
    public bool HasPartNumber => PartNumber.Length > 0;
    public string Description => Typed.Desc.Count > 0 ? string.Join(Environment.NewLine, Typed.Desc) : Product?.Description ?? "";
    public bool HasDescription => Description.Length > 0;
    public string Hsn => Product?.Hsn ?? "";
    public string Unit => Product is { Unit.Length: > 0 } p ? p.Unit : Typed.Unit;
    public decimal Rate => Typed.Rate > 0 ? Typed.Rate : Product?.Rate ?? 0;
    public string GstText => $"{GstRate:0.##}%";
    public string QuantityText => IndianFormat.Quantity(Typed.Qty);
    public string RateText => Rate > 0 ? IndianFormat.Amount(Rate) : "";
    public string DiscountText => Typed.DiscountPercent == 0 ? "" : $"{Typed.DiscountPercent:0.##}";
    public string AmountText => IndianFormat.Amount(Amount);
    public string SourceText => IsTally ? "Tally ✓" : "Typed (not in Tally)";
    public string SuggestionText => Suggestion is null ? "" : "Tally has: " + Suggestion.Name;
}

/// <summary>A problem in the text, with the line it is on (0 when it is not on one line).</summary>
public sealed record FormaterProblem(int Line, string Text)
{
    public bool HasLine => Line > 0;
    public string GoToText => $"Go to line {Line}";
    public override string ToString() => Line > 0 ? $"Line {Line}: {Text}" : Text;
}

/// <summary>
/// The Formater page: a quotation typed or pasted as text (the way of the separate Formater app). The customer and items
/// are matched to the Tally lists where they clearly are the same (read only; Tally is never changed); anything else is
/// printed as typed. "Approve" puts it on the quotation screen and approves it there like any quotation (next number,
/// Quotation History, PDF in the Zoho layout).
/// </summary>
public sealed partial class FormaterViewModel : ViewModelBase, ITitledPage, IDisposable
{
    /// <summary>The text is kept while the app is open, so going to another page and back does not lose it.</summary>
    private static string _keptText = "";
    private static decimal? _keptPacking;

    private readonly AppSession _session;
    private readonly INavigator _navigator;
    private AllSettingsDto _settings = new();
    private readonly Dictionary<string, List<CustomerSearchHitDto>> _customerHits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ProductSearchHitDto>> _productHits = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The user's own choice for a typed name: a Tally id, or -1 to keep it as typed.</summary>
    private readonly Dictionary<string, int> _customerChoice = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _productChoice = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tally ledgers / items chosen for typed names before (on any PC), by kind and typed name.</summary>
    private readonly Dictionary<(string Kind, string Typed), FormaterMatchDto> _remembered = [];
    private CancellationTokenSource? _refreshCts;
    private FormaterParse? _parse;
    private CustomerSummaryDto? _customer;
    /// <summary>The text as it was before the last paste was cleaned, and the text the paste would have given (for Undo).</summary>
    private (string Before, string Raw)? _pasteUndo;
    private bool _settingText;

    public static TimeSpan RefreshDelay { get; set; } = TimeSpan.FromMilliseconds(350);

    public FormaterViewModel(AppSession session, INavigator navigator)
    {
        _session = session;
        _navigator = navigator;
        _text = _keptText;
        _packingForwarding = _keptPacking;
        _ = InitializeAsync();
    }

    public string Title => "Formater";

    [ObservableProperty] private string _text;
    [ObservableProperty] private string _numberText = "";
    [ObservableProperty] private string _dateText = DateTime.Today.ToString("dd-MMM-yyyy");
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _customerName = "";
    [ObservableProperty] private string _customerDetails = "";
    [ObservableProperty] private bool _customerIsTally;
    [ObservableProperty] private bool _hasCustomer;
    [ObservableProperty] private string _customerSuggestion = "";
    [ObservableProperty] private string _orderText = "";
    [ObservableProperty] private string _otherRefText = "";
    [ObservableProperty] private string _gstNote = "";
    [ObservableProperty] private CalcResult? _totals;
    [ObservableProperty] private bool _customerIsRemembered;
    /// <summary>Packing &amp; forwarding in rupees (its own box, not in the text); empty = none.</summary>
    [ObservableProperty] private decimal? _packingForwarding;
    /// <summary>What was taken out of the pasted text (WhatsApp times and names, greetings); empty when nothing.</summary>
    [ObservableProperty] private string _pasteNote = "";

    public ObservableCollection<FormaterLineViewModel> Lines { get; } = [];
    public ObservableCollection<FormaterProblem> Problems { get; } = [];
    /// <summary>The lines of the text that have a problem (marked red next to the text).</summary>
    public IReadOnlyList<int> ProblemLines => Problems.Where(p => p.Line > 0).Select(p => p.Line).Distinct().ToList();
    public ObservableCollection<string> Notes { get; } = [];

    public bool HasLines => Lines.Count > 0;
    public bool HasProblems => Problems.Count > 0;
    public bool HasNotes => Notes.Count > 0;
    public bool CustomerIsTyped => HasCustomer && !CustomerIsTally;
    public bool CanKeepCustomerTyped => CustomerIsTally && !CustomerIsRemembered;
    public string CustomerRememberedText => _parse is null ? "" : $"You typed \"{_parse.BuyerName}\"; this ledger was chosen for it before.";
    public bool HasPasteNote => PasteNote.Length > 0;
    public bool HasPacking => Totals is { PackingForwarding: > 0 };
    public string PackingText => IndianFormat.Amount(Totals?.PackingForwarding ?? 0);
    public bool HasCustomerSuggestion => CustomerSuggestion.Length > 0;
    public bool CanApprove => _parse is { IsValid: true } && Lines.Count > 0 && !IsRefreshing && _session.MakesDocuments;
    public string SubtotalText => IndianFormat.Amount(Totals?.Subtotal ?? 0);
    public string TaxText => Totals is null || Totals.Taxes.Count == 0 ? (_parse?.GstMode == FormaterGst.None ? "Without GST" : "") :
        string.Join("   ", Totals.Taxes.Select(t => $"{t.Name} {t.RatePercent:0.##}%: {IndianFormat.Amount(t.Amount)}"));
    public string RoundOffText => Totals is null || Totals.RoundOff == 0 ? "" : IndianFormat.Amount(Totals.RoundOff);
    public string GrandTotalText => "₹ " + IndianFormat.Amount(Totals?.GrandTotal ?? 0);
    public string TotalQuantityText => Totals is null ? "" : $"{IndianFormat.Quantity(Totals.TotalQuantity)} {Totals.TotalQuantityUnit}".Trim();

    public const string Example = """
        Sample Company Pvt Ltd
        - Plot 1, Industrial Area
        - Pune, Maharashtra 411001

        Your enquiry dated 10-Nov-2026

        187-901-10, 2, 15600
        Make - Mitutoyo

        530-104, 5, 3250, 5

        Digital Caliper 150mm Special, 2, 3450
        Make - Insize
        """;

    private async Task InitializeAsync()
    {
        try
        {
            _settings = await _session.Api.SettingsAsync();
            var next = await _session.Api.NextNumberAsync();
            NumberText = $"{next.Number} (provisional)";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        try
        {
            foreach (var m in await _session.Api.FormaterMatchesAsync()) _remembered[(m.Kind, m.Typed)] = m;
        }
        catch (ApiException)
        {
            // An older server: nothing remembered yet.
        }
        await RefreshAsync(CancellationToken.None);
    }

    partial void OnTextChanged(string value)
    {
        _keptText = value;
        if (!_settingText && _pasteUndo is not null)
        {
            // Typed on after a cleaned paste: the note (and its Undo) goes.
            _pasteUndo = null;
            PasteNote = "";
        }
        _refreshCts?.Cancel();
        var cts = _refreshCts = new CancellationTokenSource();
        _ = DelayedRefreshAsync(cts.Token);
    }

    partial void OnPackingForwardingChanged(decimal? value)
    {
        _keptPacking = value is > 0 ? value : null;
        if (_parse is not null) _ = RefreshAsync(CancellationToken.None);
    }

    partial void OnPasteNoteChanged(string value) => OnPropertyChanged(nameof(HasPasteNote));

    partial void OnCustomerIsRememberedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanKeepCustomerTyped));
        OnPropertyChanged(nameof(CustomerRememberedText));
    }

    private async Task DelayedRefreshAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(RefreshDelay, ct);
            await RefreshAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Reads the text again and matches the customer and items to the Tally lists (cached per name).</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        IsRefreshing = true;
        OnPropertyChanged(nameof(CanApprove));
        try
        {
            var parse = FormaterText.Parse(Text);
            CustomerSummaryDto? customer = null;
            CustomerSummaryDto? customerSuggestion = null;
            var customerRemembered = false;
            if (parse.BuyerName.Length > 0)
            {
                var hits = await CustomerHitsAsync(parse.BuyerName, ct);
                (customer, customerSuggestion, customerRemembered) = Decide(parse.BuyerName, hits, h => h.Customer.Id, h => h.Match, h => h.Score,
                    _customerChoice, Remembered(FormaterMatchDto.Customer, parse.BuyerName), h => h.Customer);
            }
            var lines = new List<(FormaterItem Item, ProductSummaryDto? Product, ProductSummaryDto? Suggestion, bool Remembered)>();
            foreach (var item in parse.Items)
            {
                var hits = (await ProductHitsAsync(item.Name, ct)).Where(h => !h.Product.Hidden).ToList();
                var (p, s, r) = Decide(item.Name, hits, h => h.Product.Id, h => h.Match, h => h.Score, _productChoice,
                    Remembered(FormaterMatchDto.Product, item.Name), h => h.Product);
                lines.Add((item, p, s, r));
            }
            ct.ThrowIfCancellationRequested();
            CustomerIsRemembered = customerRemembered;
            Show(parse, customer, customerSuggestion, lines);
        }
        catch (OperationCanceledException) { throw; }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
            OnPropertyChanged(nameof(CanApprove));
        }
    }

    private int? Remembered(string kind, string typed) =>
        _remembered.TryGetValue((kind, FormaterMatchDto.Normalize(typed)), out var m) ? m.TallyId : null;

    private async Task<List<CustomerSearchHitDto>> CustomerHitsAsync(string name, CancellationToken ct)
    {
        if (_customerHits.TryGetValue(name, out var hits)) return hits;
        hits = await _session.Api.SearchCustomersAsync(name, 6, ct);
        // A ledger chosen for this name before, but not among the closest matches: found by its own name.
        if (_remembered.TryGetValue((FormaterMatchDto.Customer, FormaterMatchDto.Normalize(name)), out var m) && hits.All(h => h.Customer.Id != m.TallyId))
            hits.AddRange((await _session.Api.SearchCustomersAsync(m.TallyName, 6, ct)).Where(h => h.Customer.Id == m.TallyId));
        return _customerHits[name] = hits;
    }

    private async Task<List<ProductSearchHitDto>> ProductHitsAsync(string name, CancellationToken ct)
    {
        if (_productHits.TryGetValue(name, out var hits)) return hits;
        hits = await _session.Api.SearchProductsAsync(name, 6, ct);
        if (_remembered.TryGetValue((FormaterMatchDto.Product, FormaterMatchDto.Normalize(name)), out var m) && hits.All(h => h.Product.Id != m.TallyId))
            hits.AddRange((await _session.Api.SearchProductsAsync(m.TallyName, 6, ct)).Where(h => h.Product.Id == m.TallyId));
        return _productHits[name] = hits;
    }

    /// <summary>
    /// Taken from Tally only when the search makes it clear (an exact name or part number, a single match of every word, or a
    /// decisive lead); otherwise kept as typed, with the closest Tally entry offered. The user's own choice wins, then the
    /// entry chosen for the same typing before (remembered on the server).
    /// </summary>
    private static (TItem? Chosen, TItem? Suggestion, bool Remembered) Decide<THit, TItem>(string typed, List<THit> hits, Func<THit, int> id,
        Func<THit, SearchMatchDto> match, Func<THit, double> score, Dictionary<string, int> choices, int? remembered, Func<THit, TItem> item)
        where TItem : class
    {
        if (choices.TryGetValue(typed, out var chosen))
        {
            if (chosen < 0) return (null, hits.Count > 0 ? item(hits[0]) : null, false);
            var pick = hits.FirstOrDefault(h => id(h) == chosen);
            if (pick is not null) return (item(pick), null, chosen == remembered);
        }
        if (remembered is { } r && hits.FirstOrDefault(h => id(h) == r) is { } known) return (item(known), null, true);
        if (hits.Count == 0) return (null, null, false);
        var exact = hits.Where(h => match(h).ExactName || match(h).ExactIdentifier).ToList();
        if (exact.Count == 1) return (item(exact[0]), null, false);
        var complete = hits.Where(h => match(h).AllTermsMatched && !match(h).UsedFuzzy).OrderByDescending(score).ToList();
        if (exact.Count == 0 && complete.Count == 1) return (item(complete[0]), null, false);
        if (exact.Count == 0 && complete.Count > 1 && score(complete[0]) >= 1.6 * Math.Max(0.01, score(complete[1])))
            return (item(complete[0]), null, false);
        return (null, item((exact.Count > 0 ? exact : complete.Count > 0 ? complete : hits)[0]), false);
    }

    private void Show(FormaterParse parse, CustomerSummaryDto? customer, CustomerSummaryDto? suggestion,
        List<(FormaterItem Item, ProductSummaryDto? Product, ProductSummaryDto? Suggestion, bool Remembered)> lines)
    {
        _parse = parse;
        _customer = customer;
        var company = _settings.Company.StateCode;
        var stateCode = customer?.StateCode ?? parse.StateCode;
        var interState = company.Length > 0 && stateCode.Length > 0 && company != stateCode;
        var withoutGst = parse.GstMode == FormaterGst.None;
        var typedRate = parse.GstPercent is > 0 and var g ? g : 18m;

        // Customer
        HasCustomer = parse.BuyerName.Length > 0;
        CustomerIsTally = customer is not null;
        CustomerName = customer is not null ? (customer.MailingName.Length > 0 ? customer.MailingName : customer.Name) : parse.BuyerName;
        var gstin = customer?.Gstin ?? parse.Gstin;
        var stateName = customer?.StateName ?? IndianStates.NameForCode(parse.StateCode) ?? "";
        CustomerDetails = string.Join(" · ", new[]
        {
            customer is not null ? customer.Address.Replace("\r", "").Replace("\n", ", ") : string.Join(", ", parse.Address),
            gstin.Length > 0 ? "GSTIN " + gstin : "",
            stateName.Length > 0 ? $"{stateName} ({stateCode})" : "",
            customer?.Phone ?? parse.Phone,
            customer?.Email ?? parse.Email,
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        CustomerSuggestion = customer is null && suggestion is not null ? suggestion.Name : "";
        OrderText = parse.OrderNo.Length == 0 ? "" : parse.OrderNo + (parse.OrderDate is { } od ? " dt. " + od.ToString("dd-MMM-yyyy") : "");
        OtherRefText = parse.OtherRef;
        DateText = (parse.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToString("dd-MMM-yyyy");

        // Items
        Lines.Clear();
        var no = 0;
        foreach (var (item, product, s, remembered) in lines)
        {
            var rate = item.Rate > 0 ? item.Rate : product?.Rate ?? 0;
            Lines.Add(new FormaterLineViewModel
            {
                No = ++no, Typed = item, Product = product, Suggestion = s, IsRemembered = remembered && product is not null,
                GstRate = withoutGst ? 0 : product?.GstRate ?? typedRate,
                Amount = QuotationCalculator.LineAmount(item.Qty, rate, item.DiscountPercent),
            });
        }
        Totals = Lines.Count == 0 ? null : QuotationCalculator.Calculate(new CalcInput(
            Lines.Select(l => new CalcLine(l.Typed.Qty, l.Rate, l.Typed.DiscountPercent, l.GstRate, l.Unit)).ToList(),
            PackingForwarding ?? 0, null, TaxPresentation.ComputeTax, _settings.Quotation.RoundOffTotal, interState, withoutGst));

        // GST and anything worth knowing
        GstNote = withoutGst ? "Without GST (set in the text)"
            : stateCode.Length == 0 ? "CGST + SGST: no state found for the customer (add the GSTIN or the state in the address)"
            : company.Length == 0 ? "CGST + SGST: our own state is not set (Settings → Company)"
            : interState ? $"IGST: {stateName} ({stateCode}) is another state than ours ({company})"
            : $"CGST + SGST: {stateName} ({stateCode}) is our state";
        Notes.Clear();
        if (parse.GstMode == FormaterGst.Igst && !interState && stateCode.Length > 0)
            Notes.Add("\"GST: IGST\" was asked, but the customer's state gives CGST + SGST: the state decides.");
        if (parse.GstMode == FormaterGst.CgstSgst && interState)
            Notes.Add("\"GST: CGST+SGST\" was asked, but the customer is in another state: IGST is used.");
        foreach (var l in Lines.Where(l => l.IsTyped))
            Notes.Add($"Item {l.No} is not in Tally: printed as typed, GST {l.GstRate:0.##}%" + (l.HasSuggestion ? $" (Tally has \"{l.Suggestion!.Name}\")." : "."));
        if (HasCustomer && customer is null)
            Notes.Add("The customer is not in Tally: printed as typed and kept only in this app." +
                      (CustomerSuggestion.Length > 0 ? $" Tally has \"{CustomerSuggestion}\"." : ""));
        if (parse.PaymentTerms.Length > 0 || parse.TermsAndConditions is not null)
            Notes.Add("Terms and T&C lines are not used here: the terms come from Settings → Company (change them on the quotation screen).");
        foreach (var w in parse.Warnings) Notes.Add(w.ToString());
        Problems.Clear();
        foreach (var e in parse.Errors) Problems.Add(new FormaterProblem(e.Line, e.Text));
        if (Text.Trim().Length == 0) Problems.Clear();
        OnPropertyChanged(nameof(ProblemLines));

        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(CustomerIsTyped));
        OnPropertyChanged(nameof(HasCustomerSuggestion));
        OnPropertyChanged(nameof(CustomerRememberedText));
        OnPropertyChanged(nameof(CanApprove));
    }

    partial void OnTotalsChanged(CalcResult? value)
    {
        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(TaxText));
        OnPropertyChanged(nameof(RoundOffText));
        OnPropertyChanged(nameof(GrandTotalText));
        OnPropertyChanged(nameof(TotalQuantityText));
        OnPropertyChanged(nameof(HasPacking));
        OnPropertyChanged(nameof(PackingText));
    }

    // ---------------------------------------------------------------- the user's choices

    [RelayCommand]
    private async Task UseCustomerSuggestionAsync()
    {
        if (_parse is null || !_customerHits.TryGetValue(_parse.BuyerName, out var hits) || hits.Count == 0) return;
        var s = hits.FirstOrDefault(h => h.Customer.Name == CustomerSuggestion) ?? hits[0];
        _customerChoice[_parse.BuyerName] = s.Customer.Id;
        await RememberAsync(FormaterMatchDto.Customer, _parse.BuyerName, s.Customer.Id, s.Customer.Name);
        await RefreshAsync(CancellationToken.None);
    }

    /// <summary>Remembers the choice on the server, so the same typing is matched the same way next time, on every PC.</summary>
    private async Task RememberAsync(string kind, string typed, int tallyId, string tallyName)
    {
        var match = new FormaterMatchDto(kind, FormaterMatchDto.Normalize(typed), tallyId, tallyName);
        _remembered[(kind, match.Typed)] = match;
        try { await _session.Api.RememberFormaterMatchAsync(match); }
        catch (ApiException) { /* remembered on this PC until the app closes */ }
    }

    private async Task ForgetAsync(string kind, string typed)
    {
        _remembered.Remove((kind, FormaterMatchDto.Normalize(typed)));
        try { await _session.Api.ForgetFormaterMatchAsync(kind, typed); }
        catch (ApiException ex) { ErrorMessage = ex.Message; }
    }

    /// <summary>"Forget" on a remembered customer: no longer matched by this typing (shown as typed now).</summary>
    [RelayCommand]
    private async Task ForgetCustomerAsync()
    {
        if (_parse is null) return;
        await ForgetAsync(FormaterMatchDto.Customer, _parse.BuyerName);
        _customerChoice[_parse.BuyerName] = -1;
        await RefreshAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task ForgetItemAsync(FormaterLineViewModel? line)
    {
        if (line is null) return;
        await ForgetAsync(FormaterMatchDto.Product, line.Typed.Name);
        _productChoice[line.Typed.Name] = -1;
        await RefreshAsync(CancellationToken.None);
    }

    [RelayCommand]
    private Task KeepCustomerTypedAsync()
    {
        if (_parse is null) return Task.CompletedTask;
        _customerChoice[_parse.BuyerName] = -1;
        return RefreshAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task UseItemSuggestionAsync(FormaterLineViewModel? line)
    {
        if (line?.Suggestion is null) return;
        _productChoice[line.Typed.Name] = line.Suggestion.Id;
        await RememberAsync(FormaterMatchDto.Product, line.Typed.Name, line.Suggestion.Id, line.Suggestion.Name);
        await RefreshAsync(CancellationToken.None);
    }

    [RelayCommand]
    private Task KeepItemTypedAsync(FormaterLineViewModel? line)
    {
        if (line is null) return Task.CompletedTask;
        _productChoice[line.Typed.Name] = -1;
        return RefreshAsync(CancellationToken.None);
    }

    // ---------------------------------------------------------------- actions

    /// <summary>The quotation as the app understood it (not saved; no number used yet).</summary>
    public QuotationDto ToQuotation()
    {
        var p = _parse ?? FormaterText.Parse(Text);
        var c = _customer;
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
        var preparedBy = string.IsNullOrWhiteSpace(_settings.Company.DefaultPreparedBy)
            ? _session.Api.CurrentUser?.DisplayName ?? ""
            : _settings.Company.DefaultPreparedBy;
        return new QuotationDto
        {
            Id = Guid.Empty, DocType = DocumentType.Quotation, Status = QuotationStatus.Draft, Source = QuotationSource.Formater,
            Date = p.Date ?? DateOnly.FromDateTime(DateTime.Today),
            CustomerId = c?.Id, Buyer = buyer, Consignee = new PartyDto(), ConsigneeSameAsBuyer = true,
            BuyersReference = p.OrderNo, BuyersReferenceDate = p.OrderDate, OtherReferences = p.OtherRef,
            // Terms and T&C as on any new quotation (Settings → Company); not taken from the text.
            PaymentTerms = _settings.Company.DefaultPaymentTerms, TermsOfDelivery = _settings.Company.DefaultTermsOfDelivery,
            TermsAndConditions = string.Join(Environment.NewLine, _settings.Company.TermsAndConditions),
            PreparedBy = preparedBy, VerifiedBy = _settings.Company.DefaultVerifiedBy,
            WithoutGst = p.GstMode == FormaterGst.None,
            PackingForwarding = PackingForwarding is > 0 and var pf ? pf : 0,
            Lines = Lines.Select(l => new QuotationLineDto
            {
                LineNo = l.No, ProductId = l.Product?.Id, ItemName = l.ItemName, PartNumber = l.PartNumber, Description = l.Description,
                Hsn = l.Hsn, GstRate = l.GstRate, Quantity = l.Typed.Qty, Unit = l.Unit, Rate = l.Rate, TallyRate = l.Product?.Rate,
                DiscountPercent = l.Typed.DiscountPercent, Amount = l.Amount,
            }).ToList(),
        };
    }

    private SaveQuotationRequest ToRequest()
    {
        var q = ToQuotation();
        return new SaveQuotationRequest
        {
            DocType = q.DocType, Date = q.Date, CustomerId = q.CustomerId, Buyer = q.Buyer, Consignee = q.Consignee,
            ConsigneeSameAsBuyer = true, BuyersReference = q.BuyersReference, BuyersReferenceDate = q.BuyersReferenceDate,
            OtherReferences = q.OtherReferences, PaymentTerms = q.PaymentTerms, TermsOfDelivery = q.TermsOfDelivery,
            TermsAndConditions = q.TermsAndConditions, PreparedBy = q.PreparedBy, VerifiedBy = q.VerifiedBy, WithoutGst = q.WithoutGst,
            PackingForwarding = q.PackingForwarding, FromFormater = true, Lines = q.Lines,
        };
    }

    /// <summary>Ctrl+E: on to the quotation screen, which approves it (next number, PDF in the Zoho layout).</summary>
    [RelayCommand]
    private void Approve()
    {
        if (!CanApprove) return;
        _navigator.OpenUnsavedQuotation(ToQuotation(), approve: true);
    }

    /// <summary>The quotation screen with everything filled in, for small changes before approving.</summary>
    [RelayCommand]
    private void OpenInEditor()
    {
        if (_parse is not { IsValid: true }) return;
        _navigator.OpenUnsavedQuotation(ToQuotation(), approve: false);
    }

    /// <summary>The PDF as it will look (DRAFT, provisional number); nothing is saved and no number is used.</summary>
    [RelayCommand]
    private Task PreviewAsync() => RunAsync(async () =>
    {
        if (_parse is not { IsValid: true }) return;
        var bytes = await _session.Api.PreviewUnsavedPdfAsync(ToRequest());
        _session.Documents.SaveAndOpen(bytes, "Preview-Formater.pdf");
    });

    /// <summary>Ctrl+P: the preview (DRAFT) straight to the default printer; nothing is saved and no number is used.</summary>
    [RelayCommand]
    private Task PrintAsync() => RunAsync(async () =>
    {
        if (_parse is not { IsValid: true }) return;
        var pdf = await _session.Api.PreviewUnsavedPdfAsync(ToRequest());
        try
        {
            var printer = await Task.Run(() => _session.Documents.Print(pdf, "DRAFT Formater"));
            _navigator.Toast($"Sent to printer {printer}.");
        }
        catch (Exception ex) when (ex is not ApiException)
        {
            ErrorMessage = "Could not print: " + ex.Message;
        }
    });

    [RelayCommand]
    private void Clear()
    {
        Text = "";
        PackingForwarding = null;
    }

    /// <summary>
    /// A paste into the text box: WhatsApp times and names and greeting lines are taken out (a note says what, with Undo).
    /// <paramref name="start"/>/<paramref name="length"/> is the selection the paste replaces. Returns the caret position after it.
    /// </summary>
    public int Paste(string pasted, int start, int length)
    {
        var current = Text ?? "";
        start = Math.Clamp(start, 0, current.Length);
        length = Math.Clamp(length, 0, current.Length - start);
        var clean = FormaterText.CleanPasted(pasted);
        var cleaned = clean.Text.Replace("\n", Environment.NewLine);
        var raw = current[..start] + pasted + current[(start + length)..];
        _settingText = true;
        try { Text = current[..start] + cleaned + current[(start + length)..]; }
        finally { _settingText = false; }
        if (clean.Removed > 0)
        {
            _pasteUndo = (current, raw);
            PasteNote = "Cleaned the pasted text: removed " + clean.Describe() + ".";
        }
        else
        {
            _pasteUndo = null;
            PasteNote = "";
        }
        return start + cleaned.Length;
    }

    /// <summary>Undo on the paste note: the text as it was pasted, nothing taken out.</summary>
    [RelayCommand]
    private void UndoPasteClean()
    {
        if (_pasteUndo is not { } u) return;
        _settingText = true;
        try { Text = u.Raw; }
        finally { _settingText = false; }
        _pasteUndo = null;
        PasteNote = "";
    }

    [RelayCommand]
    private void ClosePasteNote()
    {
        _pasteUndo = null;
        PasteNote = "";
    }

    [RelayCommand]
    private void InsertExample() => Text = Example.Replace("\n", Environment.NewLine);

    public void Dispose() => _refreshCts?.Cancel();
}
