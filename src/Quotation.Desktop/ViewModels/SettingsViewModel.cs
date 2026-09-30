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
    [ObservableProperty] private string _fyOverride = "";
    [ObservableProperty] private string _newApiKey = "";
    [ObservableProperty] private string _newGmailClientJson = "";

    // Users tab
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newDisplayName = "";
    [ObservableProperty] private string _newUserPassword = "";
    [ObservableProperty] private bool _newUserIsAdmin;
    public ObservableCollection<UserDto> Users { get; } = [];

    // Numbering
    [ObservableProperty] private NumberSeriesDto? _series;
    [ObservableProperty] private decimal? _newNextSequence;

    public bool IsAdmin => _session.IsAdmin;
    public bool IsReadOnly => !IsAdmin;
    public IReadOnlyList<TaxPresentation> TaxPresentations { get; } = Enum.GetValues<TaxPresentation>();
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
        FyOverride = s.Tally.ActiveFinancialYearOverride?.ToString() ?? "";
        Settings = s;
        OnPropertyChanged(nameof(ExampleNumber));
        if (IsAdmin)
        {
            await LoadUsersAsync();
            Series = await _session.Api.NumberSeriesAsync();
            NewNextSequence = Series.NextSequence;
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
    private void RefreshExample() => OnPropertyChanged(nameof(ExampleNumber));

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Settings is null) return;
        InfoMessage = null;
        Settings.Company.AddressLines = SplitLines(AddressLines);
        Settings.Company.TermsAndConditions = SplitLines(Terms);
        Settings.Tally.CustomerGroups = SplitLines(CustomerGroups);
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

    private static List<string> SplitLines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0).ToList();
}
