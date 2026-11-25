using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using Quotation.Core.Numbering;
using Quotation.Core.Search;
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
    /// <summary>The PDF layout chosen for this quotation (Formater page); null = the user's own.</summary>
    private string? _pdfLayout;
    private bool _loading;

    public QuotationEditorViewModel(AppSession session, INavigator navigator, Guid? id = null, DocumentType docType = DocumentType.Quotation)
    {
        _session = session;
        _startedNew = id is null;
        _stepMode = CleanStepMode(session.Config.LineStepMode);
        foreach (var o in StepModes) o.IsSelected = StepKeys(_stepMode).Contains(o.Key);
        _stepUp = session.Config.LineStepUp;
        _hideHsnGst = session.Config.HideHsnGst;
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
        _sendTimer = new Avalonia.Threading.DispatcherTimer { Interval = SendRetryInterval };
        _sendTimer.Tick += (_, _) => _ = TrySendNowAsync();
        _ = InitializeAsync(id);
    }

    // ---------------------------------------------------------------- Round 38 (Q10): working while the server is off

    /// <summary>How often a quotation waiting to be sent is tried again (tests make it shorter).</summary>
    public static TimeSpan SendRetryInterval { get; set; } = TimeSpan.FromSeconds(15);
    private readonly Avalonia.Threading.DispatcherTimer _sendTimer;

    /// <summary>This new quotation's own key: sent again after a lost answer, the server returns the one already made.</summary>
    private readonly Guid _clientKey = Guid.NewGuid();

    /// <summary>Saved while the server could not be reached: kept on this PC and sent by itself when the server answers.</summary>
    [ObservableProperty] private bool _waitingToSend;

    /// <summary>The orange strip on the page while the quotation waits to be sent.</summary>
    public string WaitingText => Id is null
        ? "Server not reachable — keep working. This quotation is kept safely on this PC and will be sent when the server is back (it gets its number then)."
        : "Server not reachable — keep working. Your changes are kept safely on this PC and will be sent when the server is back.";

    partial void OnWaitingToSendChanged(bool value)
    {
        OnPropertyChanged(nameof(WaitingText));
        if (value) _sendTimer.Start();
        else _sendTimer.Stop();
    }

    /// <summary>"Try now" on the strip, and every few seconds by itself.</summary>
    [RelayCommand]
    private async Task TrySendNowAsync()
    {
        if (!WaitingToSend || IsBusy) return;
        var wasNew = Id is null;
        _quietSave = true;
        try
        {
            if (!await SaveAsync() || WaitingToSend) return;
        }
        finally { _quietSave = false; }
        _navigator.Toast($"Server is back · {(Number ?? "draft")} saved (sent from this PC).");
        if (wasNew) ClickSound.Saved();
    }

    /// <summary>The server did not answer (not a refusal): no connection, or no reply in time.</summary>
    private static bool IsUnreachable(Exception ex) =>
        ex is ApiException { Status: 0 } or HttpRequestException || (ex is TaskCanceledException && ex.InnerException is TimeoutException);

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

    /// <summary>Set by the Formater page: approve as soon as the quotation is on screen.</summary>
    public bool ApproveWhenLoaded { get; init; }
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
            PreparedBy = r.PreparedBy, VerifiedBy = r.VerifiedBy, WithoutGst = r.WithoutGst, GstByBuyer = r.GstByBuyer ?? true, Lines = r.Lines,
            Currency = r.Currency ?? "", ExchangeRate = r.ExchangeRate,
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


    /// <summary>
    /// Another page is shown (this one is kept for Back): it no longer counts as open, so nobody is told "… also has this
    /// quotation open" while it only waits in the history.
    /// </summary>
    public void PageHidden()
    {
        _presenceTimer.Stop();
        AlsoOpenText = null;
        if (Id is { } id) _ = LeaveQuietlyAsync(id);
    }

    /// <summary>Shown again (Back / Forward): open again for the others.</summary>
    public void PageShown()
    {
        if (_presenceTimer.IsEnabled) return;
        _presenceTimer.Start();
        _ = CheckPresenceAsync();
    }

    public void Dispose()
    {
        _recoveryTimer.Stop();
        _sendTimer.Stop();
        _presenceTimer.Stop();
        if (Id is { } id) _ = LeaveQuietlyAsync(id);
    }

    private async Task LeaveQuietlyAsync(Guid id)
    {
        try { await _session.Api.LeaveQuotationAsync(id); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
    }

    // ---------------------------------------------------------------- Won / Lost / Follow-up (D2)

    public bool TrackOutcomes => _settings.Quotation.TrackOutcomes;
    /// <summary>The outcome row shows on approved quotations when an administrator switched tracking on.</summary>
    public bool ShowOutcome => TrackOutcomes && IsFinal && !IsProforma && Id is not null;
    public IReadOnlyList<string> OutcomeChoices { get; } = ["Open", "Won", "Lost"];
    [ObservableProperty] private int _outcomeIndex;
    [ObservableProperty] private string _lostReason = "";
    [ObservableProperty] private DateTime? _followUpDate;
    public bool IsLost => OutcomeIndex == (int)QuotationOutcome.Lost;
    private bool _loadingOutcome;
    private readonly SemaphoreSlim _outcomeGate = new(1, 1);

    private void LoadOutcome(QuotationDto q)
    {
        _loadingOutcome = true;
        OutcomeIndex = (int)q.Outcome;
        LostReason = q.LostReason;
        FollowUpDate = q.FollowUpDate?.ToDateTime(TimeOnly.MinValue);
        _loadingOutcome = false;
        OnPropertyChanged(nameof(ShowOutcome));
    }

    partial void OnOutcomeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsLost));
        SaveOutcome();
    }

    partial void OnFollowUpDateChanged(DateTime? value) => SaveOutcome();

    /// <summary>Lost reason: saved when the box is left (not on every key).</summary>
    [RelayCommand]
    private void SaveLostReason() => SaveOutcome();

    private void SaveOutcome()
    {
        if (_loadingOutcome || _loading || Id is not { } id || !ShowOutcome) return;
        _ = RunAsync(async () =>
        {
            // One change at a time, each sending what is on the screen then: quick changes reach the server in order.
            await _outcomeGate.WaitAsync();
            QuotationDto dto;
            try
            {
                var request = new SetOutcomeRequest((QuotationOutcome)OutcomeIndex, LostReason,
                    FollowUpDate is { } f ? DateOnly.FromDateTime(f) : null);
                dto = await _session.Api.SetOutcomeAsync(id, request);
            }
            finally { _outcomeGate.Release(); }
            _loaded = dto;
            OnPropertyChanged(nameof(ChangedByText));
            InfoMessage = dto.Outcome switch
            {
                QuotationOutcome.Won => $"{Number} marked Won 🎉",
                QuotationOutcome.Lost => $"{Number} marked Lost{(dto.LostReason.Length > 0 ? " (" + dto.LostReason + ")" : "")}.",
                _ => dto.FollowUpDate is { } d ? $"Follow up on {d:dd-MMM-yyyy}." : $"{Number} is open.",
            };
        });
    }

    // ---------------------------------------------------------------- WhatsApp (E2)

    public bool ShowWhatsApp => _settings.Quotation.WhatsAppButton && IsFinal && CanAct;

    /// <summary>Makes the PDF, copies it, and opens the customer's chat (WhatsApp program or WhatsApp Web) with the message typed.</summary>
    [RelayCommand]
    private Task WhatsAppAsync() => RunAsync(async () =>
    {
        if (Id is not { } id) return;
        var bytes = await _session.Api.QuotationPdfAsync(id);
        var path = _session.Documents.Save(bytes, PdfFileName(), _session.Documents.FolderFor(IsProforma));
        LastPdfPath = path;
        var message = (_settings.Quotation.WhatsAppMessage ?? "")
            .Replace("{number}", Number ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{customer}", BuyerName, StringComparison.OrdinalIgnoreCase);
        var phone = DocumentLauncher.WhatsAppNumber(BuyerPhone);
        if (_session.Documents.ShareOnWhatsApp(path, phone, message))
            InfoMessage = _session.Documents.WhatsAppHintNow(phone is not null);
        else
            ErrorMessage = _session.Documents.WhatsAppErrorNow(path);
    });

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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DocTypeName))] private DocumentType _docType;

    /// <summary>"Quotation" or "Proforma Invoice" (the Tally theme shows it in a dark box, like a voucher type).</summary>
    public string DocTypeName => DocType == DocumentType.ProformaInvoice ? "Proforma Invoice" : "Quotation";
    [ObservableProperty] private string? _number;
    [ObservableProperty] private string _numberText = "(assigned on save)";
    [ObservableProperty] private QuotationStatus _status = QuotationStatus.Draft;
    [ObservableProperty] private QuotationSource _source = QuotationSource.Manual;
    [ObservableProperty] private int _revision;
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _hasPdf;
    partial void OnHasPdfChanged(bool value) => OnPropertyChanged(nameof(ShowOpenPdf));
    [ObservableProperty] private string? _pdfError;
    [ObservableProperty] private string? _dataWarning;
    [ObservableProperty] private string? _lastPdfPath;
    /// <summary>"Without GST": 0 % on all items, P&amp;F without GST, no tax lines (from the user's switch in My Settings).</summary>
    [ObservableProperty] private bool _withoutGst;

    /// <summary>Round 38 (PL-1): print the "View product" links on this quotation's PDF (Settings has them on).</summary>
    [ObservableProperty] private bool _showProductLinks = true;
    partial void OnShowProductLinksChanged(bool value) => MarkDirty();
    public bool ProductLinksOnPdf => _settings.Quotation.ProductLinksOnPdf;

    /// <summary>Round 38 (PL-1): the 🔗 after an item name opens its product web page.</summary>
    public void OpenProductLink(QuotationLineViewModel line)
    {
        if (line.HasLink) _session.Documents.OpenWebPage(line.Link);
    }
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

    /// <summary>
    /// The red note under the buyer's GSTIN when it looks mistyped (wrong shape or wrong check letter), e.g. "Looks
    /// mistyped: the last letter should be S"; null when it is right or there is none. Approve also asks about it.
    /// </summary>
    public string? BuyerGstinProblem => Quotation.Core.Gst.Gstin.Problem(BuyerGstin);
    partial void OnBuyerGstinChanged(string value) => OnPropertyChanged(nameof(BuyerGstinProblem));

    // ---- GST by the buyer's state or the ship-to state (only asked when the two give a different GST) ----

    /// <summary>
    /// GST follows the buyer's state (the usual "bill to – ship to" rule; new quotations start here) or, when false, the
    /// ship-to state (quotations made before this choice existed). Saved with the quotation, so reprints stay the same.
    /// </summary>
    [ObservableProperty] private bool _gstByBuyer = true;
    public bool GstByShipTo { get => !GstByBuyer; set => GstByBuyer = !value; }
    partial void OnGstByBuyerChanged(bool value) => OnPropertyChanged(nameof(GstByShipTo));

    /// <summary>The choice under the consignee: only when the ship-to is in another state and that changes the GST.</summary>
    public bool ShowGstByChoice => !ConsigneeSameAsBuyer
                                   && Quotation.Core.Gst.PlaceOfSupply.ChoiceMatters(_settings.Company.StateCode, BuyerStateCode, ConsigneeStateCode);
    public string GstByBuyerText => "Buyer's state · " + StateLabel(BuyerState, BuyerStateCode);
    public string GstByShipToText => "Ship-to state · " + StateLabel(ConsigneeState, ConsigneeStateCode);
    public string GstByBuyerTax => Quotation.Core.Gst.PlaceOfSupply.GstName(_settings.Company.StateCode, BuyerStateCode);
    public string GstByShipToTax => Quotation.Core.Gst.PlaceOfSupply.GstName(_settings.Company.StateCode, ConsigneeStateCode);

    private static string StateLabel(string name, string code) =>
        name.Trim().Length > 0 ? name.Trim() : Quotation.Core.Gst.IndianStates.NameForCode(code) ?? "code " + code;

    /// <summary>The state whose GST this quotation gets (the same rule as the server).</summary>
    private string PlaceOfSupplyCode =>
        Quotation.Core.Gst.PlaceOfSupply.Code(BuyerStateCode, ConsigneeSameAsBuyer ? "" : ConsigneeStateCode, GstByBuyer);

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

    /// <summary>My Settings → Delivery time on items (this PC, Round 35): the Delivery box next to Description.</summary>
    public bool ShowDeliveryBox => _session.Config.DeliveryTimeOnItems;

    /// <summary>Round 37: My Settings → Item rows (this PC): Bigger (default) or Normal.</summary>
    public bool BiggerItemRows => _session.Config.BiggerItemRows;

    /// <summary>The Delivery box's list: "(none)" to clear it, then the usual times (Round 38, DM-1: typed times are not remembered).</summary>
    public IReadOnlyList<string> DeliveryChoices { get; } = [NoDelivery, .. StandardDeliveryTimes];

    public const string NoDelivery = "(none)";
    public static readonly string[] StandardDeliveryTimes = ["Ex-stock", "1 week", "2–3 weeks", "4–6 weeks"];

    [RelayCommand]
    private void PickDelivery(string? value) => EntryDueOn = value is null or NoDelivery ? "" : value;


    // ---------------------------------------------------------------- gear next to Items: line by line / column by column

    /// <summary>The choices of the gear next to Items (Off at first; remembered on this PC).</summary>
    public IReadOnlyList<LineStepOption> StepModes { get; } =
    [
        new("Line", "Next line"), new("Name", "Item name"), new("Description", "Description"),
        new("Quantity", "Quantity"), new("Rate", "Rate"), new("Discount", "Discount"),
    ];

    /// <summary>
    /// The switches that are on: "Off", "Line" (the whole line) or one or more columns ("Rate,Discount"). Several columns
    /// can be on together; "Next line" already covers every box, so it is on by itself.
    /// </summary>
    [ObservableProperty] private string _stepMode = "Off";
    public bool StepModeOn => StepMode != "Off";

    /// <summary>The boxes of a line in the order they are on the screen (Enter goes through the chosen ones in this order).</summary>
    private static readonly string[] ScreenOrder = ["Name", "Quantity", "Rate", "Discount", "Description"];

    /// <summary>The chosen columns in screen order (none for Off and for the whole line).</summary>
    public IReadOnlyList<string> StepColumns => StepKeys(StepMode).Where(k => k != "Line").OrderBy(k => Array.IndexOf(ScreenOrder, k)).ToList();

    /// <summary>The box the cursor goes to when a line opens: the first chosen column, or Quantity for the whole line.</summary>
    public string? FirstStepBox => StepMode == "Line" ? "Quantity" : StepColumns.FirstOrDefault();

    /// <summary>The chosen column after this one on the same line; null when this is the last one (Enter then saves the line).</summary>
    public string? NextStepColumn(string column)
    {
        var columns = StepColumns.ToList();
        var at = columns.IndexOf(column);
        return at >= 0 && at + 1 < columns.Count ? columns[at + 1] : null;
    }

    /// <summary>"Going down: Rate, Discount · line 2 of 3" while a line is open in a step mode.</summary>
    public string StepInfo => StepModeOn && EditingLine is { } l
        ? $"Going {(StepUp ? "up" : "down")}: {(StepMode == "Line" ? "line by line" : string.Join(", ", StepColumns.Select(k => StepModes.First(m => m.Key == k).Name)))} · line {l.LineNo} of {Lines.Count}"
        : "";

    private static IReadOnlyCollection<string> StepKeys(string? mode) =>
        (mode ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(k => k != "Off").ToHashSet();

    /// <summary>A saved choice made tidy: known keys only, in the gear's order; the whole line on its own; "Off" when none.</summary>
    private string CleanStepMode(string? mode)
    {
        var keys = StepKeys(mode);
        if (keys.Contains("Line")) return "Line";
        var chosen = StepModes.Where(m => keys.Contains(m.Key)).Select(m => m.Key).ToList();
        return chosen.Count == 0 ? "Off" : string.Join(",", chosen);
    }

    /// <summary>Gear: go to the line above instead of below (remembered on this PC).</summary>
    [ObservableProperty] private bool _stepUp;

    partial void OnStepUpChanged(bool value)
    {
        OnPropertyChanged(nameof(StepInfo));
        if (_session.Config.LineStepUp == value) return;
        _session.Config.LineStepUp = value;
        SaveConfig();
    }

    /// <summary>Gear: the HSN/SAC and GST columns are hidden on this PC's screen (the PDF keeps them).</summary>
    [ObservableProperty] private bool _hideHsnGst;
    public bool ShowHsnGst => !HideHsnGst;

    partial void OnHideHsnGstChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHsnGst));
        if (_session.Config.HideHsnGst == value) return;
        _session.Config.HideHsnGst = value;
        SaveConfig();
    }

    private void SaveConfig()
    {
        try { _session.Config.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* only this PC's choice */ }
    }

    /// <summary>Gear: the % typed for "Same discount on all lines".</summary>
    [ObservableProperty] private string _allLinesDiscountText = "";

    /// <summary>Gear: puts the same discount on every line at once (0 clears all discounts).</summary>
    [RelayCommand]
    private void ApplyDiscountToAll()
    {
        if (!CanEditContent || Lines.Count == 0) return;
        var text = (AllLinesDiscountText ?? "").Trim().TrimEnd('%').Trim();
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var percent)
            || percent < 0 || percent > 100)
        {
            _navigator.Toast("Type a discount from 0 to 100 %.");
            return;
        }
        foreach (var line in Lines) line.DiscountPercent = percent;
        RecalculateTotals();
        IsDirty = true;
        _navigator.Toast(percent == 0
            ? "Discount cleared on all lines."
            : $"{percent:0.##} % discount put on {(Lines.Count == 1 ? "the line" : $"all {Lines.Count} lines")}.");
    }
    /// <summary>Gear: the delivery time typed for "Same delivery on all lines".</summary>
    [ObservableProperty] private string _allLinesDeliveryText = "";

    /// <summary>
    /// Gear: puts the same delivery time on every line at once ("(none)" or an empty box clears them all), with Undo.
    /// The value is the typed one, or <paramref name="value"/> when a choice (Ex-stock, 1 week …) is clicked.
    /// </summary>
    [RelayCommand]
    public void ApplyDeliveryToAll(string? value = null)
    {
        if (!CanEditContent || Lines.Count == 0) return;
        var delivery = (value ?? AllLinesDeliveryText ?? "").Trim();
        if (delivery == NoDelivery) delivery = "";
        AllLinesDeliveryText = delivery;
        var old = Lines.Select(l => (Line: l, DueOn: l.DueOn)).ToList();
        foreach (var line in Lines) line.DueOn = delivery;
        MarkDirty();
        var lines = Lines.Count == 1 ? "the line" : $"all {Lines.Count} lines";
        _navigator.Toast(delivery.Length == 0 ? $"Delivery cleared on {lines}." : $"Delivery \"{delivery}\" put on {lines}.", "Undo", () =>
        {
            foreach (var (line, was) in old) line.DueOn = was;
            MarkDirty();
        }, 12);
    }

    /// <summary>Gear (Round 36): the % typed for "Raise all rates by" (a minus number lowers them).</summary>
    [ObservableProperty] private string _raiseRatesText = "";

    /// <summary>
    /// Gear (Round 36): every line's rate goes up by the typed % (a minus number lowers it), rounded to paise, with Undo.
    /// </summary>
    [RelayCommand]
    private void RaiseAllRates()
    {
        if (!CanEditContent || Lines.Count == 0) return;
        var text = (RaiseRatesText ?? "").Trim().TrimEnd('%').Trim();
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var percent)
            || percent == 0 || percent <= -100 || percent > 1000)
        {
            _navigator.Toast("Type a % to raise the rates by (e.g. 5, or -5 to lower them).");
            return;
        }
        var changed = new List<(QuotationLineViewModel Line, decimal Old)>();
        foreach (var line in Lines)
        {
            if (line.Rate <= 0) continue;
            changed.Add((line, line.Rate));
            line.Rate = Math.Round(line.Rate * (100 + percent) / 100, 2, MidpointRounding.AwayFromZero);
        }
        if (changed.Count == 0) return;
        AfterPricesChanged();
        var what = changed.Count == 1 ? "1 rate" : $"{changed.Count} rates";
        _navigator.Toast(percent > 0 ? $"{what} raised by {percent:0.##} %." : $"{what} lowered by {-percent:0.##} %.", "Undo", () =>
        {
            foreach (var (line, old) in changed) line.Rate = old;
            AfterPricesChanged();
        }, 12);
    }

    public bool HasStepInfo => StepInfo.Length > 0;

    /// <summary>After a line is saved: the box the cursor goes to ("Product" for a new line; a column or "Quantity" for the next line).</summary>
    public event Action<string>? FocusEntry;

    /// <summary>
    /// A switch in the gear turns that choice on or off; the others stay as they are (several columns can be on together).
    /// "Next line" (the whole line) and the columns do not go together: turning one on turns the other kind off. "Off" turns
    /// everything off.
    /// </summary>
    [RelayCommand]
    private void ChooseStepMode(string? mode)
    {
        if (mode is null) return;
        if (mode == "Off") StepMode = "Off";
        else if (StepModes.Any(m => m.Key == mode))
        {
            var keys = StepKeys(StepMode).ToHashSet();
            if (!keys.Remove(mode))
            {
                if (mode == "Line") keys.Clear();
                else keys.Remove("Line");
                keys.Add(mode);
            }
            StepMode = CleanStepMode(string.Join(",", keys));
        }
        SyncStepSwitches();
    }

    private void SyncStepSwitches()
    {
        var keys = StepKeys(StepMode);
        foreach (var m in StepModes) m.IsSelected = keys.Contains(m.Key);
    }

    partial void OnStepModeChanged(string value)
    {
        SyncStepSwitches();
        OnPropertyChanged(nameof(StepModeOn));
        OnPropertyChanged(nameof(StepColumns));
        OnPropertyChanged(nameof(FirstStepBox));
        OnPropertyChanged(nameof(StepInfo));
        OnPropertyChanged(nameof(HasStepInfo));
        if (_session.Config.LineStepMode == value) return;
        _session.Config.LineStepMode = value;
        SaveConfig();
    }

    partial void OnEditingLineChanged(QuotationLineViewModel? value)
    {
        OnPropertyChanged(nameof(EntryButtonText));
        OnPropertyChanged(nameof(StepInfo));
        OnPropertyChanged(nameof(HasStepInfo));
    }

    /// <summary>
    /// Saves the line being entered (Enter, or the Add / Update line button). With the gear on, a changed line opens the next
    /// line at once (the whole line, or only the chosen column); after the last line it is back to a new empty line.
    /// </summary>
    [RelayCommand]
    private void SaveLine()
    {
        var editing = EditingLine;
        var index = editing is null ? -1 : Lines.IndexOf(editing);
        CommitEntry();
        if (ErrorMessage is not null) return; // not saved: stays as it is
        var next = StepUp ? index - 1 : index + 1;
        if (editing is not null && StepModeOn && index >= 0 && next >= 0 && next < Lines.Count)
        {
            EditLine(Lines[next]);
            FocusEntry?.Invoke(FirstStepBox ?? "Quantity");
            return;
        }
        FocusEntry?.Invoke("Product");
    }

    public Func<string?, CancellationToken, Task<IEnumerable<object>>> ProductPopulator { get; }
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> CustomerPopulator { get; }

    /// <summary>
    /// The ☆ / ★ on each row of the product and customer search lists (null when the administrator switched Stars off):
    /// starred ones come first in this user's searches.
    /// </summary>
    public Func<object, Task>? StarToggle => _session.Settings?.Quotation.Stars == false ? null : ToggleStarAsync;

    /// <summary>"★ YOUR STARRED ITEMS" over the first starred row and "OTHER MATCHES" over the first one after them.</summary>
    private string? PickHeading(bool starred, bool first, bool anyStarred, string what) =>
        StarToggle is null || !anyStarred || !first ? null : starred ? "★ YOUR STARRED " + what : "OTHER MATCHES";

    public async Task ToggleStarAsync(object item)
    {
        try
        {
            switch (item)
            {
                case ProductSearchHitDto p:
                    await _session.Api.SetStarAsync("product", p.Product.Id, !p.IsStarred);
                    break;
                case CustomerSearchHitDto c:
                    await _session.Api.SetStarAsync("customer", c.Customer.Id, !c.IsStarred);
                    break;
            }
        }
        catch (ApiException ex)
        {
            _navigator.Toast(ex.Status == System.Net.HttpStatusCode.NotFound ? "Stars need the server update." : ex.Message);
            throw;
        }
    }

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
    public string GrandTotalText => (IsUsd ? "$ " : "₹ ") + IndianFormat.Amount(Totals?.GrandTotal ?? 0);

    /// <summary>Round 36: a USD quotation (My Settings → USD quotation, next quotation only): rates in US dollars, no GST.</summary>
    [ObservableProperty] private bool _isUsd;
    /// <summary>Round 36: 1 USD = this many rupees for this quotation.</summary>
    [ObservableProperty] private decimal? _usdRate;
    /// <summary>"$ USD · 1 USD = ₹ 84.20 · no GST".</summary>
    public string UsdChipText => UsdRate is { } r ? $"$ USD · 1 USD = ₹ {r:0.00} · no GST" : "$ USD · no GST";

    /// <summary>The "WITHOUT GST" pill (a USD quotation says "no GST" in its own chip).</summary>
    public bool ShowWithoutGstPill => WithoutGst && !IsUsd;

    partial void OnIsUsdChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWithoutGstPill));
        OnPropertyChanged(nameof(GrandTotalText));
        OnPropertyChanged(nameof(PackingLabel));
    }

    partial void OnUsdRateChanged(decimal? value) => OnPropertyChanged(nameof(UsdChipText));

    /// <summary>A Tally rate (rupees) as this quotation shows it: in dollars on a USD quotation (2 decimals).</summary>
    public decimal? InQuotationCurrency(decimal? rupees) =>
        rupees is not { } r ? null : IsUsd && UsdRate is > 0 ? Math.Round(r / UsdRate.Value, 2, MidpointRounding.AwayFromZero) : r;
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
    /// <summary>Administrators do not make quotations: they open, preview, cancel and delete them.</summary>
    public bool MakesDocuments => _session.MakesDocuments;
    /// <summary>Approved at least once: only the administrator can cancel it (also after it was edited back to draft).</summary>
    public bool EverApproved => _loaded?.EverApproved == true;
    /// <summary>Who it belongs to (null: new, or a Gmail draft nobody has saved yet).</summary>
    public string? OwnerName => _loaded?.Owner;
    /// <summary>
    /// With "users change only their own" on (Settings → Quotation), someone else's document can only be looked at.
    /// </summary>
    public bool IsMine => IsAdmin || !_settings.Quotation.UsersChangeOnlyTheirOwn || OwnerName is null
                          || string.Equals(OwnerName, _session.Api.CurrentUser?.Username, StringComparison.OrdinalIgnoreCase);
    public string NotMineText => $"Made by {OwnerName}: you can look at it, but only {OwnerName} or the administrator can change it.";
    public bool ShowNotMine => !IsMine;
    /// <summary>Approve, Save as PDF and the rest: this user makes quotations and this one is theirs.</summary>
    public bool CanAct => MakesDocuments && IsMine;
    /// <summary>The items, parties and terms can be changed: by the person who makes quotations, while not locked or cancelled.</summary>
    public bool CanEditContent => IsEditable && CanAct;
    /// <summary>Cancel shows for the administrator, and for a user on their own document that was never approved.</summary>
    public bool ShowCancel => IsAdmin || (!EverApproved && IsMine);
    /// <summary>Cancel: as long as it can be edited, or always for an administrator (until it is cancelled).</summary>
    public bool CanCancel => ShowCancel && Status != QuotationStatus.Cancelled && (IsEditable || (IsAdmin && Id is not null));
    /// <summary>Open PDF: the administrator's way to see an approved PDF (users have Save as PDF, which can open it).</summary>
    public bool ShowOpenPdf => HasPdf && IsAdmin;
    /// <summary>Only administrators change the date; for users it is the day the quotation is first saved (enforced by the server).</summary>
    public bool CanChangeDate => IsAdmin && CanEditContent;
    public bool CanApprove => Status is QuotationStatus.Draft or QuotationStatus.PendingReview && Lines.Count > 0 && !IsLocked;
    public bool CanDelete => IsAdmin && Id is not null;
    /// <summary>
    /// The Seal tick-box shows only for a user (not an administrator) who has their own seal picture and PIN
    /// (set by an administrator in Settings → Users).
    /// </summary>
    public bool CanSeal => _session.Api.CurrentUser is { Role: not UserRole.Admin, HasSealImage: true } me
                           && (me.HasSealPin || !SealPinOn) && IsMine;
    public bool SealAvailable => CanSeal;
    /// <summary>Settings → Features → Seal PIN (off by default): only then is the 4-digit PIN asked before sealing.</summary>
    private bool SealPinOn => _settings.Quotation.SealPin;
    public string SealHint => SealPinOn ? "Put your seal on the PDF (asks for your 4-digit seal PIN)" : "Put your seal on the PDF";
    public bool CanSaveAsPdf => Status != QuotationStatus.Cancelled && (IsFinal || (Lines.Count > 0 && !IsLocked));
    /// <summary>"Packing &amp; Forwarding ₹" ("Without GST" shows in small under it in Without GST mode).</summary>
    public string PackingLabel => IsUsd ? "Packing & Forwarding $" : "Packing & Forwarding ₹";
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
    public string CancelTip => $"Cancel this {DocLabel.ToLowerInvariant()} (it is kept, marked CANCELLED)";

    /// <summary>
    /// Clear (a quotation approved on the New quotation page, for those who make quotations): a new, empty one on the same
    /// screen; this one stays approved (Back returns to it). Ctrl+N / Delete. Not on one opened from Today's Quotations or
    /// History.
    /// </summary>
    public bool ShowClear => IsFinal && Id is not null && _startedNew && _session.MakesDocuments;

    /// <summary>This screen began as a new, empty quotation (the New quotation page), not one opened from a list.</summary>
    private readonly bool _startedNew;
    public string ClearTip => $"Clear: a new, empty {DocLabel.ToLowerInvariant()} (this one stays approved) (Ctrl+N)";

    [RelayCommand]
    private void Clear()
    {
        if (ShowClear) _navigator.Navigate(IsProforma ? "newproforma" : "new");
    }
    public string NumberLabel => IsProforma ? "Invoice No." : "Quotation No.";
    /// <summary>The status chip in the heading: "Draft", "Approved", "Pending review" …</summary>
    public string StatusChipText => StatusText.Length == 0 ? "" : StatusText[0] + StatusText[1..].ToLowerInvariant();
    /// <summary>The number beside the date only while it is provisional; once saved it is in the title.</summary>
    public bool IsNumberProvisional => Number is null;
    /// <summary>"Source: …" only for quotations made from the AI Inbox or Gmail.</summary>
    public bool ShowSource => Source != QuotationSource.Manual;
    public string Title => Number is null ? "New " + DocLabel : $"{DocLabel} {Number}";
    /// <summary>The heading of the page: the number (the next free one until the first save); "New Quotation" until one is known.</summary>
    public string Heading => Number ?? _provisionalNumber ?? "New " + DocLabel;
    /// <summary>Beside a number that is not saved yet: "(provisional)", or why no number can be shown.</summary>
    public string HeadingNote => Number is not null ? ""
        : _provisionalNumber is not null ? "(provisional)"
        : NumberText.StartsWith('(') && NumberText != "(assigned on save)" ? NumberText : "";
    public bool HasHeadingNote => HeadingNote.Length > 0;

    private void NotifyHeading()
    {
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(HeadingNote));
        OnPropertyChanged(nameof(HasHeadingNote));
    }

    partial void OnNumberTextChanged(string value) => NotifyHeading();
    /// <summary>A saved quotation can be turned into a proforma invoice (a new document with its own TSPI number).</summary>
    public bool CanConvertToProforma => !IsProforma && Id is not null && Status != QuotationStatus.Cancelled && MakesDocuments
                                        && _settings.Quotation.ProformaEnabled;
    /// <summary>Unsaved header/line changes, or a line typed in the entry row but not added yet.</summary>
    public bool HasUnsavedChanges => CanEditContent && (IsDirty || EntryProduct is not null || EditingLine is not null);

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
            try
            {
                // An administrator may have given this user a seal since they signed in.
                _session.Api.UpdateCurrentUser(await _session.Api.MeAsync());
            }
            catch (ApiException) { }
            Controls.TitleCase.IsOn = _settings.Quotation.TitleCase;
            OnPropertyChanged(nameof(TrackOutcomes));
            OnPropertyChanged(nameof(ShowOutcome));
            OnPropertyChanged(nameof(ShowWhatsApp));
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
                // From the Formater page: "Approve & make PDF" goes straight on to the usual approval.
                if (ApproveWhenLoaded) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ApproveCommand.ExecuteAsync(null));
            }
            else
            {
                _loading = true;
                WithoutGst = _session.Api.CurrentUser?.WithoutGstNext == true;
                if (_session.Api.CurrentUser is { UsdNext: true, UsdRate: > 0 } usdUser && DocType == DocumentType.Quotation)
                {
                    UsdRate = usdUser.UsdRate;
                    IsUsd = true;
                    WithoutGst = true; // a USD quotation is without GST
                }
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
            _provisionalNumber = next.Number;
            NumberText = $"{next.Number} (provisional)";
        }
        catch (ApiException ex)
        {
            _provisionalNumber = null;
            NumberText = "(" + ex.Message + ")";
        }
    }

    public void Load(QuotationDto q)
    {
        _loaded = q;
        LoadOutcome(q);
        _loading = true;
        Id = q.Id;
        DocType = q.DocType;
        WithoutGst = q.WithoutGst;
        ShowProductLinks = q.ShowProductLinks;
        GstByBuyer = q.GstByBuyer;
        UsdRate = q.ExchangeRate;
        IsUsd = q.IsUsd;
        EditableUntil = q.EditableUntil;
        DoNotUseWarning = q.DoNotUseItems.Count == 0 ? null
            : "Marked DNU (do not use) in Tally — replace before sending: " + string.Join(", ", q.DoNotUseItems);
        Number = q.Number;
        NumberText = q.Number ?? "(assigned on save)";
        Status = q.Status;
        Source = q.Source;
        _pdfLayout = q.PdfLayout;
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
        NotifyState(); // who it belongs to and whether it was ever approved came with it
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
        OnPropertyChanged(nameof(ShowCopyInvoice));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanEditContent));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(ShowCancel));
        OnPropertyChanged(nameof(ShowClear));
        OnPropertyChanged(nameof(EverApproved));
        OnPropertyChanged(nameof(OwnerName));
        OnPropertyChanged(nameof(IsMine));
        OnPropertyChanged(nameof(NotMineText));
        OnPropertyChanged(nameof(ShowNotMine));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(CanSeal));
        OnPropertyChanged(nameof(ShowOpenPdf));
        OnPropertyChanged(nameof(CanChangeDate));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(IsFinal));
        OnPropertyChanged(nameof(CanRegeneratePdf));
        OnPropertyChanged(nameof(IsPendingReview));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(ChangedByText));
        OnPropertyChanged(nameof(ShowOutcome));
        OnPropertyChanged(nameof(ShowWhatsApp));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusChipText));
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
        OnPropertyChanged(nameof(ShowWithoutGstPill));
        OnPropertyChanged(nameof(ShowsTaxes));
        OnPropertyChanged(nameof(PackingLabel));
        RecalculateTotals();
    }

    partial void OnEditableUntilChanged(DateOnly? value) => NotifyState();

    partial void OnDocTypeChanged(DocumentType value)
    {
        OnPropertyChanged(nameof(DocLabel));
        OnPropertyChanged(nameof(CancelTip));
        OnPropertyChanged(nameof(NumberLabel));
        NotifyHeading();
        NotifyState();
    }

    partial void OnIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanConvertToProforma));
        OnPropertyChanged(nameof(ChangedByText));
        OnPropertyChanged(nameof(ListPositionText));
        OnPropertyChanged(nameof(HasListPosition));
        _ = CheckPresenceAsync();
    }

    partial void OnStatusChanged(QuotationStatus value) => NotifyState();
    partial void OnNumberChanged(string? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsNumberProvisional));
        NotifyHeading();
    }

    partial void OnSourceChanged(QuotationSource value) => OnPropertyChanged(nameof(ShowSource));
    partial void OnDateChanged(DateTime? value)
    {
        if (!_loading) _ = RefreshNumberPreviewAsync();
        MarkDirty();
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(BuyerState) or nameof(BuyerStateCode) or nameof(ConsigneeState) or nameof(ConsigneeStateCode)
            or nameof(ConsigneeSameAsBuyer))
        {
            OnPropertyChanged(nameof(ShowGstByChoice));
            OnPropertyChanged(nameof(GstByBuyerText));
            OnPropertyChanged(nameof(GstByShipToText));
            OnPropertyChanged(nameof(GstByBuyerTax));
            OnPropertyChanged(nameof(GstByShipToTax));
        }
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
            case nameof(PackingForwarding) or nameof(PackingForwardingPercent) or nameof(ConsigneeStateCode) or nameof(ConsigneeSameAsBuyer)
                or nameof(GstByBuyer):
                MarkDirty();
                RecalculateTotals();
                break;
            case nameof(BuyerStateCode):
                // GST follows the buyer's state on new quotations: a new customer can change it
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
        try
        {
            var hits = await _session.Api.SearchCustomersAsync(text, 15, ct, starredFirst: StarToggle is not null);
            return hits.Select((h, i) => h with { Heading = PickHeading(h.IsStarred, i == 0 || hits[i - 1].IsStarred != h.IsStarred, hits[0].IsStarred, "CUSTOMERS") });
        }
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
        try
        {
            var hits = await _session.Api.SearchProductsAsync(text, 25, ct, starredFirst: StarToggle is not null);
            return hits.Select((h, i) => h with { Heading = PickHeading(h.IsStarred, i == 0 || hits[i - 1].IsStarred != h.IsStarred, hits[0].IsStarred, "ITEMS") });
        }
        catch (Exception) { return []; }
    }

    /// <summary>Where a suggested rate came from when Tally has no selling price for the item.</summary>
    public string EntryRateNoteText => EntryProduct?.RateNote is { Length: > 0 } note ? "Last rate: " + note : "";

    partial void OnEntryProductChanged(ProductSummaryDto? value) => OnPropertyChanged(nameof(EntryRateNoteText));

    public void SelectProduct(ProductSummaryDto p)
    {
        EntryProduct = p;
        EntryRate = p.Rate > 0 ? InQuotationCurrency(p.Rate) : null; // no rate in Tally: an empty box to type into, not 0.00
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
            line.Link = p.Link;
        }
        line.Quantity = EntryQuantity.Value;
        line.Rate = EntryRate ?? 0;
        line.DiscountPercent = EntryDiscount ?? 0;
        line.Description = EntryDescription.Trim();
        line.DueOn = EntryDueOn.Trim() == NoDelivery ? "" : EntryDueOn.Trim();
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
        EntryRate = line.Rate > 0 ? line.Rate : null;
        EntryDiscount = line.DiscountPercent;
        EntryDescription = line.Description;
        EntryDueOn = line.DueOn;
    }

    [RelayCommand]
    private void RemoveLine(QuotationLineViewModel? line)
    {
        line ??= SelectedLine;
        if (line is null) return;
        var index = Lines.IndexOf(line);
        if (index < 0) return;
        var shownNo = line.LineNo;
        Lines.Remove(line);
        Renumber();
        if (EditingLine == line) ClearEntry();
        MarkDirty();
        OnPropertyChanged(nameof(CanApprove));
        // A removed line can be put back for a few seconds (in case it was removed by mistake).
        _navigator.Toast($"Line {shownNo} removed ({line.ItemName}).", "Undo", () => UndoRemoveLine(line, index));
    }

    private void UndoRemoveLine(QuotationLineViewModel line, int index)
    {
        if (!IsEditable || Lines.Contains(line)) return;
        Lines.Insert(Math.Clamp(index, 0, Lines.Count), line);
        Renumber();
        MarkDirty();
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanSaveAsPdf));
    }

    /// <summary>The line the shortcuts work on: the one being changed (clicked), else the one picked in the list.</summary>
    private QuotationLineViewModel? PickedLine => EditingLine ?? SelectedLine;

    /// <summary>
    /// Round 38 (Q5): Ctrl+D — a copy of the picked line just below it (item, description, qty, rate, disc, delivery),
    /// ready to change; Undo in the message.
    /// </summary>
    [RelayCommand]
    private void DuplicateLine()
    {
        if (!CanEditContent || PickedLine is not { } line) return;
        if (EditingLine is not null)
        {
            // What is typed in the boxes goes into the line first (nothing is lost).
            CommitEntry();
            if (EditingLine is not null) return; // the boxes have a problem (shown); nothing is copied
        }
        var index = Lines.IndexOf(line);
        if (index < 0) return;
        var copy = new QuotationLineViewModel
        {
            ProductId = line.ProductId, ItemName = line.ItemName, PartNumber = line.PartNumber, Description = line.Description,
            Hsn = line.Hsn, GstRate = line.GstRate, DueOn = line.DueOn, Quantity = line.Quantity, Unit = line.Unit, Rate = line.Rate,
            TallyRate = line.TallyRate, DiscountPercent = line.DiscountPercent, ZeroGst = line.ZeroGst, Link = line.Link,
        };
        Lines.Insert(index + 1, copy);
        Renumber();
        MarkDirty();
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanSaveAsPdf));
        EditLine(copy);
        _navigator.Toast($"Line {line.LineNo} copied as line {copy.LineNo}.", "Undo", () =>
        {
            if (!IsEditable || !Lines.Contains(copy)) return;
            if (EditingLine == copy) ClearEntry();
            Lines.Remove(copy);
            Renumber();
            MarkDirty();
            OnPropertyChanged(nameof(CanApprove));
            OnPropertyChanged(nameof(CanSaveAsPdf));
        });
    }

    /// <summary>Round 38 (Q5): Ctrl+↑ / Ctrl+↓ — the picked line one place up / down (not past the ends); Undo in the message.</summary>
    public void MoveLineStep(int step)
    {
        if (!CanEditContent || PickedLine is not { } line) return;
        var from = Lines.IndexOf(line);
        if (from < 0 || !MoveLine(line, from + step)) return;
        _navigator.Toast($"Line moved to {line.LineNo}.", "Undo", () =>
        {
            if (IsEditable && Lines.Contains(line)) MoveLine(line, from);
        });
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

    // ================================================================= Page Up / Page Down through a list

    /// <summary>The list this quotation was opened from (Today's Quotations, History …), for Page Up / Page Down.</summary>
    public IReadOnlyList<Guid>? ListIds { get; init; }

    /// <summary>"3 of 25 · PgUp / PgDn" when opened from a list.</summary>
    public string ListPositionText => ListIds is { Count: > 1 } list && Id is { } id && list.ToList().IndexOf(id) is >= 0 and var i
        ? $"{i + 1} of {list.Count} · PgUp / PgDn"
        : "";
    public bool HasListPosition => ListPositionText.Length > 0;

    // ================================================================= copy from a Tally invoice (Round 36)

    /// <summary>"⎘ Copy from Tally invoice" beside Items: the administrator's switch is on, a Tally customer is chosen, and the page can be changed.</summary>
    public bool ShowCopyInvoice => _settings.Quotation.CopyFromTallyInvoice && CustomerId is not null && CanEditContent;

    partial void OnCustomerIdChanged(int? value) => OnPropertyChanged(nameof(ShowCopyInvoice));

    [ObservableProperty] private bool _isInvoiceOpen;
    [ObservableProperty] private bool _invoiceLoading;
    [ObservableProperty] private string? _invoiceMessage;
    /// <summary>The customer's invoices from Tally, newest first.</summary>
    public ObservableCollection<TallyInvoiceDto> Invoices { get; } = [];
    [ObservableProperty] private TallyInvoiceDto? _selectedInvoice;
    /// <summary>The chosen invoice's items, each with a tick.</summary>
    public ObservableCollection<InvoiceItemViewModel> InvoiceItems { get; } = [];
    /// <summary>Today's rate from Tally (true, at first) or the rate on the invoice.</summary>
    [ObservableProperty] private bool _invoiceUseTodayRate = true;
    public bool InvoiceUseInvoiceRate { get => !InvoiceUseTodayRate; set => InvoiceUseTodayRate = !value; }
    public bool HasSelectedInvoice => SelectedInvoice is not null;
    public string InvoiceItemsTitle => SelectedInvoice is { } i ? $"Items of {i.Number} · {i.Date:dd-MMM-yyyy}" : "";
    public int InvoiceAddCount => InvoiceItems.Count(i => i.IsTicked && i.Product is not null);
    public string InvoiceAddText => InvoiceAddCount == 1 ? "Add 1 item" : $"Add {InvoiceAddCount} items";

    partial void OnInvoiceUseTodayRateChanged(bool value) => OnPropertyChanged(nameof(InvoiceUseInvoiceRate));

    partial void OnSelectedInvoiceChanged(TallyInvoiceDto? value)
    {
        foreach (var old in InvoiceItems) old.PropertyChanged -= OnInvoiceItemChanged;
        InvoiceItems.Clear();
        if (value is not null)
        {
            foreach (var line in value.Lines)
            {
                var item = new InvoiceItemViewModel(line, InQuotationCurrency(line.Product?.Rate), InQuotationCurrency(line.InvoiceRate)!.Value);
                item.PropertyChanged += OnInvoiceItemChanged;
                InvoiceItems.Add(item);
            }
        }
        OnPropertyChanged(nameof(HasSelectedInvoice));
        OnPropertyChanged(nameof(InvoiceItemsTitle));
        OnPropertyChanged(nameof(InvoiceAddCount));
        OnPropertyChanged(nameof(InvoiceAddText));
    }

    private void OnInvoiceItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(InvoiceAddCount));
        OnPropertyChanged(nameof(InvoiceAddText));
    }

    /// <summary>Opens the window and reads the customer's sales invoices of the last 12 months from Tally (read only).</summary>
    [RelayCommand]
    public async Task OpenInvoicesAsync()
    {
        if (!ShowCopyInvoice || CustomerId is not { } customer) return;
        Invoices.Clear();
        SelectedInvoice = null;
        InvoiceUseTodayRate = true;
        InvoiceMessage = null;
        IsInvoiceOpen = true;
        InvoiceLoading = true;
        try
        {
            foreach (var i in await _session.Api.TallyInvoicesAsync(customer)) Invoices.Add(i);
            if (Invoices.Count == 0) InvoiceMessage = "No sales invoices for this customer in Tally in the last 12 months.";
        }
        catch (ApiException ex)
        {
            InvoiceMessage = ex.Status == System.Net.HttpStatusCode.NotFound && ex.Code is null && ex.Message.Length == 0
                ? "This needs the server update." : ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            InvoiceMessage = "The server could not be reached: " + ex.Message;
        }
        finally
        {
            InvoiceLoading = false;
        }
    }

    [RelayCommand]
    public void CloseInvoices()
    {
        IsInvoiceOpen = false;
        SelectedInvoice = null;
        Invoices.Clear();
    }

    /// <summary>Adds the ticked items (those still in Tally) at today's or the invoice's rate; the discount is not copied.</summary>
    [RelayCommand]
    public void AddInvoiceItems()
    {
        if (SelectedInvoice is not { } invoice || !CanEditContent) return;
        var add = InvoiceItems.Where(i => i.IsTicked && i.Product is not null).ToList();
        var skipped = InvoiceItems.Count(i => i.Product is null);
        foreach (var i in add)
        {
            var p = i.Product!;
            Lines.Add(new QuotationLineViewModel
            {
                ProductId = p.Id,
                ItemName = p.Name,
                PartNumber = p.PartNumber,
                Hsn = p.Hsn,
                GstRate = p.GstRate,
                Unit = p.Unit,
                TallyRate = p.Rate,
                Link = p.Link,
                Quantity = i.Quantity,
                Rate = InvoiceUseTodayRate ? i.TodayRate ?? i.InvoiceRate : i.InvoiceRate,
                Description = (p.Description ?? "").Trim(),
                ZeroGst = WithoutGst,
            });
        }
        CloseInvoices();
        if (add.Count == 0) return;
        Renumber();
        MarkDirty();
        RecalculateTotals();
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanSaveAsPdf));
        var what = add.Count == 1 ? "1 item" : $"{add.Count} items";
        _navigator.Toast($"{what} added from Tally invoice {invoice.Number}" + (skipped > 0 ? $" ({skipped} skipped: not in Tally now)." : "."));
    }

    // ================================================================= pasted list

    /// <summary>The lines of a list pasted into the Product box (WhatsApp, e-mail, Excel), each with the item found for it.</summary>
    public ObservableCollection<PasteRowViewModel> PasteRows { get; } = [];

    [ObservableProperty] private bool _isPasteOpen;

    public string PasteTitle => $"Pasted list — {PasteRows.Count} line{(PasteRows.Count == 1 ? "" : "s")}";
    public int PasteReadyCount => PasteRows.Count(r => r.IsFound && r.IsReady);
    public int PasteChooseCount => PasteRows.Count(r => r.IsChoose);
    public int PasteNotFoundCount => PasteRows.Count(r => r.IsNotFound);
    public bool PasteStillSearching => PasteRows.Any(r => r.IsLoading);
    public string PasteReadyText => $"{PasteReadyCount} ready";
    public string PasteChooseText => $"{PasteChooseCount} to choose";
    public string PasteNotFoundText => $"{PasteNotFoundCount} not found";
    public int PasteAddCount => PasteRows.Count(r => r.IsReady);
    public string PasteAddText => PasteAddCount == 1 ? "Add 1 line" : $"Add {PasteAddCount} lines";
    public bool CanAddPasted => PasteAddCount > 0 && !PasteStillSearching;

    /// <summary>
    /// Opens the pasted-list box when <paramref name="text"/> holds two or more item lines, and looks each one up (the
    /// app's own item list on the server; Tally is not asked). False for a single line: it is typed into the box as usual.
    /// </summary>
    public async Task<bool> OpenPastedListAsync(string? text)
    {
        if (!CanEditContent) return false;
        var lines = PastedListParser.Parse(text);
        if (lines.Count < 2) return false;

        // Rows copied from Excel: chips show how each column was read (A Part No, B Name, C Qty, D Rate …) and can be changed
        _pastedText = text;
        PasteColumns.Clear();
        PasteIsTable = PastedListParser.IsTable(text);
        if (PasteIsTable)
        {
            var roles = PastedListParser.GuessColumns(text, out _pasteHasHeader);
            for (var c = 0; c < roles.Length; c++)
            {
                var column = new PasteColumnViewModel { Letter = ((char)('A' + Math.Min(c, 25))).ToString(), RoleIndex = (int)roles[c] };
                column.Changed = () => _ = RereadPastedColumnsAsync();
                PasteColumns.Add(column);
            }
        }
        IsPasteOpen = true;
        await LoadPasteRowsAsync(lines);
        return true;
    }

    private string? _pastedText;
    private bool _pasteHasHeader;

    /// <summary>The columns of rows pasted from Excel (empty for a WhatsApp or e-mail list).</summary>
    public ObservableCollection<PasteColumnViewModel> PasteColumns { get; } = [];
    [ObservableProperty] private bool _pasteIsTable;

    /// <summary>A column chip was changed: the rows are read again with the chosen columns.</summary>
    private async Task RereadPastedColumnsAsync()
    {
        if (!IsPasteOpen || _pastedText is null) return;
        var lines = PastedListParser.ParseTable(_pastedText, PasteColumns.Select(c => c.Role).ToList(), _pasteHasHeader);
        await LoadPasteRowsAsync(lines);
    }

    private async Task LoadPasteRowsAsync(IReadOnlyList<PastedLine> lines)
    {
        PasteRows.Clear();
        foreach (var (line, i) in lines.Select((l, i) => (l, i)))
        {
            var row = new PasteRowViewModel
            {
                No = i + 1, Raw = line.Raw, Query = line.Query, NameQuery = line.NameQuery, Quantity = line.Quantity, SearchText = line.Query,
                Rate = line.Rate, RateFromPaste = line.Rate is not null, Discount = line.Discount,
            };
            row.Changed = OnPasteRowsChanged;
            PasteRows.Add(row);
        }
        OnPasteRowsChanged();

        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(PasteRows.ToList().Select(async row =>
        {
            await gate.WaitAsync();
            try
            {
                IReadOnlyList<ProductSearchHitDto> hits;
                try { hits = await _session.Api.SearchProductsAsync(row.Query, 8); }
                catch (Exception) { hits = []; }
                // An Excel part number the item list does not know: try the row's item name
                if (hits.Count == 0 && row.NameQuery is { Length: > 0 } name)
                {
                    try { hits = await _session.Api.SearchProductsAsync(name, 8); }
                    catch (Exception) { hits = []; }
                }
                row.SetResults(hits);
            }
            finally { gate.Release(); }
        }));
        OnPasteRowsChanged();
    }

    private void OnPasteRowsChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(PasteTitle), nameof(PasteReadyCount), nameof(PasteChooseCount), nameof(PasteNotFoundCount), nameof(PasteStillSearching),
                     nameof(PasteReadyText), nameof(PasteChooseText), nameof(PasteNotFoundText), nameof(PasteAddCount), nameof(PasteAddText),
                     nameof(CanAddPasted),
                 })
            OnPropertyChanged(name);
        AddPastedLinesCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SearchPasteRow(PasteRowViewModel? row)
    {
        if (row is not null) row.IsSearching = true;
    }

    [RelayCommand]
    private void ClosePasteList()
    {
        IsPasteOpen = false;
        PasteRows.Clear();
        PasteColumns.Clear();
        PasteIsTable = false;
        _pastedText = null;
    }

    /// <summary>
    /// Adds every ready line (found or chosen, quantity above zero): with the rate and discount pasted from Excel (or typed in
    /// the box), else the item's Tally rate, as typing it would. The pasted rate is for this quotation only; Tally is not changed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddPasted))]
    private void AddPastedLines()
    {
        if (!CanEditContent) return;
        foreach (var row in PasteRows.Where(r => r.IsReady))
        {
            var p = row.Selected!.Product;
            Lines.Add(new QuotationLineViewModel
            {
                LineNo = Lines.Count + 1,
                ProductId = p.Id,
                ItemName = p.Name,
                PartNumber = p.PartNumber,
                Hsn = p.Hsn,
                GstRate = p.GstRate,
                Unit = p.Unit,
                TallyRate = p.Rate,
                Link = p.Link,
                Quantity = row.Quantity!.Value,
                Rate = row.Rate ?? (p.Rate > 0 ? InQuotationCurrency(p.Rate) ?? 0 : 0),
                DiscountPercent = row.Discount is > 0 and < 100 ? row.Discount.Value : 0,
                Description = (p.Description ?? "").Trim(),
                ZeroGst = WithoutGst,
            });
        }
        var added = PasteRows.Count(r => r.IsReady);
        ClosePasteList();
        Renumber();
        MarkDirty();
        RecalculateTotals();
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanSaveAsPdf));
        _navigator.Toast(added == 1 ? "1 line added from the pasted list." : $"{added} lines added from the pasted list.");
    }

    /// <summary>
    /// Prices copied from an Excel column and pasted into Rate: one per line, from the line being changed (or line 1)
    /// down; extra prices are left out. A notice says which lines changed, with Undo. Tally is not changed. Returns how
    /// many lines got a price (0: the text is not a column of prices, and is pasted as usual).
    /// </summary>
    public int PastePriceColumn(string? text) => PasteColumn(text, PasteColumnKind.Rate);

    /// <summary>
    /// Round 37: a column copied from Excel pasted into Rate, Qty or Disc %: one value per line, from the line being
    /// changed (or line 1) down; extra values are left out. A notice says which lines changed, with Undo. Returns how many
    /// lines got a value (0: the text is not such a column, and is pasted as usual).
    /// </summary>
    public int PasteColumn(string? text, PasteColumnKind kind)
    {
        if (!CanEditContent || Lines.Count == 0 || string.IsNullOrWhiteSpace(text)) return 0;
        var cells = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (cells.Count < 2) return 0;
        var values = new List<decimal>();
        foreach (var cell in cells)
        {
            if (!PastedListParser.TryNumber(cell.Split('\t')[0], out var value, out _, out var percent)) return 0;
            var ok = kind switch
            {
                PasteColumnKind.Rate => !percent && value >= 0,
                PasteColumnKind.Quantity => !percent && value > 0,
                _ => value >= 0 && value < 100,
            };
            if (!ok) return 0;
            values.Add(value);
        }
        var start = EditingLine is { } editing ? Math.Max(0, Lines.IndexOf(editing)) : 0;
        var changed = new List<(QuotationLineViewModel Line, decimal Old)>();
        for (var i = 0; i < values.Count && start + i < Lines.Count; i++)
        {
            var line = Lines[start + i];
            changed.Add((line, Get(line, kind)));
            Set(line, kind, values[i]);
        }
        if (changed.Count == 0) return 0;
        AfterPricesChanged();
        var lines = changed.Count == 1 ? $"line {start + 1}" : $"lines {start + 1} to {start + changed.Count}";
        var (one, many, box) = kind switch
        {
            PasteColumnKind.Rate => ("price", "prices", "Rate"),
            PasteColumnKind.Quantity => ("quantity", "quantities", "Qty"),
            _ => ("discount", "discounts", "Disc %"),
        };
        _navigator.Toast($"{changed.Count} {(changed.Count == 1 ? one : many)} pasted into {box} — {lines}", "Undo", () =>
        {
            foreach (var (line, old) in changed) Set(line, kind, old);
            AfterPricesChanged();
        }, 12);
        return changed.Count;
    }

    private static decimal Get(QuotationLineViewModel line, PasteColumnKind kind) => kind switch
    {
        PasteColumnKind.Rate => line.Rate,
        PasteColumnKind.Quantity => line.Quantity,
        _ => line.DiscountPercent,
    };

    private static void Set(QuotationLineViewModel line, PasteColumnKind kind, decimal value)
    {
        switch (kind)
        {
            case PasteColumnKind.Rate: line.Rate = value; break;
            case PasteColumnKind.Quantity: line.Quantity = value; break;
            default: line.DiscountPercent = value; break;
        }
    }

    private void AfterPricesChanged()
    {
        if (EditingLine is { } editing)
        {
            EntryRate = editing.Rate;
            EntryQuantity = editing.Quantity;
            EntryDiscount = editing.DiscountPercent;
        }
        RecalculateTotals();
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
        var pos = PlaceOfSupplyCode;
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
        ShowProductLinks = ShowProductLinks,
        GstByBuyer = GstByBuyer,
        Currency = IsUsd ? Currencies.Usd : null,
        ExchangeRate = IsUsd ? UsdRate : null,
        FromFormater = Source == QuotationSource.Formater,
        PdfLayout = _pdfLayout,
        ClientKey = Id is null ? _clientKey : null,
        Lines = Lines.Select(l => l.ToDto()).ToList(),
    };

    [RelayCommand]
    public async Task<bool> SaveAsync()
    {
        if (!CanAct) return false; // Ctrl+S on an administrator's screen, or on someone else's document
        if (EntryProduct is not null || EditingLine is not null) CommitEntry();
        InfoMessage = null;
        ValidationMessages.Clear();
        ApiException? clash = null;
        var offline = false;
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
            catch (Exception ex) when (IsUnreachable(ex))
            {
                // Round 38 (Q10): nothing is lost — kept on this PC and sent by itself when the server answers again.
                offline = true;
                return;
            }
            WaitingToSend = false;
            Load(dto);
            DropRecoveryCopy();
            MyClashVersion = null;
            // "Without GST" was for this one quotation: the server switched it off; keep the session in step.
            if (created && dto.WithoutGst && _session.Api.CurrentUser is { } me) _session.Api.UpdateCurrentUser(me with { WithoutGstNext = false });
            // "USD quotation" too was for this one quotation.
            if (created && dto.IsUsd && _session.Api.CurrentUser is { } usdMe) _session.Api.UpdateCurrentUser(usdMe with { UsdNext = false, UsdRate = 0 });
            InfoMessage = created && _provisionalNumber is { } shown && dto.Number is { } got && shown != got
                ? $"Saved as {got}. {shown} was used by another user."
                : $"Saved {dto.Number ?? "draft"} at {DateTime.Now:HH:mm}.";
        });
        if (clash is not null) return await ResolveClashAsync(clash);
        if (offline)
        {
            IsDirty = true;
            KeepRecoveryCopy();
            if (!WaitingToSend) InfoMessage = "Server not reachable: kept on this PC, it will be sent when the server is back.";
            WaitingToSend = true;
            return false;
        }
        if (ok && !_quietSave) ClickSound.Saved(); // Round 35: a soft click (not when it is part of approving)
        return ok;
    }

    /// <summary>Saving as part of approving: the approval's double click is heard instead of the save's single one.</summary>
    private bool _quietSave;

    [RelayCommand]
    private async Task ApproveAsync() => await ApproveCoreAsync(overrideStale: false, openPdf: true);

    [RelayCommand]
    private async Task ApproveAnywayAsync() => await ApproveCoreAsync(overrideStale: true, openPdf: !_saveAsPdfAfterApproval);

    private bool _saveAsPdfAfterApproval;

    /// <summary>
    /// Before approving: a GSTIN from another state, a mistyped GSTIN or a party without a state would put the wrong kind of
    /// GST on the quotation. Asks "Approve anyway" / "Go back"; true when there is nothing to check or the user goes on.
    /// </summary>
    private async Task<bool> TaxLooksRightAsync()
    {
        // No customer yet: the usual checks say "Select a customer" first.
        if (BuyerName.Trim().Length == 0) return true;
        var parties = new List<Quotation.Core.Gst.TaxCheck.Party> { new("The buyer", BuyerGstin, BuyerState, BuyerStateCode) };
        if (!ConsigneeSameAsBuyer && ConsigneeName.Trim().Length > 0)
            parties.Add(new("The consignee", ConsigneeGstin, ConsigneeState, ConsigneeStateCode));
        var problems = Quotation.Core.Gst.TaxCheck.Problems([.. parties]);
        if (problems.Count == 0) return true;
        var placeOfSupply = PlaceOfSupplyCode;
        var message = string.Join(Environment.NewLine, problems.Select(p => "• " + p)) + Environment.NewLine + Environment.NewLine +
                      "GST on this quotation: " + Quotation.Core.Gst.TaxCheck.Describe(_settings.Company.StateCode, placeOfSupply) + ".";
        return await _navigator.ConfirmAsync("Check the GST before approving", message, "Approve anyway", "Go back");
    }

    /// <summary>Approval with all checks. True when the quotation is approved afterwards.</summary>
    private async Task<bool> ApproveCoreAsync(bool overrideStale, bool openPdf)
    {
        if (!CanAct) return false; // Ctrl+Enter on an administrator's screen, or on someone else's document
        // Tally theme: asked like Tally's "Accept?" (Y = Yes, N = No).
        if (!overrideStale && ThemeService.Current == AppTheme.Tally
            && !await _navigator.ConfirmAsync("Approve ?", "Yes or No", "Yes", "No")) return false;
        if (!overrideStale && !await TaxLooksRightAsync()) return false;
        _quietSave = true;
        try
        {
            if ((IsDirty || Id is null) && !await SaveAsync()) return false;
        }
        finally
        {
            _quietSave = false;
        }
        ValidationMessages.Clear();
        NeedsStaleOverride = false;
        InfoMessage = null;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var dto = await _session.Api.ApproveQuotationAsync(Id!.Value, overrideStale, Revision);
            Load(dto);
            ClickSound.Approved(); // Round 35: two quick clicks
            if (_settings.Quotation.Greetings) _navigator.Toast("Alhamdulillah  الحمد لله", $"{dto.Number} approved.");
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
        if (SealPdf && CanSeal && SealAvailable && !SealPinOn) pin = ""; // Seal PIN switch off: sealed without asking
        else if (SealPdf && CanSeal && SealAvailable)
        {
            pin = await _navigator.AskAsync("Your seal", $"Enter your 4-digit seal PIN to seal {Number}.", "Seal PIN", secret: true, ok: "Seal & Save",
                stamp: true);
            if (pin is null) return;
        }
        await RunAsync(async () =>
        {
            // In the user's own layout (My Settings → PDF layout: Tally or Zoho).
            var bytes = pin is null ? await _session.Api.QuotationPdfAsync(Id.Value) : await _session.Api.SealedPdfAsync(Id.Value, pin.Trim());
            // My Settings → "Open the PDF after Save as PDF" (on by default): the saved file also opens.
            LastPdfPath = _session.Api.CurrentUser?.OpenPdfAfterSave != false
                ? _session.Documents.SaveAndOpen(bytes, PdfFileName(), folder: _session.Documents.FolderFor(IsProforma))
                : _session.Documents.Save(bytes, PdfFileName(), _session.Documents.FolderFor(IsProforma));
            InfoMessage = (pin is null ? "Saved to " : "Sealed and saved to ") + LastPdfPath;
        });
    }

    /// <summary>
    /// "TSQ2526-3403 TATA Electronics System Solutions.pdf", or "TATA Electronics System Solutions TSQ2526-3403.pdf" when the
    /// user chose customer first (My Settings); characters Windows does not allow become "-".
    /// </summary>
    public string PdfFileName() => PdfFileName(Number ?? "draft", BuyerName, CustomerFirstInFileNames(_session));

    public static bool CustomerFirstInFileNames(AppSession session) =>
        session.Api.CurrentUser?.PdfFileOrder == PdfFileOrders.CustomerFirst;

    private const string NotInFileNames = "\\/:*?\"<>|";

    public static string PdfFileName(string number, string customer, bool customerFirst = false)
    {
        var name = new string(customer.Select(c => char.IsControl(c) ? ' ' : NotInFileNames.Contains(c) ? '-' : c).ToArray()).Trim();
        while (name.Contains("  ", StringComparison.Ordinal)) name = name.Replace("  ", " ");
        if (name.Length > 60) name = name[..60].TrimEnd();
        var file = QuotationNumberFormatter.ToFileName(number);
        if (name.Length == 0) return file.TrimEnd('.', ' ') + ".pdf";
        return (customerFirst ? $"{name.TrimEnd('.', ' ')} {file}" : $"{file} {name}").TrimEnd('.', ' ') + ".pdf";
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

    /// <summary>
    /// Ctrl+P: straight to the default printer, no questions. Approved and unchanged: its PDF; a draft (or changed since
    /// approval): what is on the screen, marked DRAFT, without saving anything.
    /// </summary>
    [RelayCommand]
    private async Task PrintShortcutAsync()
    {
        if (Id is null && Lines.Count == 0) return;
        await RunAsync(async () =>
        {
            byte[] pdf;
            string name;
            if (IsFinal && Id is { } id && !HasUnsavedChanges)
            {
                pdf = await _session.Api.QuotationPdfAsync(id);
                name = PdfFileName();
            }
            else
            {
                pdf = await _session.Api.PreviewUnsavedPdfAsync(BuildRequest());
                name = "DRAFT " + (Number ?? "quotation");
            }
            try
            {
                var printer = await Task.Run(() => _session.Documents.Print(pdf, name));
                _navigator.Toast($"Sent to printer {printer}.");
            }
            catch (Exception ex) when (ex is not ApiException)
            {
                ErrorMessage = "Could not print: " + ex.Message;
            }
        });
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
            // Nothing saved yet: stay on this page. Nothing typed: nothing to do; otherwise it starts again blank
            // (no number is used; the copy kept for a crash is thrown away).
            if (!IsDirty && Lines.Count == 0 && EntryProduct is null && string.IsNullOrWhiteSpace(ProductSearchText)) return;
            // Something typed: ask first (Round 35), so typed work is never thrown away by mistake
            if (!await _navigator.ConfirmAsync("Discard this quotation?", "Nothing is saved; the number is not used.", "Discard", "No")) return;
            ClearEntry();
            IsDirty = false;
            DropRecoveryCopy();
            _navigator.StartOver(IsProforma);
            return;
        }
        Load(await _session.Api.CancelQuotationAsync(Id.Value));
        DropRecoveryCopy();
        InfoMessage = $"{Number} cancelled. The number is not reused.";
    });

    [RelayCommand]
    private void NewQuotation()
    {
        if (MakesDocuments) _navigator.Navigate(IsProforma ? "newproforma" : "new");
    }

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

/// <summary>One choice of the gear next to Items.</summary>
public sealed partial class LineStepOption(string key, string name) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isSelected;
}

/// <summary>Round 36: one item of a Tally invoice in the "Copy from Tally invoice" window.</summary>
public sealed partial class InvoiceItemViewModel : ObservableObject
{
    public InvoiceItemViewModel(TallyInvoiceLineDto line, decimal? todayRate, decimal invoiceRate)
    {
        Line = line;
        TodayRate = todayRate;
        InvoiceRate = invoiceRate;
        _isTicked = line.Product is not null;
    }

    public TallyInvoiceLineDto Line { get; }
    public ProductSummaryDto? Product => Line.Product;
    public string ItemName => Line.ItemName;
    public decimal Quantity => Line.Quantity;
    public string QuantityText => $"{Line.Quantity:0.###} {Line.Unit}".Trim();
    public decimal InvoiceRate { get; }
    public decimal? TodayRate { get; }
    public bool InTally => Product is not null;
    public bool NotInTally => Product is null;
    public double RowOpacity => InTally ? 1 : 0.55;
    public string InvoiceRateText => IndianFormat.Amount(InvoiceRate);
    public string TodayRateText => NotInTally ? "skipped" : TodayRate is { } r ? IndianFormat.Amount(r) : "—";
    /// <summary>"+4%" when today's rate is higher than the invoice's (orange), "−3%" when lower.</summary>
    public string ChangeText => TodayRate is { } r && InvoiceRate > 0 && r != InvoiceRate
        ? (r > InvoiceRate ? "+" : "−") + Math.Abs(Math.Round((r - InvoiceRate) * 100 / InvoiceRate, 0)).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%"
        : "";
    public bool HasChange => ChangeText.Length > 0;

    [ObservableProperty] private bool _isTicked;
}

/// <summary>Round 37: the box a column from Excel is pasted into.</summary>
public enum PasteColumnKind { Rate, Quantity, Discount }
