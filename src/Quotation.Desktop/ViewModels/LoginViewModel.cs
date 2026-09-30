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

    public LoginViewModel(AppSession session, Action onLoggedIn)
    {
        _session = session;
        _onLoggedIn = onLoggedIn;
        _serverUrl = session.Config.ServerUrl;
        _username = session.Config.LastUsername;
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
            await _session.Api.LoginAsync(Username.Trim(), Password);
            _session.Config.LastUsername = Username.Trim();
            _session.Config.Save();
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
