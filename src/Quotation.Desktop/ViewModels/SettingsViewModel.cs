using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSession _session;

    [ObservableProperty] private AllSettingsDto? _settings;
    [ObservableProperty] private string _addressLines = "";
    [ObservableProperty] private string _terms = "";
    [ObservableProperty] private string _customerGroups = "";
    [ObservableProperty] private string _lastRateVoucherTypes = "";
    [ObservableProperty] private string _fyOverride = "";
    [ObservableProperty] private string _newApiKey = "";
    [ObservableProperty] private string _newGmailClientJson = "";

    // Users tab
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newDisplayName = "";
    [ObservableProperty] private string _newUserPassword = "";
    public ObservableCollection<UserDto> Users { get; } = [];

    // Users page (layout B): people on the left with a search box, the chosen person on the right
    [ObservableProperty] private string _userFilter = "";
    [ObservableProperty] private UserDto? _selectedUser;
    [ObservableProperty] private bool _isAddingUser;
    public ObservableCollection<UserDto> FilteredUsers { get; } = [];
    /// <summary>Nobody chosen and not adding: a short hint on the right.</summary>
    public bool ShowUserHint => !IsAddingUser && EditingUser is null;
    partial void OnUserFilterChanged(string value) => FilterUsers();
    partial void OnSelectedUserChanged(UserDto? value)
    {
        if (value is null) return;
        IsAddingUser = false;
        if (EditingUser?.Id != value.Id || EditingUser != value) EditUser(value);
    }
    partial void OnIsAddingUserChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUserHint));
        OnPropertyChanged(nameof(CanDeleteEditingUser));
    }

    private void FilterUsers()
    {
        var f = UserFilter.Trim();
        var keep = SelectedUser?.Id;
        FilteredUsers.Clear();
        foreach (var u in Users.Where(u => f.Length == 0 || u.Username.Contains(f, StringComparison.OrdinalIgnoreCase)
                                                   || u.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)))
            FilteredUsers.Add(u);
        SelectedUser = FilteredUsers.FirstOrDefault(u => u.Id == keep);
    }

    /// <summary>"+ Add": the right side shows the new-user form.</summary>
    [RelayCommand]
    private void StartAddUser()
    {
        SelectedUser = null;
        EditingUser = null;
        IsAddingUser = true;
        NewUsername = NewDisplayName = NewUserPassword = "";
    }

    /// <summary>Two letters for the round badge: "Ravi Kumar" → "RK".</summary>
    public static Avalonia.Data.Converters.FuncValueConverter<UserDto?, string> Initials { get; } = new(u =>
    {
        var words = (u?.DisplayName ?? u?.Username ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "?" : string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    });
    public static Avalonia.Data.Converters.FuncValueConverter<UserDto?, string> StatusText { get; } = new(u => u is { IsActive: false } ? "Inactive" : "Active");
    public static Avalonia.Data.Converters.FuncValueConverter<UserDto?, bool> IsInactive { get; } = new(u => u is { IsActive: false });
    public static Avalonia.Data.Converters.FuncValueConverter<UserDto?, bool> IsAdminUser { get; } = new(u => u is { Role: UserRole.Admin });
    /// <summary>"Seal ready" / "No seal" … tag of a Standard user.</summary>
    public static Avalonia.Data.Converters.FuncValueConverter<UserDto?, string> SealTag { get; } = new(u => u switch
    {
        null or { Role: UserRole.Admin } => "",
        { HasSealImage: true, HasSealPin: true } => "Seal ready",
        { HasSealImage: true } => "Seal: no PIN",
        { HasSealPin: true } => "Seal: no picture",
        _ => "No seal",
    });

    // Editing a user (administrator)
    [ObservableProperty] private UserDto? _editingUser;
    [ObservableProperty] private string _editUsername = "";
    [ObservableProperty] private string _editDisplayName = "";
    [ObservableProperty] private bool _editIsAdmin;
    [ObservableProperty] private bool _editIsActive;
    [ObservableProperty] private string _resetPassword = "";
    public bool IsEditingUser => EditingUser is not null;
    partial void OnEditingUserChanged(UserDto? value)
    {
        OnPropertyChanged(nameof(IsEditingUser));
        OnPropertyChanged(nameof(ShowUserHint));
        OnPropertyChanged(nameof(EditingUserCanHaveSeal));
        OnPropertyChanged(nameof(CanDeleteEditingUser));
        OnPropertyChanged(nameof(UserSealState));
        OnPropertyChanged(nameof(UserHasAnySeal));
        OnPropertyChanged(nameof(UserSealReady));
    }

    // Personal seal of the user being edited (users only; administrators do not seal)
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _userSealImage;
    [ObservableProperty] private string _newUserSealPin = "";
    public bool HasUserSealImage => UserSealImage is not null;
    partial void OnUserSealImageChanged(Avalonia.Media.Imaging.Bitmap? value) => OnPropertyChanged(nameof(HasUserSealImage));
    public bool EditingUserCanHaveSeal => EditingUser is { Role: not UserRole.Admin };

    /// <summary>Users (not the administrator) can be deleted; not while adding a new one.</summary>
    public bool CanDeleteEditingUser => EditingUser is { Role: not UserRole.Admin } && !IsAddingUser;

    /// <summary>Deletes the user after asking: signed out everywhere; their quotations keep their name.</summary>
    [RelayCommand]
    private async Task DeleteUserAsync()
    {
        if (EditingUser is not { Role: not UserRole.Admin } user || IsAddingUser) return;
        var name = user.DisplayName.Length > 0 && user.DisplayName != user.Username ? $"{user.Username} ({user.DisplayName})" : user.Username;
        if (_navigator is not null && !await _navigator.ConfirmAsync("Delete user",
                $"Delete {name}? They can no longer sign in. Their quotations stay, with their name.", "Delete", "Cancel"))
            return;
        InfoMessage = null;
        if (await RunAsync(async () =>
            {
                await _session.Api.DeleteUserAsync(user.Id);
                EditingUser = null;
                SelectedUser = null;
                await LoadUsersAsync();
            }))
            InfoMessage = $"User '{user.Username}' deleted.";
    }
    public bool UserHasAnySeal => EditingUser is { } u && (u.HasSealImage || u.HasSealPin);
    public bool UserSealReady => EditingUser is { HasSealImage: true, HasSealPin: true };
    public string UserSealState => EditingUser switch
    {
        null => "",
        { HasSealImage: true, HasSealPin: true } => "Seal ready: this user can seal PDFs.",
        { HasSealImage: true } => "Picture saved. Type a 4-digit PIN and press Save user to finish.",
        { HasSealPin: true } => "PIN saved. Choose a picture to finish.",
        _ => "No seal yet: choose a picture and type a PIN, then Save user.",
    };

    /// <summary>"Seal: ready" / "Seal: no PIN" … in the list of users.</summary>
    public static Avalonia.Data.Converters.FuncValueConverter<UserDto?, string> SealStateText { get; } = new(u => u switch
    {
        null or { Role: UserRole.Admin } => "",
        { HasSealImage: true, HasSealPin: true } => "Seal: ready",
        { HasSealImage: true } => "Seal: no PIN",
        { HasSealPin: true } => "Seal: no picture",
        _ => "Seal: none",
    });

    // Seal size and position on PDFs (administrator)
    // Round 40 (SL-1): the boxes show the Tally or the Zoho seal numbers; the other layout's are kept here meanwhile.
    private (decimal? Width, decimal? OffsetX, decimal? MoveUp) _tallySeal, _zohoSeal;
    [ObservableProperty] private bool _sealForZoho;
    public bool SealForTally { get => !SealForZoho; set { if (value) SealForZoho = false; } }
    public string SealPreviewText => SealForZoho ? "Preview on a Zoho page" : "Preview on a Tally page";
    private string SealLayout => SealForZoho ? PdfLayouts.Zoho : PdfLayouts.Tally;

    partial void OnSealForZohoChanging(bool value)
    {
        if (value == SealForZoho) return;
        if (SealForZoho) _zohoSeal = (SealWidth, SealOffsetX, SealMoveUp); else _tallySeal = (SealWidth, SealOffsetX, SealMoveUp);
    }

    partial void OnSealForZohoChanged(bool value)
    {
        (SealWidth, SealOffsetX, SealMoveUp) = value ? _zohoSeal : _tallySeal;
        OnPropertyChanged(nameof(SealForTally));
        OnPropertyChanged(nameof(SealPreviewText));
    }

    /// <summary>Both layouts' numbers, with the boxes' values for the one shown.</summary>
    private ((decimal? Width, decimal? OffsetX, decimal? MoveUp) Tally, (decimal? Width, decimal? OffsetX, decimal? MoveUp) Zoho) BothSeals() =>
        SealForZoho ? (_tallySeal, (SealWidth, SealOffsetX, SealMoveUp)) : ((SealWidth, SealOffsetX, SealMoveUp), _zohoSeal);

    [ObservableProperty] private decimal? _sealWidth;
    [ObservableProperty] private decimal? _sealOffsetX;
    /// <summary>Points the seal is moved up (negative = down); stored as the downward offset.</summary>
    [ObservableProperty] private decimal? _sealMoveUp;
    [ObservableProperty] private string _hiddenMarkers = "";
    /// <summary>Sign-off lines of the Zoho layout, one per row.</summary>

    // Numbering
    [ObservableProperty] private NumberSeriesDto? _series;
    [ObservableProperty] private decimal? _newNextSequence;
    [ObservableProperty] private NumberSeriesDto? _proformaSeries;
    [ObservableProperty] private decimal? _newNextProformaSequence;

    public bool IsAdmin => _session.IsAdmin;
    public bool IsReadOnly => !IsAdmin;
    public IReadOnlyList<TaxPresentation> TaxPresentations { get; } = Enum.GetValues<TaxPresentation>();

    /// <summary>Tally's "Display name for Parties", in the order of <see cref="PartyDisplayName"/>.</summary>
    public IReadOnlyList<string> PartyDisplayNames { get; } = ["Name Only", "Alias Only", "Name (Alias)", "Alias (Name)"];

    public int PartyDisplayNameIndex
    {
        get => (int)(Settings?.Quotation.PartyPrint.DisplayName ?? PartyDisplayName.NameOnly);
        set
        {
            if (Settings is null || value < 0) return;
            Settings.Quotation.PartyPrint.DisplayName = (PartyDisplayName)value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> PdfLayoutChoices { get; } = PdfLayouts.All;

    /// <summary>Round 37: the note on a USD quotation's PDF (Settings → PDF).</summary>
    public IReadOnlyList<string> UsdPdfNoteChoices { get; } =
    [
        "Amounts in US dollars (USD), without GST. 1 USD = ₹ 84.20.",
        "Amounts in US dollars (USD).",
        "No note",
    ];

    public int UsdPdfNoteIndex
    {
        get => Math.Max(0, Array.IndexOf(UsdPdfNotes.All, Settings?.Quotation.UsdPdfNote ?? UsdPdfNotes.Full));
        set
        {
            if (Settings is null || value < 0 || value >= UsdPdfNotes.All.Length) return;
            Settings.Quotation.UsdPdfNote = UsdPdfNotes.All[value];
            OnPropertyChanged();
        }
    }

    /// <summary>"Show Consignee details", in the order of <see cref="ConsigneePrint"/>.</summary>
    public IReadOnlyList<string> ConsigneeChoices { get; } = ["Yes", "No", "Only when different from Buyer", "Buyer/Consignee when same"];

    public int ConsigneeChoiceIndex
    {
        get => (int)(Settings?.Quotation.PartyPrint.EffectiveConsignee ?? ConsigneePrint.CombinedWhenSame);
        set
        {
            if (Settings is null || value < 0) return;
            Settings.Quotation.PartyPrint.Consignee = (ConsigneePrint)value;
            Settings.Quotation.PartyPrint.ShowConsignee = null; // replaces the older Yes/No setting
            OnPropertyChanged();
        }
    }

    partial void OnSettingsChanged(AllSettingsDto? value)
    {
        OnPropertyChanged(nameof(UsdPdfNoteIndex));
        OnPropertyChanged(nameof(PartyDisplayNameIndex));
        OnPropertyChanged(nameof(ConsigneeChoiceIndex));
        OnPropertyChanged(nameof(UpiNoteQuotation));
        OnPropertyChanged(nameof(UpiNoteOrder));
        OnPropertyChanged(nameof(UpiNoteBoth));
    }

    // ---------------------------------------------------------------- Pay by UPI: payment note and Test QR

    public bool UpiNoteQuotation { get => Settings?.Company.UpiNote == UpiNoteKind.QuotationNumber; set => SetUpiNote(value, UpiNoteKind.QuotationNumber); }
    public bool UpiNoteOrder { get => Settings?.Company.UpiNote == UpiNoteKind.OrderNumber; set => SetUpiNote(value, UpiNoteKind.OrderNumber); }
    public bool UpiNoteBoth { get => Settings?.Company.UpiNote == UpiNoteKind.Both; set => SetUpiNote(value, UpiNoteKind.Both); }

    private void SetUpiNote(bool chosen, UpiNoteKind kind)
    {
        if (!chosen || Settings is null) return;
        Settings.Company.UpiNote = kind;
        OnPropertyChanged(nameof(UpiNoteQuotation));
        OnPropertyChanged(nameof(UpiNoteOrder));
        OnPropertyChanged(nameof(UpiNoteBoth));
    }

    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _testQrImage;
    [ObservableProperty] private bool _isTestQrOpen;
    [ObservableProperty] private string _testQrPayee = "";
    [ObservableProperty] private string _testQrUpiId = "";
    public const string TestQrNote = "Test from Quotations";

    /// <summary>
    /// A ₹1 QR from what is typed (before Save): scanning it with a phone shows the name and UPI ID the customers' apps will
    /// show, so a mistyped UPI ID is found before the QR goes on quotations. Made on this PC; nothing is sent anywhere.
    /// </summary>
    [RelayCommand]
    private void ShowTestQr()
    {
        if (Settings is null) return;
        var upi = (Settings.Company.UpiId ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(upi, @"^[A-Za-z0-9._-]{2,256}@[A-Za-z0-9.-]{2,64}$"))
        {
            ErrorMessage = "Type the UPI ID first (like name@bank, e.g. yourcompany@okhdfcbank).";
            return;
        }
        ErrorMessage = null;
        var link = UpiLink.Build(upi, Settings.Company.CompanyName, 1m, TestQrNote);
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(10);
        TestQrImage = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
        TestQrPayee = string.IsNullOrWhiteSpace(Settings.Company.CompanyName) ? upi : Settings.Company.CompanyName.Trim();
        TestQrUpiId = upi;
        IsTestQrOpen = true;
    }

    [RelayCommand]
    private void CloseTestQr() => IsTestQrOpen = false;
    public IReadOnlyList<string> RateSources { get; } = ["StandardPrice", "PriceLevel"];

    public string ExampleNumber
    {
        get
        {
            if (Settings is null) return "";
            try
            {
                var fy = FinancialYear.For(DateOnly.FromDateTime(DateTime.Today), Settings.Quotation.FinancialYearStartMonth);
                return Core.Numbering.QuotationNumberFormatter.Format(Settings.Quotation.NumberPattern, fy, 3247);
            }
            catch (Exception ex)
            {
                return "Invalid pattern: " + ex.Message;
            }
        }
    }

    private readonly INavigator? _navigator;

    public SettingsViewModel(AppSession session, INavigator? navigator = null)
    {
        _session = session;
        _navigator = navigator;
        _ = LoadAsync();
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var s = await _session.Api.SettingsAsync();
        AddressLines = string.Join(Environment.NewLine, s.Company.AddressLines);
        Terms = string.Join(Environment.NewLine, s.Company.TermsAndConditions);
        CustomerGroups = string.Join(Environment.NewLine, s.Tally.CustomerGroups);
        LastRateVoucherTypes = string.Join(Environment.NewLine, s.Tally.LastRateVoucherTypes);
        HiddenMarkers = string.Join(Environment.NewLine, s.Tally.HiddenProductMarkers);
        var zoho = s.Company.ZohoSeal();
        _tallySeal = ((decimal)s.Company.SealWidth, (decimal)s.Company.SealOffsetX, -(decimal)s.Company.SealOffsetY);
        _zohoSeal = ((decimal)zoho.Width, (decimal)zoho.OffsetX, -(decimal)zoho.OffsetY);
        (SealWidth, SealOffsetX, SealMoveUp) = SealForZoho ? _zohoSeal : _tallySeal;
        FyOverride = s.Tally.ActiveFinancialYearOverride?.ToString() ?? "";
        Settings = s;
        OnPropertyChanged(nameof(ExampleNumber));
        if (IsAdmin)
        {
            await LoadUsersAsync();
            Series = await _session.Api.NumberSeriesAsync();
            NewNextSequence = Series.NextSequence;
            ProformaSeries = await _session.Api.NumberSeriesAsync(type: DocumentType.ProformaInvoice);
            NewNextProformaSequence = ProformaSeries.NextSequence;
            await LoadDriveAsync();
        }
        _session.Settings = s;
    });

    // ------------------------------------------------------------------ Google Drive backup (Backup & updates)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DriveConnected), nameof(DriveNotConnected), nameof(DriveAccountText), nameof(DriveLastCopyText),
        nameof(DriveError), nameof(HasDriveError))]
    private DriveBackupStatusDto? _drive;

    [ObservableProperty] private bool _driveBusy;

    public bool DriveConnected => Drive?.Connected == true;
    public bool DriveNotConnected => Drive is { Connected: false };
    public string DriveAccountText => Drive?.Account is { Length: > 0 } a ? a : DriveConnected ? "Google account" : "Connect it on the Gmail Connection page";

    public string DriveLastCopyText => Drive switch
    {
        null => "",
        { LastCopyLocal: { } at } d => When(at) + " · " + d.LastCopyText + (d.PdfsWaiting > 0 ? $" ({d.PdfsWaiting} PDFs to go)" : ""),
        { PdfsWaiting: > 0 } d => $"Not yet ({d.PdfsWaiting} PDFs to copy)",
        _ => "Not yet",
    };

    public string? DriveError => Drive?.Error;
    public bool HasDriveError => !string.IsNullOrEmpty(Drive?.Error);

    private static string When(DateTime at) =>
        at.Date == DateTime.Today ? "Today " + at.ToString("HH:mm") :
        at.Date == DateTime.Today.AddDays(-1) ? "Yesterday " + at.ToString("HH:mm") : at.ToString("dd-MMM-yy HH:mm");

    private async Task LoadDriveAsync()
    {
        try { Drive = await _session.Api.DriveBackupAsync(); }
        catch (Quotation.ApiClient.ApiException) { Drive = null; } // a server without Google Drive backup (not updated yet): no card
    }

    /// <summary>Back up now: a new database backup, then everything new goes to Google Drive.</summary>
    [RelayCommand]
    private async Task DriveBackupNowAsync()
    {
        if (DriveBusy) return;
        DriveBusy = true;
        InfoMessage = null;
        try
        {
            if (await RunAsync(async () => Drive = await _session.Api.DriveBackupNowAsync()) && !HasDriveError)
                InfoMessage = "Copied to Google Drive: " + Drive!.LastCopyText + ".";
        }
        finally
        {
            DriveBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetNextNumberAsync()
    {
        if (Series is null || NewNextSequence is null) return;
        InfoMessage = null;
        if (await RunAsync(async () => Series = await _session.Api.SetNextSequenceAsync(Series.FinancialYearStart, (int)NewNextSequence.Value)))
        {
            InfoMessage = $"Next quotation number for {Series!.FinancialYear} will be {Series.NextNumberPreview}.";
        }
    }

    [RelayCommand]
    private async Task SetNextProformaNumberAsync()
    {
        if (ProformaSeries is null || NewNextProformaSequence is null) return;
        InfoMessage = null;
        if (await RunAsync(async () => ProformaSeries = await _session.Api.SetNextSequenceAsync(
                ProformaSeries.FinancialYearStart, (int)NewNextProformaSequence.Value, DocumentType.ProformaInvoice)))
        {
            InfoMessage = $"Next proforma invoice number for {ProformaSeries!.FinancialYear} will be {ProformaSeries.NextNumberPreview}.";
        }
    }

    [RelayCommand]
    private void RefreshExample() => OnPropertyChanged(nameof(ExampleNumber));

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Settings is null) return;
        InfoMessage = null;
        Settings.Company.AddressLines = SplitLines(AddressLines);
        Settings.Company.TermsAndConditions = SplitLines(Terms);
        Settings.Tally.CustomerGroups = SplitLines(CustomerGroups);
        Settings.Tally.LastRateVoucherTypes = SplitLines(LastRateVoucherTypes);
        Settings.Tally.HiddenProductMarkers = SplitLines(HiddenMarkers);
        var (tally, zohoSeal) = BothSeals();
        Settings.Company.SealWidth = (double)(tally.Width ?? 150);
        Settings.Company.SealOffsetX = (double)(tally.OffsetX ?? 0);
        Settings.Company.SealOffsetY = -(double)(tally.MoveUp ?? 0);
        Settings.Company.ZohoSealWidth = (double)(zohoSeal.Width ?? 150);
        Settings.Company.ZohoSealOffsetX = (double)(zohoSeal.OffsetX ?? 0);
        Settings.Company.ZohoSealOffsetY = -(double)(zohoSeal.MoveUp ?? 0);
        if (string.IsNullOrWhiteSpace(FyOverride))
        {
            Settings.Tally.ActiveFinancialYearOverride = null;
        }
        else if (int.TryParse(FyOverride.Trim(), out var y) && y is > 2000 and < 2100)
        {
            Settings.Tally.ActiveFinancialYearOverride = y;
        }
        else
        {
            ErrorMessage = "Financial year override must be a start year such as 2025, or empty.";
            return;
        }
        Settings.Ai.ApiKey = string.IsNullOrWhiteSpace(NewApiKey) ? null : NewApiKey.Trim();
        Settings.Gmail.ClientSecretJson = string.IsNullOrWhiteSpace(NewGmailClientJson) ? null : NewGmailClientJson.Trim();

        if (await RunAsync(async () => Settings = await _session.Api.SaveSettingsAsync(Settings)))
        {
            NewApiKey = "";
            NewGmailClientJson = "";
            _session.Settings = Settings;
            InfoMessage = "Settings saved.";
            OnPropertyChanged(nameof(ExampleNumber));
        }
    }

    private async Task LoadUsersAsync()
    {
        Users.Clear();
        foreach (var u in await _session.Api.UsersAsync()) Users.Add(u);
        FilterUsers();
    }

    [RelayCommand]
    private async Task AddUserAsync()
    {
        InfoMessage = null;
        UserDto? created = null;
        var ok = await RunAsync(async () =>
        {
            created = await _session.Api.CreateUserAsync(new CreateUserRequest(NewUsername, NewDisplayName, NewUserPassword,
                UserRole.User)); // one administrator: new users are Standard users
            await LoadUsersAsync();
        });
        if (ok)
        {
            var name = NewUsername;
            NewUsername = NewDisplayName = NewUserPassword = "";
            IsAddingUser = false;
            UserFilter = "";
            SelectedUser = FilteredUsers.FirstOrDefault(u => u.Id == created?.Id); // straight on to their seal and details
            InfoMessage = $"User '{name}' created. They must change the password at first login.";
        }
    }

    [RelayCommand]
    private Task ToggleUserAsync(UserDto user) => RunAsync(async () =>
    {
        await _session.Api.SetUserActiveAsync(user.Id, !user.IsActive);
        await LoadUsersAsync();
    });

    [RelayCommand]
    private void EditUser(UserDto? user)
    {
        EditingUser = user;
        if (user is null) return;
        IsAddingUser = false;
        if (SelectedUser?.Id != user.Id) SelectedUser = FilteredUsers.FirstOrDefault(u => u.Id == user.Id) ?? SelectedUser;
        EditUsername = user.Username;
        EditDisplayName = user.DisplayName;
        EditIsAdmin = user.Role == UserRole.Admin;
        EditIsActive = user.IsActive;
        ResetPassword = "";
        NewUserSealPin = "";
        InfoMessage = null;
        ErrorMessage = null;
        _ = LoadUserSealImageAsync(user);
    }

    [RelayCommand]
    private void CancelEditUser()
    {
        EditingUser = null;
        SelectedUser = null;
        IsAddingUser = false;
    }

    /// <summary>Saves login name, display name, role and active; old quotations keep the name used at the time.</summary>
    [RelayCommand]
    private async Task SaveUserAsync()
    {
        if (EditingUser is not { } user) return;
        InfoMessage = null;
        var pin = NewUserSealPin.Trim();
        if (pin.Length > 0 && (pin.Length != 4 || !pin.All(char.IsAsciiDigit)))
        {
            ErrorMessage = "The seal PIN must be exactly 4 digits.";
            return;
        }
        string? sealNote = null;
        if (await RunAsync(async () =>
            {
                var saved = await _session.Api.UpdateUserAsync(user.Id,
                    new UpdateUserRequest(EditUsername, EditDisplayName, EditIsAdmin ? UserRole.Admin : UserRole.User, EditIsActive));
                // The seal PIN typed in the Seal section, and the seal size and place.
                if (pin.Length > 0 && saved.Role != UserRole.Admin) saved = await _session.Api.SetUserSealPinAsync(user.Id, pin);
                await SaveSealPositionAsync();
                _session.Api.UpdateCurrentUser(saved);
                NewUserSealPin = "";
                await LoadUsersAsync();
                if (saved.Role != UserRole.Admin)
                    sealNote = saved switch
                    {
                        { HasSealImage: true, HasSealPin: true } => " Seal ready: they can seal PDFs.",
                        { HasSealImage: true } => " Seal picture saved, but no PIN yet.",
                        { HasSealPin: true } => " Seal PIN saved, but no picture yet.",
                        _ => "",
                    };
            }))
        {
            var name = EditUsername.Trim();
            // Stay on this person (layout B): the list was read again, so pick them in it again.
            var again = FilteredUsers.FirstOrDefault(u => u.Id == user.Id);
            SelectedUser = null;
            SelectedUser = again;
            InfoMessage = $"User '{name}' saved." + sealNote;
        }
    }

    /// <summary>Saves the seal size and place (Tally and Zoho each) when they were changed on screen (the same for every seal).</summary>
    private async Task SaveSealPositionAsync()
    {
        if (Settings is null) return;
        var (tally, zoho) = BothSeals();
        var c = Settings.Company;
        var t = new SealPositionDto((double)(tally.Width ?? 150), (double)(tally.OffsetX ?? 0), -(double)(tally.MoveUp ?? 0), PdfLayouts.Tally);
        if ((c.SealWidth, c.SealOffsetX, c.SealOffsetY) != (t.Width, t.OffsetX, t.OffsetY))
        {
            // The server gives the Zoho seal its own numbers before the Tally ones move.
            if (c.ZohoSealWidth is null) (c.ZohoSealWidth, c.ZohoSealOffsetX, c.ZohoSealOffsetY) = c.ZohoSeal();
            await _session.Api.SaveSealPositionAsync(t);
            (c.SealWidth, c.SealOffsetX, c.SealOffsetY) = (t.Width, t.OffsetX, t.OffsetY);
        }
        var z = new SealPositionDto((double)(zoho.Width ?? 150), (double)(zoho.OffsetX ?? 0), -(double)(zoho.MoveUp ?? 0), PdfLayouts.Zoho);
        if (c.ZohoSeal() != (z.Width, z.OffsetX, z.OffsetY) || c.ZohoSealWidth is null)
        {
            await _session.Api.SaveSealPositionAsync(z);
            (c.ZohoSealWidth, c.ZohoSealOffsetX, c.ZohoSealOffsetY) = (z.Width, z.OffsetX, z.OffsetY);
        }
    }

    /// <summary>Temporary password: the user must choose a new one at the next login; logged out on every PC now.</summary>
    [RelayCommand]
    private async Task ResetUserPasswordAsync()
    {
        if (EditingUser is not { } user) return;
        InfoMessage = null;
        if (ResetPassword.Length < 4)
        {
            ErrorMessage = "The temporary password must have at least 4 characters.";
            return;
        }
        if (await RunAsync(() => _session.Api.ResetPasswordAsync(user.Id, ResetPassword)))
        {
            InfoMessage = $"Password of '{user.Username}' reset. They log in with the temporary password and then choose a new one.";
            ResetPassword = "";
        }
    }

    // ---------------------------------------------------------------- personal seals

    private async Task LoadUserSealImageAsync(UserDto user)
    {
        UserSealImage = null;
        if (!user.HasSealImage) return;
        try
        {
            using var ms = new MemoryStream(await _session.Api.UserSealImageAsync(user.Id));
            if (EditingUser?.Id == user.Id) UserSealImage = new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch (Exception)
        {
            // Preview only.
        }
    }

    /// <summary>After a seal change: the user's row, the edit box and "Preview on a page" follow.</summary>
    private async Task SealChangedAsync(UserDto saved, string message)
    {
        EditingUser = saved;
        await LoadUsersAsync();
        Settings = await _session.Api.SettingsAsync();
        _session.Settings = Settings;
        await LoadUserSealImageAsync(saved);
        InfoMessage = message;
    }

    /// <summary>Called by the view with the chosen PNG/JPG file.</summary>
    /// <summary>Round 38 (PL-1): what loading the product links did ("520 linked · 12 not found: …").</summary>
    [ObservableProperty] private string _productLinksResult = "";

    /// <summary>Round 38 (PL-1): reads the Excel (column A product code, column B link) and saves the links on the server.</summary>
    public Task LoadProductLinksAsync(byte[] file, string fileName) => RunAsync(async () =>
    {
        List<(string Code, string Url)> rows;
        try { rows = Core.Import.LinkSheet.Read(file, fileName); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException)
        {
            ProductLinksResult = "This file could not be read. Save it as an Excel workbook (.xlsx) or CSV: column A product code, column B link.";
            return;
        }
        if (rows.Count == 0)
        {
            ProductLinksResult = "No rows found. Column A = product code, column B = link.";
            return;
        }
        var r = await _session.Api.LoadProductLinksAsync(rows.Select(x => new ProductLinkRowDto(x.Code, x.Url)).ToList());
        var text = $"{r.Linked} of {r.Rows} items linked.";
        if (r.NotFound.Count > 0) text += $" {r.NotFound.Count} codes not found: " + string.Join(", ", r.NotFound.Take(30)) + (r.NotFound.Count > 30 ? " …" : "");
        if (r.BadLinks.Count > 0) text += $" {r.BadLinks.Count} without a web link (http/https): " + string.Join(", ", r.BadLinks.Take(10)) + (r.BadLinks.Count > 10 ? " …" : "");
        ProductLinksResult = text;
        InfoMessage = $"Product links loaded: {r.Linked} items.";
    });

    public Task UploadUserSealAsync(byte[] image) => RunAsync(async () =>
    {
        if (EditingUser is not { } user) return;
        var saved = await _session.Api.UploadUserSealAsync(user.Id, image);
        await SealChangedAsync(saved, saved.HasSealPin
            ? $"Seal picture of '{user.Username}' saved."
            : $"Seal picture of '{user.Username}' saved. Now type their 4-digit seal PIN and press Save user.");
    });

    [RelayCommand]
    private Task RemoveUserSealAsync() => RunAsync(async () =>
    {
        if (EditingUser is not { } user) return;
        var saved = await _session.Api.RemoveUserSealAsync(user.Id);
        await SealChangedAsync(saved, $"Seal of '{user.Username}' removed: they can no longer seal PDFs.");
    });

    [RelayCommand]
    private Task PreviewUserSealAsync() => RunAsync(async () =>
    {
        if (EditingUser is not { } user) return;
        var bytes = await _session.Api.SealPreviewAsync((double)(SealWidth ?? 150), (double)(SealOffsetX ?? 0), -(double)(SealMoveUp ?? 0), userId: user.Id,
            layout: SealLayout);
        _session.Documents.SaveAndOpen(bytes, $"Seal-preview-{SealLayout}-{user.Username}.pdf");
    });

    /// <summary>Opens a sample page with the seal at the size and position shown on screen (also before saving).</summary>
    [RelayCommand]
    private Task PreviewSealAsync() => RunAsync(async () =>
    {
        // The values on screen, so each change can be checked before pressing Save.
        var bytes = await _session.Api.SealPreviewAsync((double)(SealWidth ?? 150), (double)(SealOffsetX ?? 0), -(double)(SealMoveUp ?? 0), layout: SealLayout);
        _session.Documents.SaveAndOpen(bytes, $"Seal-preview-{SealLayout}.pdf");
    });

    /// <summary>Opens the latest quotation printed with the heading and Party Details on screen (also before saving).</summary>
    [RelayCommand]
    private Task PreviewPrintAsync() => RunAsync(async () =>
    {
        if (Settings is null) return;
        var bytes = await _session.Api.PrintPreviewAsync(Settings.Quotation);
        _session.Documents.SaveAndOpen(bytes, "Print-preview.pdf");
    });

    private static List<string> SplitLines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0).ToList();
}
