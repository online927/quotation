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
    [ObservableProperty] private bool _newUserIsAdmin;
    public ObservableCollection<UserDto> Users { get; } = [];

    // Editing a user (administrator)
    [ObservableProperty] private UserDto? _editingUser;
    [ObservableProperty] private string _editUsername = "";
    [ObservableProperty] private string _editDisplayName = "";
    [ObservableProperty] private bool _editIsAdmin;
    [ObservableProperty] private bool _editIsActive;
    [ObservableProperty] private string _resetPassword = "";
    public bool IsEditingUser => EditingUser is not null;
    partial void OnEditingUserChanged(UserDto? value) => OnPropertyChanged(nameof(IsEditingUser));

    // Seal (administrator)
    [ObservableProperty] private string _newSealPin = "";
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _sealImage;
    [ObservableProperty] private decimal? _sealWidth;
    [ObservableProperty] private decimal? _sealOffsetX;
    /// <summary>Points the seal is moved up (negative = down); stored as the downward offset.</summary>
    [ObservableProperty] private decimal? _sealMoveUp;
    [ObservableProperty] private string _hiddenMarkers = "";
    public bool HasSealImage => SealImage is not null;
    public string SealPinState => Settings?.SealPinConfigured == true ? "A seal PIN is set." : "No seal PIN yet.";
    partial void OnSealImageChanged(Avalonia.Media.Imaging.Bitmap? value) => OnPropertyChanged(nameof(HasSealImage));

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

    partial void OnSettingsChanged(AllSettingsDto? value) => OnPropertyChanged(nameof(PartyDisplayNameIndex));
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
        SealWidth = (decimal)s.Company.SealWidth;
        SealOffsetX = (decimal)s.Company.SealOffsetX;
        SealMoveUp = -(decimal)s.Company.SealOffsetY;
        FyOverride = s.Tally.ActiveFinancialYearOverride?.ToString() ?? "";
        Settings = s;
        OnPropertyChanged(nameof(ExampleNumber));
        OnPropertyChanged(nameof(SealPinState));
        if (IsAdmin)
        {
            await LoadUsersAsync();
            Series = await _session.Api.NumberSeriesAsync();
            NewNextSequence = Series.NextSequence;
            ProformaSeries = await _session.Api.NumberSeriesAsync(type: DocumentType.ProformaInvoice);
            NewNextProformaSequence = ProformaSeries.NextSequence;
            await LoadSealImageAsync();
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
        Settings.Company.SealWidth = (double)(SealWidth ?? 150);
        Settings.Company.SealOffsetX = (double)(SealOffsetX ?? 0);
        Settings.Company.SealOffsetY = -(double)(SealMoveUp ?? 0);
        var pin = NewSealPin.Trim();
        if (pin.Length > 0 && (pin.Length != 4 || !pin.All(char.IsAsciiDigit)))
        {
            ErrorMessage = "The seal PIN must be exactly 4 digits.";
            return;
        }
        Settings.SealPin = pin.Length > 0 ? pin : null;
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
            NewSealPin = "";
            _session.Settings = Settings;
            InfoMessage = "Settings saved.";
            OnPropertyChanged(nameof(ExampleNumber));
            OnPropertyChanged(nameof(SealPinState));
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
                NewUserIsAdmin ? UserRole.Admin : UserRole.User));
            await LoadUsersAsync();
        });
        if (ok)
        {
            InfoMessage = $"User '{NewUsername}' created. They must change the password at first login.";
            NewUsername = NewDisplayName = NewUserPassword = "";
            NewUserIsAdmin = false;
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
        InfoMessage = null;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void CancelEditUser() => EditingUser = null;

    /// <summary>Saves login name, display name, role and active; old quotations keep the name used at the time.</summary>
    [RelayCommand]
    private async Task SaveUserAsync()
    {
        if (EditingUser is not { } user) return;
        InfoMessage = null;
        if (await RunAsync(async () =>
            {
                var saved = await _session.Api.UpdateUserAsync(user.Id,
                    new UpdateUserRequest(EditUsername, EditDisplayName, EditIsAdmin ? UserRole.Admin : UserRole.User, EditIsActive));
                _session.Api.UpdateCurrentUser(saved);
                await LoadUsersAsync();
            }))
        {
            InfoMessage = $"User '{EditUsername.Trim()}' saved.";
            EditingUser = null;
        }
    }

    /// <summary>Temporary password: the user must choose a new one at the next login; logged out on every PC now.</summary>
    [RelayCommand]
    private async Task ResetUserPasswordAsync()
    {
        if (EditingUser is not { } user) return;
        InfoMessage = null;
        if (ResetPassword.Length < 6)
        {
            ErrorMessage = "The temporary password must have at least 6 characters.";
            return;
        }
        if (await RunAsync(() => _session.Api.ResetPasswordAsync(user.Id, ResetPassword)))
        {
            InfoMessage = $"Password of '{user.Username}' reset. They log in with the temporary password and then choose a new one.";
            ResetPassword = "";
        }
    }

    // ---------------------------------------------------------------- seal

    private async Task LoadSealImageAsync()
    {
        SealImage = null;
        if (Settings?.SealImageConfigured != true) return;
        try
        {
            using var ms = new MemoryStream(await _session.Api.SealImageAsync());
            SealImage = new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch (Exception)
        {
            // Preview only.
        }
    }

    /// <summary>Called by the view with the chosen PNG/JPG file.</summary>
    public Task UploadSealAsync(byte[] image) => RunAsync(async () =>
    {
        await _session.Api.UploadSealAsync(image);
        Settings = await _session.Api.SettingsAsync();
        _session.Settings = Settings;
        await LoadSealImageAsync();
        InfoMessage = "Seal image saved. Set the seal PIN below (if not set yet) and press Save; 'Preview on a page' shows where it is printed.";
    });

    [RelayCommand]
    private Task RemoveSealAsync() => RunAsync(async () =>
    {
        await _session.Api.RemoveSealAsync();
        Settings = await _session.Api.SettingsAsync();
        _session.Settings = Settings;
        SealImage = null;
        InfoMessage = "Seal image removed.";
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
