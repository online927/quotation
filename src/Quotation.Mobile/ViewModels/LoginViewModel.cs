using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quotation.ApiClient;
using Quotation.Desktop.Services;
using Quotation.Mobile.Services;

namespace Quotation.Mobile.ViewModels;

/// <summary>Sign in: the same username and password as on the PC. The server address is typed once (kept on the phone).</summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly PhoneSession _session;
    private readonly Func<Task> _signedIn;

    public LoginViewModel(PhoneSession session, Func<Task> signedIn)
    {
        _session = session;
        _signedIn = signedIn;
        _serverAddress = session.Settings.ServerUrl;
        _username = session.Settings.Username;
        _showServer = session.Settings.ServerUrl.Length == 0;
    }

    [ObservableProperty] private string _serverAddress;
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _showServer;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _serverStatus;
    [ObservableProperty] private bool _serverOk;

    public string CompanyName => _session.Settings.CompanyName.Length > 0 ? _session.Settings.CompanyName : "";
    public string CompanyInitials => CompanyLetters.Of(_session.Settings.CompanyName);
    public string Greeting => DateTime.Now.Hour switch { < 12 => "Good morning", < 17 => "Good afternoon", _ => "Good evening" };
    /// <summary>The chip under the boxes: which server this phone uses.</summary>
    public string ServerText => _session.Settings.ServerUrl.Length == 0 ? "No server yet" : "Server " + ShortServer(_session.Settings.ServerUrl);

    public static string ShortServer(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? (u.Port == 5080 ? u.Host : $"{u.Host}:{u.Port}") : url;

    public void Refresh()
    {
        OnPropertyChanged(nameof(CompanyName));
        OnPropertyChanged(nameof(CompanyInitials));
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(ServerText));
    }

    [RelayCommand]
    private void ToggleServer() => ShowServer = !ShowServer;

    [RelayCommand]
    private async Task TestServerAsync()
    {
        ServerStatus = null;
        if (PhoneSettings.NormalizeServer(ServerAddress).Length == 0)
        {
            ServerOk = false;
            ServerStatus = "Type the server's address (the PC with the Quotation Server, e.g. 192.168.1.20).";
            return;
        }
        _session.UseServer(ServerAddress);
        ServerAddress = _session.Settings.ServerUrl;
        IsBusy = true;
        try
        {
            var health = await _session.Api.HealthAsync();
            ServerOk = true;
            ServerStatus = $"Connected (server {health.Version})";
            _session.Settings.Save();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            ServerOk = false;
            ServerStatus = "Not reachable. Is the phone on the office Wi-Fi?";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ServerText));
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        if (PhoneSettings.NormalizeServer(ServerAddress).Length == 0)
        {
            ShowServer = true;
            ErrorMessage = "Type the server's address first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Enter username and password.";
            return;
        }
        _session.UseServer(ServerAddress);
        IsBusy = true;
        try
        {
            await _session.SignInAsync(Username.Trim(), Password);
            Password = "";
            ShowServer = false;
            Refresh();
            await _signedIn();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.IsConnectionFailure ? "The Quotation Server cannot be reached. Is the phone on the office Wi-Fi?" : ex.Message;
            if (ex.IsConnectionFailure) ShowServer = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "The Quotation Server cannot be reached. Is the phone on the office Wi-Fi?";
            ShowServer = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
