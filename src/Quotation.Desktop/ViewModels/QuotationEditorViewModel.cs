using System.Collections.ObjectModel;
using System.Globalization;
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
public sealed partial class QuotationEditorViewModel : ViewModelBase, ITitledPage, IUnsavedChangesPage, IReturnablePage, IDisposable
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
        _recoveryTimer = new Avalonia.Threading.DispatcherTimer { Interval = RecoveryInterval };
        _recoveryTimer.Tick += (_, _) => KeepRecoveryCopy();
        _recoveryTimer.Start();
        _presenceTimer = new Avalonia.Threading.DispatcherTimer { Interval = PresenceInterval };
        _presenceTimer.Tick += (_, _) => _ = CheckPresenceAsync();
        _presenceTimer.Start();
        _ = InitializeAsync(id);
    }

    /// <summary>Restores unsaved work kept on this PC after a crash or power cut (no number is used until it is saved).</summary>
    public QuotationEditorViewModel(AppSession session, INavigator navigator, RecoverySnapshot restore)
        : this(session, navigator, restore.Quotation.Id == Guid.Empty ? null : restore.Quotation.Id, restore.Quotation.DocType)
    {
        _restore = restore.Quotation;
        _ownsRecovery = true; // this screen now holds that work: saving or discarding it removes the copy
    }

    // ---------------------------------------------------------------- unsaved work kept on this PC

    /// <summary>How often unsaved work is copied to the PC (tests make it shorter).</summary>
    public static TimeSpan RecoveryInterval { get; set; } = TimeSpan.FromSeconds(30);
    public static TimeSpan PresenceInterval { get; set; } = TimeSpan.FromSeconds(15);

    private readonly Avalonia.Threading.DispatcherTimer _recoveryTimer;
    private readonly Avalonia.Threading.DispatcherTimer _presenceTimer;
    private QuotationDto? _restore;
    private QuotationDto? _loaded;
    private bool _ownsRecovery;
    private string? RecoveryUser => _session.Api.CurrentUser?.Username;

    /// <summary>Copies unsaved work to the PC (every 30 s); removes the copy once there is nothing unsaved.</summary>
    public void KeepRecoveryCopy()
    {
        if (RecoveryUser is not { } user) return;
        if (IsDirty && IsEditable && (Lines.Count > 0 || CustomerId is not null))
        {
            RecoveryStore.For(_session.Config).Save(new RecoverySnapshot(user, DateTime.Now, ToSnapshot()));
            _ownsRecovery = true;
        }
        else if (!HasUnsavedChanges)
        {
            DropRecoveryCopy();
        }
    }

    private void DropRecoveryCopy()
    {
        if (!_ownsRecovery || RecoveryUser is not { } user) return;
        RecoveryStore.For(_session.Config).Delete(user);
        _ownsRecovery = false;
    }

    /// <summary>The quotation as it is on screen (saved details plus the unsaved changes).</summary>
    public QuotationDto ToSnapshot()
    {
        var r = BuildRequest();
        return new QuotationDto
        {
            Id = Id ?? Guid.Empty, Number = Number, DocType = DocType, Status = Status, Source = Source, Revision = Revision,
            Date = r.Date, CustomerId = r.CustomerId, Buyer = r.Buyer, Consignee = r.Consignee, ConsigneeSameAsBuyer = r.ConsigneeSameAsBuyer,
            BuyersReference = r.BuyersReference, BuyersReferenceDate = r.BuyersReferenceDate, DispatchedThrough = r.DispatchedThrough,
            PaymentTerms = r.PaymentTerms, OtherReferences = r.OtherReferences, Destination = r.Destination, TermsOfDelivery = r.TermsOfDelivery,
            DeliveryNote = r.DeliveryNote, DispatchDocNo = r.DispatchDocNo, DeliveryNoteDate = r.DeliveryNoteDate, Remarks = r.Remarks,
            TermsAndConditions = r.TermsAndConditions, PackingForwarding = r.PackingForwarding, PackingForwardingPercent = r.PackingForwardingPercent,
            PreparedBy = r.PreparedBy, VerifiedBy = r.VerifiedBy, WithoutGst = r.WithoutGst, Lines = r.Lines,
            HasPdf = HasPdf, PdfError = PdfError, DataFreshnessWarning = DataWarning, EditableUntil = EditableUntil ?? default,
            CreatedBy = _loaded?.CreatedBy ?? "", CreatedUtc = _loaded?.CreatedUtc ?? default, UpdatedBy = _loaded?.UpdatedBy,
            UpdatedUtc = _loaded?.UpdatedUtc, ApprovedBy = _loaded?.ApprovedBy, ApprovedUtc = _loaded?.ApprovedUtc,
        };
    }

    /// <summary>Puts a kept version (recovered after a crash, or "my changes" after a save clash) on screen as unsaved changes.</summary>
    private void ApplyUnsaved(QuotationDto version, int revision)
    {
        var copy = version;
        copy.Revision = revision;
        Load(copy);
        if (copy.Id == Guid.Empty) Id = null;
        IsDirty = true;
        NotifyState();
    }

    public void DiscardChanges()
    {
        IsDirty = false;
        DropRecoveryCopy();
    }

    // ---------------------------------------------------------------- someone else has it open

    /// <summary>"Rasik (TSAIFUDDIN3) also has this quotation open" while another person has it open.</summary>
    [ObservableProperty] private string? _alsoOpenText;

    public async Task CheckPresenceAsync()
    {
        if (Id is not { } id)
        {
            AlsoOpenText = null;
            return;
        }
        try
        {
            var others = await _session.Api.QuotationPresenceAsync(id);
            AlsoOpenText = others.Count == 0 ? null
                : string.Join(", ", others.Select(o => $"{o.User} ({o.Machine})"))
                  + (others.Count == 1 ? " also has" : " also have") + $" this {DocLabel.ToLowerInvariant()} open. Changes by both of you can clash.";
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            // An older server without this check, or no connection: just show nothing.
        }
    }


    public void Dispose()
    {
        _recoveryTimer.Stop();
        _presenceTimer.Stop();
        if (Id is { } id) _ = LeaveQuietlyAsync(id);
    }

    private async Task LeaveQuietlyAsync(Guid id)
    {
        try { await _session.Api.LeaveQuotationAsync(id); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
    }

    // ---------------------------------------------------------------- who changed it (M3)

    /// <summary>"Changed by hatim at 14:05 · Approved by rasik at 14:10" under the title.</summary>
    public string ChangedByText
    {
        get
        {
            if (_loaded is not { } q || Id is null) return "";
            static string When(DateTime utc)
            {
                var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
                return local.Date == DateTime.Today ? $"at {local:HH:mm}" : $"on {local:dd-MMM} at {local:HH:mm}";
            }
            var parts = new List<string>();
            if (q.UpdatedBy is { Length: > 0 } by && q.UpdatedUtc is { } at)
                parts.Add(by == "tally-sync" ? $"Rates refreshed from Tally {When(at)}" : $"Changed by {by} {When(at)}");
            else if (q.CreatedBy.Length > 0)
                parts.Add($"Created by {q.CreatedBy} {When(q.CreatedUtc)}");
            if (q.ApprovedBy is { Length: > 0 } ab && q.ApprovedUtc is { } aat) parts.Add($"Approved by {ab} {When(aat)}");
            if (q.CancelledBy is { Length: > 0 } cb && q.CancelledUtc is { } cat) parts.Add($"Cancelled by {cb} {When(cat)}");
            return string.Join(" · ", parts);
        }
    }

    // ---------------------------------------------------------------- save clash (M2)

    /// <summary>Kept after "See their changes" in a save clash; "Put my changes back" brings it back.</summary>
    [ObservableProperty] private QuotationDto? _myClashVersion;
    public bool HasMyClashVersion => MyClashVersion is not null;
    partial void OnMyClashVersionChanged(QuotationDto? value) => OnPropertyChanged(nameof(HasMyClashVersion));

    [RelayCommand]
    private void PutMyChangesBack()
    {
        if (MyClashVersion is not { } mine) return;
        MyClashVersion = null;
        ApplyUnsaved(mine, Revision); // on top of their latest version: saving now keeps mine
        InfoMessage = "Your changes are back. Save to keep them (they replace the other user's changes).";
    }

    [RelayCommand]
    private void ForgetMyChanges() => MyClashVersion = null;

    /// <summary>Someone saved the same quotation first: save mine as a new quotation, or look at theirs first.</summary>
    private async Task<bool> ResolveClashAsync(ApiException clash)
    {
        var mine = ToSnapshot();
        var label = DocLabel.ToLowerInvariant();
        var saveAsNew = await _navigator.ConfirmAsync("Someone else saved this " + label,
            $"{clash.Message.Split(" Reload", 2)[0]}\n\nSave your version as a new {label} (it gets the next number), or see their changes first? Your version is kept, so you can put it back.",
            $"Save mine as a new {label}", "See their changes");
        if (saveAsNew)
        {
            var request = BuildRequest();
            var oldNumber = Number;
            return await RunAsync(async () =>
            {
                var created = await _session.Api.CreateQuotationAsync(request);
                Load(created);
                DropRecoveryCopy();
                InfoMessage = $"Your version is saved as {created.Number}. {oldNumber} keeps the other user's changes.";
            });
        }
        if (Id is not { } id) return false;
        await RunAsync(async () => Load(await _session.Api.QuotationAsync(id)));
        MyClashVersion = mine;
        return false;
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
    /// <summary>
    /// Buyer, consignee and references folded into one line (customer name and other references) once they are filled in;
    /// the eye button shows them in full.
    /// </summary>
    [ObservableProperty] private bool _headerCollapsed;
    private bool _headerFoldDecided;

    public bool CanCollapseHeader => !string.IsNullOrWhiteSpace(BuyerName);
    public string HeaderSummaryOtherRef => string.IsNullOrWhiteSpace(OtherReferences) ? "—" : OtherReferences;

    partial void OnBuyerNameChanged(string value) => OnPropertyChanged(nameof(CanCollapseHeader));
    partial void OnOtherReferencesChanged(string value) => OnPropertyChanged(nameof(HeaderSummaryOtherRef));

    /// <summary>Remarks, terms and prepared/verified by folded into one line; the eye button shows them.</summary>
    [ObservableProperty] private bool _footerCollapsed = true;

    public string FooterSummary
    {
        get
        {
            var terms = (TermsAndConditions ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            var remarks = string.IsNullOrWhiteSpace(Remarks) ? "—" : Remarks.Replace('\n', ' ').Trim();
            if (remarks.Length > 50) remarks = remarks[..50] + "…";
            return $"Remarks: {remarks}   ·   Terms: {(terms == 0 ? "—" : terms == 1 ? "1 line" : $"{terms} lines")}   ·   Prepared by: {(string.IsNullOrWhiteSpace(PreparedBy) ? "—" : PreparedBy)}";
        }
    }

    partial void OnRemarksChanged(string value) => OnPropertyChanged(nameof(FooterSummary));
    partial void OnTermsAndConditionsChanged(string value) => OnPropertyChanged(nameof(FooterSummary));
    partial void OnPreparedByChanged(string value) => OnPropertyChanged(nameof(FooterSummary));

    [RelayCommand]
    private void ToggleFooter() => FooterCollapsed = !FooterCollapsed;

    [RelayCommand]
    private void ToggleHeader() => HeaderCollapsed = !HeaderCollapsed && CanCollapseHeader;

    /// <summary>Folds the header after the customer and other references are entered (Enter from Other references).</summary>
    public void FoldHeader()
    {
        if (CanCollapseHeader) HeaderCollapsed = true;
    }
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

    /// <summary>
    /// "Ref. dated" as typed (no calendar): 05-11-2026, 5/11/26, 05.11.2026, 05-Nov-2026 … (day first), shown as
    /// 05-Nov-2026. Empty = no date; text that is not a date keeps the previous date.
    /// </summary>
    public string BuyersReferenceDateText
    {
        get => BuyersReferenceDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) BuyersReferenceDate = null;
            else if (ParseTypedDate(value) is { } d) BuyersReferenceDate = d;
            OnPropertyChanged(); // show it as dd-MMM-yyyy (or the previous date if the text was not a date)
        }
    }

    private static readonly string[] TypedDateFormats =
    [
        "d-M-yyyy", "d-M-yy", "d/M/yyyy", "d/M/yy", "d.M.yyyy", "d.M.yy", "d M yyyy", "d M yy",
        "d-MMM-yyyy", "d-MMM-yy", "d MMM yyyy", "d MMM yy", "d/MMM/yyyy", "d-MMMM-yyyy", "d MMMM yyyy", "ddMMyyyy", "ddMMyy",
    ];

    public static DateTime? ParseTypedDate(string text) =>
        DateTime.TryParseExact(text.Trim(), TypedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
            ? d.Date
            : null;

    partial void OnBuyersReferenceDateChanged(DateTime? value) => OnPropertyChanged(nameof(BuyersReferenceDateText));
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
    [ObservableProperty] private decimal? _packingForwarding; // empty box = 0.00
    [ObservableProperty] private decimal? _packingForwardingPercent;
    [ObservableProperty] private CalcResult? _totals;
    public ObservableCollection<string> ValidationMessages { get; } = [];
    public ObservableCollection<string> AuditTrail { get; } = [];
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
    /// <summary>Regenerate PDF is only needed when making the PDF failed at approval (Open/Save as PDF are always fresh).</summary>
    public bool CanRegeneratePdf => IsFinal && !string.IsNullOrWhiteSpace(PdfError);
    partial void OnPdfErrorChanged(string? value) => OnPropertyChanged(nameof(CanRegeneratePdf));
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
    /// <summary>The number beside the date only while it is provisional; once saved it is in the title.</summary>
    public bool IsNumberProvisional => Number is null;
    /// <summary>"Source: …" only for quotations made from the AI Inbox or Gmail.</summary>
    public bool ShowSource => Source != QuotationSource.Manual;
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

    public string EntryButtonText => EditingLine is null ? "Add line" : "Update line";

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
                var saved = await _session.Api.QuotationAsync(existing);
                if (_restore is { } kept)
                {
                    // Recovered work on an existing quotation: the unsaved changes on top of the saved version.
                    _loaded = saved;
                    ApplyUnsaved(kept, kept.Revision);
                    _restore = null;
                }
                else
                {
                    Load(saved);
                }
            }
            else if (_restore is { } kept)
            {
                ApplyUnsaved(kept, 0);
                _restore = null;
                await RefreshNumberPreviewAsync();
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
                PackingForwarding = _settings.Quotation.DefaultPackingForwardingAmount is > 0 and var pf ? pf : null;
                PackingForwardingPercent = null; // P&F is an amount (the % box was removed)
                _loading = false;
                await RefreshNumberPreviewAsync();
                RecalculateTotals();
                IsDirty = false;
            }
        });
    }

    private string? _provisionalNumber;

    private async Task RefreshNumberPreviewAsync()
    {
        if (Number is not null) return;
        try
        {
            var next = await _session.Api.NextNumberAsync(Date is null ? null : DateOnly.FromDateTime(Date.Value), DocType);
            NumberText = $"{next.Number} (provisional)";
            _provisionalNumber = next.Number;
        }
        catch (ApiException ex)
        {
            NumberText = "(" + ex.Message + ")";
        }
    }

    public void Load(QuotationDto q)
    {
        _loaded = q;
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
        PackingForwarding = q.PackingForwardingPercent is > 0 || q.PackingForwarding == 0 ? null : q.PackingForwarding;
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
        if (PackingForwardingPercent is > 0 && Totals is { } t)
        {
            // An older quotation with P&F as a %: shown (and saved, if edited) as the same amount.
            _loading = true;
            PackingForwarding = t.PackingForwarding;
            PackingForwardingPercent = null;
            _loading = false;
            RecalculateTotals();
        }
        IsDirty = false;
        NotifyState();
        if (!_headerFoldDecided)
        {
            // An existing quotation opens with the header folded; a new one starts open.
            _headerFoldDecided = true;
            HeaderCollapsed = CanCollapseHeader;
        }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanChangeDate));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(IsFinal));
        OnPropertyChanged(nameof(CanRegeneratePdf));
        OnPropertyChanged(nameof(IsPendingReview));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(ChangedByText));
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

    partial void OnIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(CanConvertToProforma));
        OnPropertyChanged(nameof(ChangedByText));
        _ = CheckPresenceAsync();
    }

    partial void OnStatusChanged(QuotationStatus value) => NotifyState();
    partial void OnNumberChanged(string? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsNumberProvisional));
    }

    partial void OnSourceChanged(QuotationSource value) => OnPropertyChanged(nameof(ShowSource));
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
            line.PartNumber = p.PartNumber;
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

    /// <summary>Moves a line to another place (dragged by its grip); Sl. No. are renumbered.</summary>
    public bool MoveLine(QuotationLineViewModel line, int to)
    {
        var from = Lines.IndexOf(line);
        to = Math.Clamp(to, 0, Lines.Count - 1);
        if (from < 0 || from == to) return false;
        Lines.Move(from, to);
        Renumber();
        MarkDirty();
        return true;
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
        ApiException? clash = null;
        var ok = await RunAsync(async () =>
        {
            var created = Id is null;
            QuotationDto dto;
            try
            {
                dto = created
                    ? await _session.Api.CreateQuotationAsync(BuildRequest())
                    : await _session.Api.UpdateQuotationAsync(Id!.Value, BuildRequest());
            }
            catch (ApiException ex) when (!created && ex.Code == ApiErrorCodes.Concurrency)
            {
                clash = ex;
                return;
            }
            Load(dto);
            DropRecoveryCopy();
            MyClashVersion = null;
            // "Without GST" was for this one quotation: the server switched it off; keep the session in step.
            if (created && dto.WithoutGst && _session.Api.CurrentUser is { } me) _session.Api.UpdateCurrentUser(me with { WithoutGstNext = false });
            InfoMessage = created && _provisionalNumber is { } shown && dto.Number is { } got && shown != got
                ? $"Saved as {got}. {shown} was used by another user."
                : $"Saved {dto.Number ?? "draft"} at {DateTime.Now:HH:mm}.";
        });
        if (clash is not null) return await ResolveClashAsync(clash);
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
            // In the user's own layout (My Settings → PDF layout: Tally or Zoho).
            var bytes = pin is null ? await _session.Api.QuotationPdfAsync(Id.Value) : await _session.Api.SealedPdfAsync(Id.Value, pin.Trim());
            LastPdfPath = _session.Documents.Save(bytes, PdfFileName(), _session.Documents.FolderFor(IsProforma));
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
            DropRecoveryCopy();
            _navigator.GoBack();
        }
    }

    [RelayCommand]
    private void DismissStale() => NeedsStaleOverride = false;

    [RelayCommand]
    private async Task OpenPdfAsync()
    {
        if (IsFinal && HasUnsavedChanges)
        {
            // The PDF is of the approved version: changes must be saved and approved again first, or the PDF would be old.
            if (!await _navigator.ConfirmAsync("Unsaved changes",
                    $"You changed this {DocLabel.ToLowerInvariant()}. Save and approve it again before opening the PDF?", "Save & approve", "Cancel"))
                return;
            await ApproveCoreAsync(overrideStale: false, openPdf: true);
            return;
        }
        await RunAsync(OpenPdfCoreAsync);
    }

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
        // The same file as Save as PDF: "Number Customer.pdf".
        LastPdfPath = _session.Documents.SaveAndOpen(bytes, PdfFileName(),
            folder: _session.Documents.FolderFor(IsProforma));
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
        DropRecoveryCopy();
        InfoMessage = $"{Number} cancelled. The number is not reused.";
    });

    [RelayCommand]
    private void NewQuotation() => _navigator.Navigate(IsProforma ? "newproforma" : "new");

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
