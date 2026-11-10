using CommunityToolkit.Mvvm.ComponentModel;
using Quotation.Desktop.Services;

namespace Quotation.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppSession _session;

    [ObservableProperty] private ViewModelBase _content;

    public MainWindowViewModel(AppSession session)
    {
        _session = session;
        _content = CreateLogin();
    }

    public string Title => "TS Quotation System";

    private LoginViewModel CreateLogin() => new(_session, OnLoggedIn);

    private void OnLoggedIn()
    {
        _ = RememberCompanyAsync();
        _session.Api.SessionExpired += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ShowLogin);
        if (_session.Api.CurrentUser?.MustChangePassword == true)
        {
            Content = new ChangePasswordViewModel(_session, forced: true, onDone: ShowShell);
        }
        else
        {
            ShowShell();
        }
    }

    /// <summary>The company name for the sign-in screen next time (from Settings → Company).</summary>
    private async Task RememberCompanyAsync()
    {
        try
        {
            await _session.GetSettingsAsync(refresh: true);
        }
        catch (Exception)
        {
            // Not needed to work; tried again whenever settings are read.
        }
    }

    private void ShowShell() => Content = new ShellViewModel(_session, ShowLogin);

    private void ShowLogin()
    {
        if (Content is ShellViewModel shell) shell.Dispose();
        if (Content is not LoginViewModel) Content = CreateLogin();
    }
}
