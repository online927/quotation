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
        OnPropertyChanged(nameof(EditingUserCanHaveSeal));
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
    [ObservableProperty] private decimal? _sealWidth;
    [ObservableProperty] private decimal? _sealOffsetX;
    /// <summary>Points the seal is moved up (negative = down); stored as the downward offset.</summary>
    [ObservableProperty] private decimal? _sealMoveUp;
    [ObservableProperty] private string _hiddenMarkers = "";
    /// <summary>Sign-off lines of the Zoho layout, one per row.</summary>
    [ObservableProperty] private string _modernSignOff = "";

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

    public IReadOnlyList<string> PdfLayoutChoices { get; } = [PdfLayouts.Tally, PdfLayouts.Zoho];

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
        OnPropertyChanged(nameof(PartyDisplayNameIndex));
        OnPropertyChanged(nameof(ConsigneeChoiceIndex));
    }
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

    public SettingsViewModel(AppSession session)
    {
        _session = session;
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
        ModernSignOff = string.Join(Environment.NewLine, s.Quotation.ModernSignOff);
        SealWidth = (decimal)s.Company.SealWidth;
        SealOffsetX = (decimal)s.Company.SealOffsetX;
        SealMoveUp = -(decimal)s.Company.SealOffsetY;
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
        }
        _session.Settings = s;
    });

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
        Settings.Quotation.ModernSignOff = SplitLines(ModernSignOff);
        Settings.Company.SealWidth = (double)(SealWidth ?? 150);
        Settings.Company.SealOffsetX = (double)(SealOffsetX ?? 0);
        Settings.Company.SealOffsetY = -(double)(SealMoveUp ?? 0);
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
    }

    [RelayCommand]
    private async Task AddUserAsync()
    {
        InfoMessage = null;
        var ok = await RunAsync(async () =>
        {
            await _session.Api.CreateUserAsync(new CreateUserRequest(NewUsername, NewDisplayName, NewUserPassword,
                UserRole.User)); // one administrator: new users are Standard users
            await LoadUsersAsync();
        });
        if (ok)
        {
            InfoMessage = $"User '{NewUsername}' created. They must change the password at first login.";
            NewUsername = NewDisplayName = NewUserPassword = "";
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
    private void CancelEditUser() => EditingUser = null;

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
            InfoMessage = $"User '{EditUsername.Trim()}' saved." + sealNote;
            EditingUser = null;
        }
    }

    /// <summary>Saves the seal size and place when they were changed on screen (the same for every seal).</summary>
    private async Task SaveSealPositionAsync()
    {
        if (Settings is null) return;
        var position = new SealPositionDto((double)(SealWidth ?? 150), (double)(SealOffsetX ?? 0), -(double)(SealMoveUp ?? 0));
        var c = Settings.Company;
        if (c.SealWidth == position.Width && c.SealOffsetX == position.OffsetX && c.SealOffsetY == position.OffsetY) return;
        await _session.Api.SaveSealPositionAsync(position);
        (c.SealWidth, c.SealOffsetX, c.SealOffsetY) = (position.Width, position.OffsetX, position.OffsetY);
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
        var bytes = await _session.Api.SealPreviewAsync((double)(SealWidth ?? 150), (double)(SealOffsetX ?? 0), -(double)(SealMoveUp ?? 0), userId: user.Id);
        _session.Documents.SaveAndOpen(bytes, $"Seal-preview-{user.Username}.pdf");
    });

    /// <summary>Opens a sample page with the seal at the size and position shown on screen (also before saving).</summary>
    [RelayCommand]
    private Task PreviewSealAsync() => RunAsync(async () =>
    {
        // The values on screen, so each change can be checked before pressing Save.
        var bytes = await _session.Api.SealPreviewAsync((double)(SealWidth ?? 150), (double)(SealOffsetX ?? 0), -(double)(SealMoveUp ?? 0));
        _session.Documents.SaveAndOpen(bytes, "Seal-preview.pdf");
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
