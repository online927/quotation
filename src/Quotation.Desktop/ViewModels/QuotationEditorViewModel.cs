using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Core.Numbering;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed record ShipToOption(string Label, AddressDto? Address, bool SameAsBuyer);

/// <summary>
/// The quotation entry screen. Totals are calculated locally with the same engine the server uses,
/// so they update while typing; the server recalculates authoritatively on every save.
/// </summary>
public sealed partial class QuotationEditorViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly INavigator _navigator;
    private AllSettingsDto _settings = new();
    private bool _loading;

    public QuotationEditorViewModel(AppSession session, INavigator navigator, Guid? id = null)
    {
        _session = session;
        _navigator = navigator;
        Lines.CollectionChanged += (_, _) => RecalculateTotals();
        ProductPopulator = SearchProductsAsync;
        CustomerPopulator = SearchCustomersAsync;
        _ = InitializeAsync(id);
    }

    // ---------------------------------------------------------------- header
    [ObservableProperty] private Guid? _id;
    [ObservableProperty] private string? _number;
    [ObservableProperty] private string _numberText = "(assigned on save)";
    [ObservableProperty] private QuotationStatus _status = QuotationStatus.Draft;
    [ObservableProperty] private QuotationSource _source = QuotationSource.Manual;
    [ObservableProperty] private int _revision;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _hasPdf;
    [ObservableProperty] private string? _pdfError;
    [ObservableProperty] private string? _dataWarning;
    [ObservableProperty] private string? _lastPdfPath;

    // ---------------------------------------------------------------- customer
    [ObservableProperty] private int? _customerId;
    [ObservableProperty] private CustomerSearchHitDto? _selectedCustomerHit;
    [ObservableProperty] private string _customerSearchText = "";
    [ObservableProperty] private string _buyerName = "";
    [ObservableProperty] private string _buyerAddress = "";
    [ObservableProperty] private string _buyerGstin = "";
    [ObservableProperty] private string _buyerState = "";
    [ObservableProperty] private string _buyerStateCode = "";
    [ObservableProperty] private string _buyerContact = "";
    [ObservableProperty] private string _buyerPhone = "";
    [ObservableProperty] private string _buyerEmail = "";

    public string BuyerStateDisplay => BuyerStateCode.Length > 0 ? $"{BuyerState}, Code: {BuyerStateCode}" : BuyerState;
    partial void OnBuyerStateChanged(string value) => OnPropertyChanged(nameof(BuyerStateDisplay));
    partial void OnBuyerStateCodeChanged(string value) => OnPropertyChanged(nameof(BuyerStateDisplay));

    public ObservableCollection<ShipToOption> ShipToOptions { get; } = [new("Same as buyer", null, true)];
    [ObservableProperty] private ShipToOption? _selectedShipTo;
    [ObservableProperty] private bool _consigneeSameAsBuyer = true;
    [ObservableProperty] private string _consigneeName = "";
    [ObservableProperty] private string _consigneeAddress = "";
    [ObservableProperty] private string _consigneeGstin = "";
    [ObservableProperty] private string _consigneeState = "";
    [ObservableProperty] private string _consigneeStateCode = "";

    // ---------------------------------------------------------------- references
    [ObservableProperty] private string _buyersReference = "";
    [ObservableProperty] private DateTime? _buyersReferenceDate;
    [ObservableProperty] private string _dispatchedThrough = "";
    [ObservableProperty] private string _paymentTerms = "";
    [ObservableProperty] private string _otherReferences = "";
    [ObservableProperty] private string _destination = "";
    [ObservableProperty] private string _termsOfDelivery = "";
    [ObservableProperty] private string _remarks = "";
    [ObservableProperty] private string _termsAndConditions = "";
    [ObservableProperty] private string _preparedBy = "";
    [ObservableProperty] private string _verifiedBy = "";

    // ---------------------------------------------------------------- lines
    public ObservableCollection<QuotationLineViewModel> Lines { get; } = [];
    [ObservableProperty] private QuotationLineViewModel? _selectedLine;

    // Line entry panel
    [ObservableProperty] private ProductSearchHitDto? _selectedProductHit;
    [ObservableProperty] private string _productSearchText = "";
    [ObservableProperty] private ProductSummaryDto? _entryProduct;
    [ObservableProperty] private decimal? _entryQuantity = 1;
    [ObservableProperty] private decimal? _entryRate;
    [ObservableProperty] private decimal? _entryDiscount = 0;
    [ObservableProperty] private string _entryDescription = "";
    [ObservableProperty] private string _entryDueOn = "";
    [ObservableProperty] private QuotationLineViewModel? _editingLine;

    public Func<string?, CancellationToken, Task<IEnumerable<object>>> ProductPopulator { get; }
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> CustomerPopulator { get; }

    // ---------------------------------------------------------------- totals
    [ObservableProperty] private decimal? _packingForwarding = 0;
    [ObservableProperty] private decimal? _packingForwardingPercent;
    [ObservableProperty] private CalcResult? _totals;
    public ObservableCollection<string> ValidationMessages { get; } = [];
    [ObservableProperty] private bool _needsStaleOverride;
    [ObservableProperty] private string? _staleMessage;

    public string SubtotalText => IndianFormat.Amount(Totals?.Subtotal ?? 0);
    public string PackingText => IndianFormat.Amount(Totals?.PackingForwarding ?? 0);
    public string TaxText => Totals is null || Totals.Taxes.Count == 0 ? "" :
        string.Join("   ", Totals.Taxes.Select(t => $"{t.Name} {t.RatePercent:0.##}%: {IndianFormat.Amount(t.Amount)}"));
    public string RoundOffText => Totals is null || Totals.RoundOff == 0 ? "" : IndianFormat.Amount(Totals.RoundOff);
    public string GrandTotalText => "₹ " + IndianFormat.Amount(Totals?.GrandTotal ?? 0);
    public string TotalQuantityText => Totals is null ? "" : $"{IndianFormat.Quantity(Totals.TotalQuantity)} {Totals.TotalQuantityUnit}".Trim();
    public string AmountInWords => IndianFormat.AmountInWords(Totals?.GrandTotal ?? 0, _settings.Quotation.CurrencyWordsPrefix);
    public bool ShowsTaxes => _settings.Quotation.TaxPresentation == TaxPresentation.ComputeTax;

    public bool IsEditable => Status != QuotationStatus.Cancelled;
    public bool CanApprove => Status is QuotationStatus.Draft or QuotationStatus.PendingReview && Lines.Count > 0;
    public bool IsFinal => Status is QuotationStatus.Approved or QuotationStatus.Generated;
    public string StatusText => Status switch
    {
        QuotationStatus.PendingReview => "PENDING REVIEW",
        _ => Status.ToString().ToUpperInvariant(),
    };
    public string Title => Number is null ? "New Quotation" : $"Quotation {Number}";
    public string EntryButtonText => EditingLine is null ? "Add line (Enter)" : "Update line";

    // ================================================================= loading

    private async Task InitializeAsync(Guid? id)
    {
        await RunAsync(async () =>
        {
            _settings = await _session.GetSettingsAsync();
            OnPropertyChanged(nameof(ShowsTaxes));
            if (id is { } existing)
            {
                Load(await _session.Api.QuotationAsync(existing));
            }
            else
            {
                _loading = true;
                PaymentTerms = _settings.Company.DefaultPaymentTerms;
                TermsOfDelivery = _settings.Company.DefaultTermsOfDelivery;
                TermsAndConditions = string.Join(Environment.NewLine, _settings.Company.TermsAndConditions);
                PreparedBy = string.IsNullOrWhiteSpace(_settings.Company.DefaultPreparedBy)
                    ? _session.Api.CurrentUser?.DisplayName ?? ""
                    : _settings.Company.DefaultPreparedBy;
                VerifiedBy = _settings.Company.DefaultVerifiedBy;
                PackingForwarding = _settings.Quotation.DefaultPackingForwardingAmount;
                PackingForwardingPercent = _settings.Quotation.DefaultPackingForwardingPercent > 0 ? _settings.Quotation.DefaultPackingForwardingPercent : null;
                _loading = false;
                await RefreshNumberPreviewAsync();
                RecalculateTotals();
                IsDirty = false;
            }
        });
    }

    private async Task RefreshNumberPreviewAsync()
    {
        if (Number is not null) return;
        try
        {
            var next = await _session.Api.NextNumberAsync(Date is null ? null : DateOnly.FromDateTime(Date.Value));
            NumberText = $"{next.Number} (provisional)";
        }
        catch (ApiException ex)
        {
            NumberText = "(" + ex.Message + ")";
        }
    }

    public void Load(QuotationDto q)
    {
        _loading = true;
        Id = q.Id;
        Number = q.Number;
        NumberText = q.Number ?? "(assigned on save)";
        Status = q.Status;
        Source = q.Source;
        Revision = q.Revision;
        Date = q.Date.ToDateTime(TimeOnly.MinValue);
        CustomerId = q.CustomerId;
        CustomerSearchText = q.Buyer.Name;
        BuyerName = q.Buyer.Name;
        BuyerAddress = q.Buyer.Address;
        BuyerGstin = q.Buyer.Gstin;
        BuyerState = q.Buyer.StateName;
        BuyerStateCode = q.Buyer.StateCode;
        BuyerContact = q.Buyer.ContactPerson;
        BuyerPhone = q.Buyer.Phone;
        BuyerEmail = q.Buyer.Email;
        ConsigneeSameAsBuyer = q.ConsigneeSameAsBuyer;
        ConsigneeName = q.Consignee.Name;
        ConsigneeAddress = q.Consignee.Address;
        ConsigneeGstin = q.Consignee.Gstin;
        ConsigneeState = q.Consignee.StateName;
        ConsigneeStateCode = q.Consignee.StateCode;
        BuyersReference = q.BuyersReference;
        BuyersReferenceDate = q.BuyersReferenceDate?.ToDateTime(TimeOnly.MinValue);
        DispatchedThrough = q.DispatchedThrough;
        PaymentTerms = q.PaymentTerms;
        OtherReferences = q.OtherReferences;
        Destination = q.Destination;
        TermsOfDelivery = q.TermsOfDelivery;
        Remarks = q.Remarks;
        TermsAndConditions = q.TermsAndConditions;
        PreparedBy = q.PreparedBy;
        VerifiedBy = q.VerifiedBy;
        PackingForwarding = q.PackingForwardingPercent is > 0 ? 0 : q.PackingForwarding;
        PackingForwardingPercent = q.PackingForwardingPercent;
        HasPdf = q.HasPdf;
        PdfError = q.PdfError;
        DataWarning = q.DataFreshnessWarning;
        Lines.Clear();
        foreach (var l in q.Lines) Lines.Add(QuotationLineViewModel.From(l));
        ClearEntry();
        _loading = false;
        if (CustomerId is { } cid) _ = LoadShipToOptionsAsync(cid, keepCurrent: true);
        RecalculateTotals();
        IsDirty = false;
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(IsFinal));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Title));
    }

    partial void OnStatusChanged(QuotationStatus value) => NotifyState();
    partial void OnNumberChanged(string? value) => OnPropertyChanged(nameof(Title));
    partial void OnEditingLineChanged(QuotationLineViewModel? value) => OnPropertyChanged(nameof(EntryButtonText));
    partial void OnDateChanged(DateTime? value)
    {
        if (!_loading) _ = RefreshNumberPreviewAsync();
        MarkDirty();
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading) return;
        switch (e.PropertyName)
        {
            case nameof(BuyerName) or nameof(BuyerAddress) or nameof(BuyerContact) or nameof(BuyerPhone) or nameof(BuyerEmail)
                or nameof(ConsigneeName) or nameof(ConsigneeAddress) or nameof(ConsigneeGstin) or nameof(ConsigneeState)
                or nameof(BuyersReference) or nameof(BuyersReferenceDate) or nameof(DispatchedThrough) or nameof(PaymentTerms)
                or nameof(OtherReferences) or nameof(Destination) or nameof(TermsOfDelivery) or nameof(Remarks)
                or nameof(TermsAndConditions) or nameof(PreparedBy) or nameof(VerifiedBy):
                MarkDirty();
                break;
            case nameof(PackingForwarding) or nameof(PackingForwardingPercent) or nameof(ConsigneeStateCode) or nameof(ConsigneeSameAsBuyer):
                MarkDirty();
                RecalculateTotals();
                break;
        }
    }

    private void MarkDirty()
    {
        if (!_loading) IsDirty = true;
    }

    // ================================================================= customer

    private async Task<IEnumerable<object>> SearchCustomersAsync(string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try { return await _session.Api.SearchCustomersAsync(text, 15, ct); }
        catch (Exception) { return []; }
    }

    partial void OnSelectedCustomerHitChanged(CustomerSearchHitDto? value)
    {
        if (value is null || _loading || value.Customer.Id == CustomerId) return;
        _ = RunAsync(() => SelectCustomerAsync(value.Customer.Id));
    }

    public async Task SelectCustomerAsync(int customerId)
    {
        var c = await _session.Api.CustomerAsync(customerId);
        CustomerId = c.Id;
        CustomerSearchText = c.Name;
        BuyerName = c.MailingName.Length > 0 ? c.MailingName : c.Name;
        BuyerAddress = string.Join(Environment.NewLine, c.AddressLines.Concat(string.IsNullOrEmpty(c.Pincode) ? [] : [$"PIN: {c.Pincode}"]));
        BuyerGstin = c.Gstin;
        BuyerState = c.StateName;
        BuyerStateCode = c.StateCode;
        BuyerContact = c.ContactPerson;
        BuyerPhone = c.Mobile.Length > 0 ? c.Mobile : c.Phone;
        BuyerEmail = c.Email;
        await LoadShipToOptionsAsync(customerId, keepCurrent: false, detail: c);
        MarkDirty();
        RecalculateTotals();
    }

    private async Task LoadShipToOptionsAsync(int customerId, bool keepCurrent, CustomerDetailDto? detail = null)
    {
        try
        {
            detail ??= await _session.Api.CustomerAsync(customerId);
        }
        catch (ApiException) { return; }
        var wasLoading = _loading;
        _loading = true;
        ShipToOptions.Clear();
        ShipToOptions.Add(new ShipToOption("Same as buyer", null, true));
        foreach (var a in detail.ShipToAddresses) ShipToOptions.Add(new ShipToOption(a.Name, a, false));
        ShipToOptions.Add(new ShipToOption("Other (type below)", null, false));
        SelectedShipTo = keepCurrent && !ConsigneeSameAsBuyer ? ShipToOptions[^1] : ShipToOptions[0];
        _loading = wasLoading;
        if (!keepCurrent) ApplyShipTo(SelectedShipTo);
    }

    partial void OnSelectedShipToChanged(ShipToOption? value)
    {
        if (!_loading) ApplyShipTo(value);
    }

    private void ApplyShipTo(ShipToOption? option)
    {
        if (option is null) return;
        ConsigneeSameAsBuyer = option.SameAsBuyer;
        if (option.Address is { } a)
        {
            ConsigneeName = a.Name;
            ConsigneeAddress = string.Join(Environment.NewLine, a.Lines.Concat(string.IsNullOrEmpty(a.Pincode) ? [] : [$"PIN: {a.Pincode}"]));
            ConsigneeState = a.StateName;
            ConsigneeStateCode = a.StateCode;
            ConsigneeGstin = "";
        }
        RecalculateTotals();
    }

    // ================================================================= line entry

    private async Task<IEnumerable<object>> SearchProductsAsync(string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try { return await _session.Api.SearchProductsAsync(text, 25, ct); }
        catch (Exception) { return []; }
    }

    partial void OnSelectedProductHitChanged(ProductSearchHitDto? value)
    {
        if (value is null) return;
        SelectProduct(value.Product);
    }

    public void SelectProduct(ProductSummaryDto p)
    {
        EntryProduct = p;
        EntryRate = p.Rate;
        if (EditingLine is null || EditingLine.ProductId != p.Id)
        {
            EntryDescription = p.Description;
        }
    }

    [RelayCommand]
    private void CommitEntry()
    {
        ErrorMessage = null;
        if (EntryProduct is null && EditingLine?.ProductId is null)
        {
            ErrorMessage = "Select a product from the list (type part of its name, part number or alias).";
            return;
        }
        if (EntryQuantity is null or <= 0)
        {
            ErrorMessage = "Enter a quantity greater than zero.";
            return;
        }
        var line = EditingLine ?? new QuotationLineViewModel { LineNo = Lines.Count + 1 };
        if (EntryProduct is { } p && (line.ProductId != p.Id))
        {
            line.ProductId = p.Id;
            line.ItemName = p.Name;
            line.Hsn = p.Hsn;
            line.GstRate = p.GstRate;
            line.Unit = p.Unit;
            line.TallyRate = p.Rate;
        }
        line.Quantity = EntryQuantity.Value;
        line.Rate = EntryRate ?? 0;
        line.DiscountPercent = EntryDiscount ?? 0;
        line.Description = EntryDescription.Trim();
        line.DueOn = EntryDueOn.Trim();
        if (EditingLine is null) Lines.Add(line);
        line.IsEditing = false;
        ClearEntry();
        MarkDirty();
        RecalculateTotals();
        OnPropertyChanged(nameof(CanApprove));
    }

    [RelayCommand]
    private void EditLine(QuotationLineViewModel? line)
    {
        line ??= SelectedLine;
        if (line is null) return;
        foreach (var l in Lines) l.IsEditing = l == line;
        EditingLine = line;
        EntryProduct = line.ProductId is null ? null : new ProductSummaryDto(line.ProductId.Value, line.ItemName, "", "", "", line.Unit, line.Hsn, line.GstRate, line.TallyRate, line.Description);
        _loading = true;
        ProductSearchText = line.ItemName;
        _loading = false;
        EntryQuantity = line.Quantity;
        EntryRate = line.Rate;
        EntryDiscount = line.DiscountPercent;
        EntryDescription = line.Description;
        EntryDueOn = line.DueOn;
    }

    [RelayCommand]
    private void RemoveLine(QuotationLineViewModel? line)
    {
        line ??= SelectedLine;
        if (line is null) return;
        Lines.Remove(line);
        Renumber();
        if (EditingLine == line) ClearEntry();
        MarkDirty();
        OnPropertyChanged(nameof(CanApprove));
    }

    [RelayCommand]
    private void MoveLineUp(QuotationLineViewModel? line)
    {
        line ??= SelectedLine;
        if (line is null) return;
        var i = Lines.IndexOf(line);
        if (i <= 0) return;
        Lines.Move(i, i - 1);
        Renumber();
        MarkDirty();
    }

    [RelayCommand]
    private void CancelEntry() => ClearEntry();

    private void ClearEntry()
    {
        foreach (var l in Lines) l.IsEditing = false;
        EditingLine = null;
        EntryProduct = null;
        SelectedProductHit = null;
        ProductSearchText = "";
        EntryQuantity = 1;
        EntryRate = null;
        EntryDiscount = 0;
        EntryDescription = "";
        EntryDueOn = "";
    }

    private void Renumber()
    {
        for (var i = 0; i < Lines.Count; i++) Lines[i].LineNo = i + 1;
    }

    // ================================================================= totals

    public void RecalculateTotals()
    {
        var company = _settings.Company.StateCode;
        var pos = !ConsigneeSameAsBuyer && ConsigneeStateCode.Length > 0 ? ConsigneeStateCode : BuyerStateCode;
        Totals = QuotationCalculator.Calculate(new CalcInput(
            Lines.Select(l => new CalcLine(l.Quantity, l.Rate, l.DiscountPercent, l.GstRate ?? 0, l.Unit)).ToList(),
            PackingForwarding ?? 0,
            PackingForwardingPercent,
            _settings.Quotation.TaxPresentation,
            _settings.Quotation.RoundOffTotal,
            company.Length > 0 && pos.Length > 0 && company != pos));
    }

    partial void OnTotalsChanged(CalcResult? value)
    {
        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(PackingText));
        OnPropertyChanged(nameof(TaxText));
        OnPropertyChanged(nameof(RoundOffText));
        OnPropertyChanged(nameof(GrandTotalText));
        OnPropertyChanged(nameof(TotalQuantityText));
        OnPropertyChanged(nameof(AmountInWords));
    }

    // ================================================================= commands

    public SaveQuotationRequest BuildRequest() => new()
    {
        Revision = Revision,
        Date = DateOnly.FromDateTime(Date ?? DateTime.Today),
        CustomerId = CustomerId,
        Buyer = new PartyDto
        {
            Name = BuyerName, Address = BuyerAddress, Gstin = BuyerGstin, StateName = BuyerState, StateCode = BuyerStateCode,
            ContactPerson = BuyerContact, Phone = BuyerPhone, Email = BuyerEmail,
        },
        Consignee = new PartyDto
        {
            Name = ConsigneeName, Address = ConsigneeAddress, Gstin = ConsigneeGstin, StateName = ConsigneeState, StateCode = ConsigneeStateCode,
        },
        ConsigneeSameAsBuyer = ConsigneeSameAsBuyer,
        BuyersReference = BuyersReference,
        BuyersReferenceDate = BuyersReferenceDate is null ? null : DateOnly.FromDateTime(BuyersReferenceDate.Value),
        DispatchedThrough = DispatchedThrough,
        PaymentTerms = PaymentTerms,
        OtherReferences = OtherReferences,
        Destination = Destination,
        TermsOfDelivery = TermsOfDelivery,
        Remarks = Remarks,
        TermsAndConditions = TermsAndConditions,
        PackingForwarding = PackingForwarding ?? 0,
        PackingForwardingPercent = PackingForwardingPercent,
        PreparedBy = PreparedBy,
        VerifiedBy = VerifiedBy,
        Lines = Lines.Select(l => l.ToDto()).ToList(),
    };

    [RelayCommand]
    public async Task<bool> SaveAsync()
    {
        if (EntryProduct is not null || EditingLine is not null) CommitEntry();
        InfoMessage = null;
        ValidationMessages.Clear();
        var ok = await RunAsync(async () =>
        {
            var dto = Id is null
                ? await _session.Api.CreateQuotationAsync(BuildRequest())
                : await _session.Api.UpdateQuotationAsync(Id.Value, BuildRequest());
            Load(dto);
            InfoMessage = $"Saved {dto.Number ?? "draft"} at {DateTime.Now:HH:mm}.";
        });
        return ok;
    }

    [RelayCommand]
    private async Task ApproveAsync() => await ApproveCoreAsync(overrideStale: false);

    [RelayCommand]
    private async Task ApproveAnywayAsync() => await ApproveCoreAsync(overrideStale: true);

    private async Task ApproveCoreAsync(bool overrideStale)
    {
        if ((IsDirty || Id is null) && !await SaveAsync()) return;
        ValidationMessages.Clear();
        NeedsStaleOverride = false;
        InfoMessage = null;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var dto = await _session.Api.ApproveQuotationAsync(Id!.Value, overrideStale, Revision);
            Load(dto);
            if (dto.HasPdf)
            {
                InfoMessage = $"{dto.Number} approved and PDF generated.";
                await OpenPdfCoreAsync();
            }
            else
            {
                InfoMessage = $"{dto.Number} approved.";
            }
        }
        catch (ApiException ex) when (ex.Code == ApiErrorCodes.Validation)
        {
            ErrorMessage = ex.Message;
            foreach (var d in ex.Details) ValidationMessages.Add(d);
        }
        catch (ApiException ex) when (ex.Code == ApiErrorCodes.StaleData)
        {
            StaleMessage = ex.Message;
            NeedsStaleOverride = _settings.Quotation.AllowStaleDataOverride;
            ErrorMessage = NeedsStaleOverride ? null : ex.Message;
        }
        catch (ApiException ex) when (ex.Code == ApiErrorCodes.RatesChanged)
        {
            Load(await _session.Api.QuotationAsync(Id!.Value));
            ErrorMessage = ex.Message;
            foreach (var d in ex.Details) ValidationMessages.Add(d);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.FullMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DismissStale() => NeedsStaleOverride = false;

    [RelayCommand]
    private Task OpenPdfAsync() => RunAsync(OpenPdfCoreAsync);

    private async Task OpenPdfCoreAsync()
    {
        if (Id is null) return;
        var bytes = await _session.Api.QuotationPdfAsync(Id.Value);
        LastPdfPath = _session.Documents.SaveAndOpen(bytes, QuotationNumberFormatter.ToFileName(Number ?? Id.ToString()!) + ".pdf");
    }

    [RelayCommand]
    private Task RegeneratePdfAsync() => RunAsync(async () =>
    {
        Load(await _session.Api.RegeneratePdfAsync(Id!.Value));
        if (HasPdf) await OpenPdfCoreAsync();
    });

    [RelayCommand]
    private Task DuplicateAsync() => RunAsync(async () =>
    {
        if (Id is null) return;
        var copy = await _session.Api.DuplicateQuotationAsync(Id.Value);
        _navigator.OpenQuotation(copy.Id);
    });

    [RelayCommand]
    private Task CancelQuotationAsync() => RunAsync(async () =>
    {
        if (Id is null)
        {
            _navigator.Navigate("dashboard");
            return;
        }
        Load(await _session.Api.CancelQuotationAsync(Id.Value));
        InfoMessage = $"{Number} cancelled. The number is not reused.";
    });

    [RelayCommand]
    private void NewQuotation() => _navigator.NewQuotation();
}
