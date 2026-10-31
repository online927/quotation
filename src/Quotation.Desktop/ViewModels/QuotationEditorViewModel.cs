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
public sealed partial class QuotationEditorViewModel : ViewModelBase, ITitledPage, IUnsavedChangesPage, IReturnablePage
{
    private readonly AppSession _session;
    private readonly INavigator _navigator;
    private AllSettingsDto _settings = new();
    private bool _loading;

    public QuotationEditorViewModel(AppSession session, INavigator navigator, Guid? id = null, DocumentType docType = DocumentType.Quotation)
    {
        _session = session;
        _navigator = navigator;
        _docType = docType;
        Lines.CollectionChanged += (_, _) => RecalculateTotals();
        ProductPopulator = SearchProductsAsync;
        CustomerPopulator = SearchCustomersAsync;
        _ = InitializeAsync(id);
    }

    // ---------------------------------------------------------------- header
    [ObservableProperty] private Guid? _id;
    [ObservableProperty] private DocumentType _docType;
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
    /// <summary>"Without GST": 0 % on all items, P&amp;F without GST, no tax lines (from the user's switch in My Settings).</summary>
    [ObservableProperty] private bool _withoutGst;
    [ObservableProperty] private DateOnly? _editableUntil;
    /// <summary>Tick next to "Save as PDF": put the company seal on the PDF (asks for the seal PIN).</summary>
    [ObservableProperty] private bool _sealPdf;
    [ObservableProperty] private string? _doNotUseWarning;

    // ---------------------------------------------------------------- customer
    [ObservableProperty] private int? _customerId;
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
    // Proforma invoice only (printed in the header grid like Tally's invoice).
    [ObservableProperty] private string _deliveryNote = "";
    [ObservableProperty] private string _dispatchDocNo = "";
    [ObservableProperty] private DateTime? _deliveryNoteDate;
    [ObservableProperty] private string _remarks = "";
    [ObservableProperty] private string _termsAndConditions = "";
    [ObservableProperty] private string _preparedBy = "";
    [ObservableProperty] private string _verifiedBy = "";

    // ---------------------------------------------------------------- lines
    public ObservableCollection<QuotationLineViewModel> Lines { get; } = [];
    [ObservableProperty] private QuotationLineViewModel? _selectedLine;

    // Line entry panel
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
    public ObservableCollection<RecentItemDto> RecentCustomers { get; } = [];
    public ObservableCollection<RecentItemDto> RecentProducts { get; } = [];
    public ObservableCollection<string> AuditTrail { get; } = [];
    public bool HasRecentCustomers => RecentCustomers.Count > 0 && CustomerId is null;
    public bool HasRecentProducts => RecentProducts.Count > 0;
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
    public bool ShowsTaxes => _settings.Quotation.TaxPresentation == TaxPresentation.ComputeTax && !WithoutGst;

    public bool IsAdmin => _session.IsAdmin;
    /// <summary>Older than the editing period (7 days by default) and the user is not an administrator.</summary>
    public bool IsLocked => !IsAdmin && Id is not null && EditableUntil is { } until && DateOnly.FromDateTime(DateTime.Today) > until;
    public string LockText => EditableUntil is { } until
        ? $"Locked — made more than {_settings.Quotation.UserEditDays} days ago (editable until {until:dd-MMM-yyyy}). Ask an administrator to change it, or use Duplicate."
        : "";
    public bool IsEditable => Status != QuotationStatus.Cancelled && !IsLocked;
    /// <summary>Only administrators change the date; for users it is the day the quotation is first saved (enforced by the server).</summary>
    public bool CanChangeDate => IsAdmin && IsEditable;
    public bool CanApprove => Status is QuotationStatus.Draft or QuotationStatus.PendingReview && Lines.Count > 0 && !IsLocked;
    public bool CanDelete => IsAdmin && Id is not null;
    /// <summary>This user may seal PDFs (administrator, or allowed for all users): the Seal tick-box is shown.</summary>
    public bool CanSeal => IsAdmin || _settings.Quotation.AllUsersCanSeal;
    /// <summary>A seal image is set up; without it the tick-box is shown greyed out with the reason.</summary>
    public bool SealAvailable => _settings.SealImageConfigured;
    public string SealHint => SealAvailable
        ? "Put the company seal on the PDF (asks for the 4-digit seal PIN)"
        : "No seal image yet — an administrator adds it in Settings → Company → Company seal";
    public bool CanSaveAsPdf => Status != QuotationStatus.Cancelled && (IsFinal || (Lines.Count > 0 && !IsLocked));
    public string PackingLabel => WithoutGst ? "Packing & Forwarding - Without GST ₹" : "Packing & Forwarding ₹";
    public bool IsFinal => Status is QuotationStatus.Approved or QuotationStatus.Generated;
    public bool IsPendingReview => Status == QuotationStatus.PendingReview;
    public bool IsCancelled => Status == QuotationStatus.Cancelled;
    public string StatusText => Status switch
    {
        QuotationStatus.PendingReview => "PENDING REVIEW",
        _ => Status.ToString().ToUpperInvariant(),
    };
    public bool IsProforma => DocType == DocumentType.ProformaInvoice;
    public string DocLabel => IsProforma ? "Proforma Invoice" : "Quotation";
    public string NumberLabel => IsProforma ? "Invoice No." : "Quotation No.";
    public string Title => Number is null ? "New " + DocLabel : $"{DocLabel} {Number}";
    /// <summary>A saved quotation can be turned into a proforma invoice (a new document with its own TSPI number).</summary>
    public bool CanConvertToProforma => !IsProforma && Id is not null && Status != QuotationStatus.Cancelled;
    /// <summary>Unsaved header/line changes, or a line typed in the entry row but not added yet.</summary>
    public bool HasUnsavedChanges => IsEditable && (IsDirty || EntryProduct is not null || EditingLine is not null);

    public Task<bool> SaveChangesAsync() => SaveAsync();

    /// <summary>Coming back (e.g. from the proforma made from this quotation): show the latest saved state.</summary>
    public void OnReturnedTo()
    {
        if (Id is { } id && !HasUnsavedChanges && !IsBusy) _ = RunAsync(async () => Load(await _session.Api.QuotationAsync(id)));
    }

    public string EntryButtonText => EditingLine is null ? "Add line (Enter)" : "Update line";

    // ================================================================= loading

    private async Task InitializeAsync(Guid? id)
    {
        await RunAsync(async () =>
        {
            // Always the current settings: an administrator may have changed them (e.g. added the seal) on another PC.
            _settings = await _session.GetSettingsAsync(refresh: true);
            Controls.TitleCase.IsOn = _settings.Quotation.TitleCase;
            OnPropertyChanged(nameof(ShowsTaxes));
            OnPropertyChanged(nameof(CanSeal));
            OnPropertyChanged(nameof(SealAvailable));
            OnPropertyChanged(nameof(SealHint));
            if (id is { } existing)
            {
                Load(await _session.Api.QuotationAsync(existing));
            }
            else
            {
                _loading = true;
                WithoutGst = _session.Api.CurrentUser?.WithoutGstNext == true;
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
            try
            {
                var recent = await _session.Api.RecentItemsAsync();
                foreach (var c in recent.Customers) RecentCustomers.Add(c);
                foreach (var p in recent.Products) RecentProducts.Add(p);
                OnPropertyChanged(nameof(HasRecentCustomers));
                OnPropertyChanged(nameof(HasRecentProducts));
            }
            catch (ApiException)
            {
                // Recent items are a convenience only.
            }
        });
    }

    private async Task RefreshNumberPreviewAsync()
    {
        if (Number is not null) return;
        try
        {
            var next = await _session.Api.NextNumberAsync(Date is null ? null : DateOnly.FromDateTime(Date.Value), DocType);
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
        DocType = q.DocType;
        WithoutGst = q.WithoutGst;
        EditableUntil = q.EditableUntil;
        DoNotUseWarning = q.DoNotUseItems.Count == 0 ? null
            : "Marked DNU (do not use) in Tally — replace before sending: " + string.Join(", ", q.DoNotUseItems);
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
        DeliveryNote = q.DeliveryNote;
        DispatchDocNo = q.DispatchDocNo;
        DeliveryNoteDate = q.DeliveryNoteDate?.ToDateTime(TimeOnly.MinValue);
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
        foreach (var l in q.Lines)
        {
            var line = QuotationLineViewModel.From(l);
            line.ZeroGst = q.WithoutGst;
            Lines.Add(line);
        }
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
        OnPropertyChanged(nameof(CanChangeDate));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(IsFinal));
        OnPropertyChanged(nameof(IsPendingReview));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanConvertToProforma));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(LockText));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanSaveAsPdf));
    }

    partial void OnWithoutGstChanged(bool value)
    {
        foreach (var l in Lines) l.ZeroGst = value;
        OnPropertyChanged(nameof(ShowsTaxes));
        OnPropertyChanged(nameof(PackingLabel));
        RecalculateTotals();
    }

    partial void OnEditableUntilChanged(DateOnly? value) => NotifyState();

    partial void OnDocTypeChanged(DocumentType value)
    {
        OnPropertyChanged(nameof(IsProforma));
        OnPropertyChanged(nameof(DocLabel));
        OnPropertyChanged(nameof(NumberLabel));
        NotifyState();
    }

    partial void OnIdChanged(Guid? value) => OnPropertyChanged(nameof(CanConvertToProforma));

    partial void OnStatusChanged(QuotationStatus value) => NotifyState();
    partial void OnCustomerIdChanged(int? value) => OnPropertyChanged(nameof(HasRecentCustomers));
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
                or nameof(DeliveryNote) or nameof(DispatchDocNo) or nameof(DeliveryNoteDate)
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

    /// <summary>A customer was picked from the search list.</summary>
    public Task<bool> PickCustomerAsync(int customerId) => RunAsync(() => SelectCustomerAsync(customerId));

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

    /// <summary>Where a suggested rate came from when Tally has no selling price for the item.</summary>
    public string EntryRateNoteText => EntryProduct?.RateNote is { Length: > 0 } note ? "Last rate: " + note : "";

    partial void OnEntryProductChanged(ProductSummaryDto? value) => OnPropertyChanged(nameof(EntryRateNoteText));

    public void SelectProduct(ProductSummaryDto p)
    {
        EntryProduct = p;
        EntryRate = p.Rate;
        if (EditingLine is null || EditingLine.ProductId != p.Id)
        {
            EntryDescription = p.Description;
        }
        SetProductTextSilently(p.Name);
    }

    private void SetProductTextSilently(string text)
    {
        var wasLoading = _loading;
        _loading = true;
        ProductSearchText = text;
        _loading = wasLoading;
    }

    partial void OnProductSearchTextChanged(string value)
    {
        // Typing over a picked product un-picks it, so a line is never added for a product the box no longer shows.
        if (_loading || EntryProduct is not { } p) return;
        if (!string.Equals(value.Trim(), p.Name.Trim(), StringComparison.OrdinalIgnoreCase)) EntryProduct = null;
    }

    [RelayCommand]
    private void CommitEntry()
    {
        ErrorMessage = null;
        if (EntryProduct is null)
        {
            ErrorMessage = "Select a product from the list: type part of its name, part number or alias, then press Enter (or click it).";
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
        line.ZeroGst = WithoutGst;
        if (EditingLine is null) Lines.Add(line);
        line.IsEditing = false;
        ClearEntry();
        MarkDirty();
        RecalculateTotals();
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanSaveAsPdf));
    }

    [RelayCommand]
    private void EditLine(QuotationLineViewModel? line)
    {
        line ??= SelectedLine;
        if (line is null) return;
        foreach (var l in Lines) l.IsEditing = l == line;
        EditingLine = line;
        EntryProduct = line.ProductId is null ? null : new ProductSummaryDto(line.ProductId.Value, line.ItemName, "", "", "", line.Unit, line.Hsn, line.GstRate, line.TallyRate, line.Description);
        SetProductTextSilently(line.ItemName);
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
        SetProductTextSilently("");
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
            company.Length > 0 && pos.Length > 0 && company != pos,
            WithoutGst));
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
        DocType = DocType,
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
        DeliveryNote = DeliveryNote,
        DispatchDocNo = DispatchDocNo,
        DeliveryNoteDate = DeliveryNoteDate is null ? null : DateOnly.FromDateTime(DeliveryNoteDate.Value),
        Remarks = Remarks,
        TermsAndConditions = TermsAndConditions,
        PackingForwarding = PackingForwarding ?? 0,
        PackingForwardingPercent = PackingForwardingPercent,
        PreparedBy = PreparedBy,
        VerifiedBy = VerifiedBy,
        WithoutGst = WithoutGst,
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
            var created = Id is null;
            var dto = created
                ? await _session.Api.CreateQuotationAsync(BuildRequest())
                : await _session.Api.UpdateQuotationAsync(Id!.Value, BuildRequest());
            Load(dto);
            // "Without GST" was for this one quotation: the server switched it off; keep the session in step.
            if (created && dto.WithoutGst && _session.Api.CurrentUser is { } me) _session.Api.UpdateCurrentUser(me with { WithoutGstNext = false });
            InfoMessage = $"Saved {dto.Number ?? "draft"} at {DateTime.Now:HH:mm}.";
        });
        return ok;
    }

    [RelayCommand]
    private async Task ApproveAsync() => await ApproveCoreAsync(overrideStale: false, openPdf: true);

    [RelayCommand]
    private async Task ApproveAnywayAsync() => await ApproveCoreAsync(overrideStale: true, openPdf: !_saveAsPdfAfterApproval);

    private bool _saveAsPdfAfterApproval;

    /// <summary>Approval with all checks. True when the quotation is approved afterwards.</summary>
    private async Task<bool> ApproveCoreAsync(bool overrideStale, bool openPdf)
    {
        if ((IsDirty || Id is null) && !await SaveAsync()) return false;
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
                if (openPdf) await OpenPdfCoreAsync();
            }
            else
            {
                InfoMessage = $"{dto.Number} approved.";
            }
            if (_saveAsPdfAfterApproval && overrideStale)
            {
                // "Approve anyway" answered the stale-data question raised by Save as PDF: finish saving the PDF.
                _saveAsPdfAfterApproval = false;
                await SavePdfFileAsync();
            }
            return true;
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
        return false;
    }

    // ================================================================= Save as PDF, seal, delete

    /// <summary>
    /// One click: a draft is approved first (all the usual checks), then the PDF is saved straight into the folder set in
    /// My Settings as "Number Customer.pdf" (replacing an earlier copy), optionally with the company seal.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsPdfAsync()
    {
        InfoMessage = null;
        if (!IsFinal || IsDirty)
        {
            _saveAsPdfAfterApproval = true;
            var approved = await ApproveCoreAsync(overrideStale: false, openPdf: false);
            if (!approved)
            {
                // Waiting for "Approve anyway" (stale Tally data) keeps the request; anything else cancels it.
                if (!NeedsStaleOverride) _saveAsPdfAfterApproval = false;
                return;
            }
            _saveAsPdfAfterApproval = false;
        }
        await SavePdfFileAsync();
    }

    private async Task SavePdfFileAsync()
    {
        if (Id is null) return;
        string? pin = null;
        if (SealPdf && CanSeal && SealAvailable)
        {
            pin = await _navigator.AskAsync("Company seal", $"Enter the 4-digit seal PIN to seal {Number}.", "Seal PIN", secret: true, ok: "Seal & Save");
            if (pin is null) return;
        }
        await RunAsync(async () =>
        {
            var bytes = pin is null ? await _session.Api.QuotationPdfAsync(Id.Value) : await _session.Api.SealedPdfAsync(Id.Value, pin.Trim());
            LastPdfPath = _session.Documents.Save(bytes, PdfFileName());
            InfoMessage = (pin is null ? "Saved to " : "Sealed and saved to ") + LastPdfPath;
        });
    }

    /// <summary>"TSQ2526-3403 TATA Electronics System Solutions.pdf": characters Windows does not allow become "-".</summary>
    public string PdfFileName() => PdfFileName(Number ?? "draft", BuyerName);

    private const string NotInFileNames = "\\/:*?\"<>|";

    public static string PdfFileName(string number, string customer)
    {
        var name = new string(customer.Select(c => char.IsControl(c) ? ' ' : NotInFileNames.Contains(c) ? '-' : c).ToArray()).Trim();
        while (name.Contains("  ", StringComparison.Ordinal)) name = name.Replace("  ", " ");
        if (name.Length > 60) name = name[..60].TrimEnd();
        var file = QuotationNumberFormatter.ToFileName(number);
        return (name.Length > 0 ? $"{file} {name}" : file).TrimEnd('.', ' ') + ".pdf";
    }

    [RelayCommand]
    private void OpenPdfFolder()
    {
        if (LastPdfPath is { } path) _session.Documents.ShowInFolder(path);
    }

    /// <summary>Administrator: moves the document to the Deleted list (restorable for 30 days).</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanDelete || Id is null) return;
        if (!await _navigator.ConfirmAsync("Delete", $"Delete {Number ?? "this draft"} ({BuyerName})? It goes to the Deleted list and can be restored for 30 days.",
                "Delete", "Cancel")) return;
        if (await RunAsync(() => _session.Api.DeleteQuotationAsync(Id.Value)))
        {
            ClearEntry();
            IsDirty = false;
            _navigator.GoBack();
        }
    }

    [RelayCommand]
    private void DismissStale() => NeedsStaleOverride = false;

    [RelayCommand]
    private Task OpenPdfAsync() => RunAsync(OpenPdfCoreAsync);

    /// <summary>Saves pending changes and opens a DRAFT-watermarked preview of the current quotation.</summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        if ((IsDirty || Id is null) && !await SaveAsync()) return;
        await RunAsync(async () =>
        {
            var bytes = await _session.Api.PreviewPdfAsync(Id!.Value);
            LastPdfPath = _session.Documents.SaveAndOpen(bytes, "Preview-" + QuotationNumberFormatter.ToFileName(Number ?? "draft") + ".pdf");
        });
    }

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

    /// <summary>Creates a proforma invoice from this quotation (own TSPI number) and opens it.</summary>
    [RelayCommand]
    private async Task ConvertToProformaAsync()
    {
        if (IsDirty && !await SaveAsync()) return;
        await RunAsync(async () =>
        {
            if (Id is null) return;
            var proforma = await _session.Api.ConvertToProformaAsync(Id.Value);
            _navigator.OpenQuotation(proforma.Id);
        });
    }

    [RelayCommand]
    private Task CancelQuotationAsync() => RunAsync(async () =>
    {
        if (Id is null)
        {
            // Nothing saved yet: throw the entry away and go back without asking.
            ClearEntry();
            IsDirty = false;
            _navigator.GoBack();
            return;
        }
        Load(await _session.Api.CancelQuotationAsync(Id.Value));
        InfoMessage = $"{Number} cancelled. The number is not reused.";
    });

    [RelayCommand]
    private void NewQuotation() => _navigator.Navigate(IsProforma ? "newproforma" : "new");

    [RelayCommand]
    private Task PickRecentCustomerAsync(RecentItemDto item) => RunAsync(async () =>
    {
        await SelectCustomerAsync(item.Id);
        OnPropertyChanged(nameof(HasRecentCustomers));
    });

    [RelayCommand]
    private Task PickRecentProductAsync(RecentItemDto item) => RunAsync(async () =>
    {
        var p = await _session.Api.ProductAsync(item.Id);
        const string last = "Last rate: ";
        SelectProduct(new ProductSummaryDto(p.Id, p.Name, p.PartNumber, p.Brand, p.StockGroup, p.Unit, p.Hsn, p.GstRate, p.Rate, p.Description,
            p.RateSource.StartsWith(last, StringComparison.Ordinal) ? p.RateSource[last.Length..] : ""));
    });

    [RelayCommand]
    private Task LoadAuditTrailAsync() => RunAsync(async () =>
    {
        AuditTrail.Clear();
        if (Id is null) return;
        foreach (var a in await _session.Api.QuotationAuditAsync(Id.Value))
        {
            AuditTrail.Add($"{Formatting.LocalDateTime(a.AtUtc)}  {a.Action}  by {a.User} ({a.Machine})  {a.Details}".TrimEnd());
        }
    });
}
