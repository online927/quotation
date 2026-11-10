using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly Action _onLoggedIn;

    [ObservableProperty] private string _serverUrl;
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _serverStatus;
    /// <summary>The server address is hidden behind "Server settings"; it opens by itself when the server cannot be reached.</summary>
    [ObservableProperty] private bool _showServer;

    /// <summary>The company name from Settings → Company (remembered on this PC after the first sign-in), else "Quotations".</summary>
    public string CompanyName => string.IsNullOrWhiteSpace(_session.Config.CompanyName) ? "Quotations" : _session.Config.CompanyName.Trim();

    /// <summary>The first letters of the first two words of the company name ("T.SAIFUDDIN &amp; CO." → "TS").</summary>
    public string CompanyInitials
    {
        get
        {
            var words = System.Text.RegularExpressions.Regex.Matches(CompanyName, "[A-Za-z0-9]+").Select(m => m.Value).ToList();
            var letters = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
            return letters.Length > 0 ? letters : "Q";
        }
    }

    /// <summary>The time of day (replaced in tests).</summary>
    public static Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <summary>Good morning (before 12), Good afternoon (before 5 pm), Good evening.</summary>
    public string Greeting => Now().Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening",
    };

    [RelayCommand]
    private void ToggleServer() => ShowServer = !ShowServer;

    public LoginViewModel(AppSession session, Action onLoggedIn)
    {
        _session = session;
        _onLoggedIn = onLoggedIn;
        _serverUrl = session.Config.ServerUrl;
        // The last Standard user of this PC is filled in; the administrator's name never is.
        var last = session.Config.LastUsername.Trim();
        _username = last.Equals("admin", StringComparison.OrdinalIgnoreCase) ? "" : last;
    }

    [RelayCommand]
    private async Task TestServerAsync()
    {
        await RunAsync(async () =>
        {
            ApplyServerUrl();
            var health = await _session.Api.HealthAsync();
            ServerStatus = $"Connected to Quotation Server v{health.Version}";
        });
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Enter username and password.";
            return;
        }
        var ok = await RunAsync(async () =>
        {
            ApplyServerUrl();
            try
            {
                await _session.Api.LoginAsync(Username.Trim(), Password);
            }
            catch (Quotation.ApiClient.ApiException ex) when (ex.IsConnectionFailure)
            {
                ShowServer = true; // the server could not be reached: show where the app looks for it
                throw;
            }
            if (_session.Api.CurrentUser?.Role != Core.Domain.UserRole.Admin)
            {
                // Remembered for next time; an administrator signing in keeps this PC's user name as it was.
                _session.Config.LastUsername = Username.Trim();
                _session.Config.Save();
            }
        });
        Password = "";
        if (ok) _onLoggedIn();
    }

    private void ApplyServerUrl()
    {
        var url = ServerUrl.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "http://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw new InvalidOperationException("Server address is not valid.");
        if (!string.Equals(url, _session.Api.BaseAddress?.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            _session.Reconnect(url);
        }
        ServerUrl = url;
    }
}
