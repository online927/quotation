using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop.Tests;

/// <summary>The sign-in screen: greeting by the time of day, the server address hidden behind "Server settings".</summary>
public class LoginScreenTests
{
    private static async Task Shot(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaFact]
    public async Task Greeting_and_hidden_server_address()
    {
        var old = LoginViewModel.Now;
        try
        {
            LoginViewModel.Now = () => new DateTime(2026, 11, 9, 9, 30, 0);
            var config = new ClientConfig { FilePath = Path.Combine(Path.GetTempPath(), $"login-{Guid.NewGuid():N}.json"), ServerUrl = "http://127.0.0.1:1" };
            // A new PC: nobody signed in yet, so no company name is known.
            Assert.Equal("Quotations", new LoginViewModel(new AppSession(config, url => Quotation.ApiClient.QuotationApiClient.Create(url)), () => { }).CompanyName);
            config.CompanyName = "T.SAIFUDDIN & CO."; // remembered from Settings → Company after a sign-in
            var session = new AppSession(config, url => Quotation.ApiClient.QuotationApiClient.Create(url));
            var main = new MainWindowViewModel(session);
            var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var login = Assert.IsType<LoginViewModel>(main.Content);
            var view = window.FindDescendantOfType<LoginView>()!;
            Assert.Equal("Good morning", view.FindControl<TextBlock>("GreetingText")!.Text);
            Assert.Equal("T.SAIFUDDIN & CO.", view.FindControl<TextBlock>("CompanyNameText")!.Text);
            Assert.Equal("TS", view.FindControl<TextBlock>("CompanyInitials")!.Text);
            Assert.False(view.FindControl<StackPanel>("ServerPanel")!.IsVisible); // the address is not shown
            await Shot(window, "login-C-light");

            ThemeService.Apply(Quotation.Contracts.AppTheme.Dark);
            await Shot(window, "login-C-dark");
            ThemeService.Apply(Quotation.Contracts.AppTheme.Light);

            login.ToggleServerCommand.Execute(null); // "Server settings"
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<StackPanel>("ServerPanel")!.IsVisible);
            await Shot(window, "login-C-server-open");
            login.ToggleServerCommand.Execute(null);
            Assert.False(login.ShowServer);

            // The server cannot be reached: the address opens by itself, with the reason.
            login.Username = "ravi";
            login.Password = "secret";
            await login.LoginCommand.ExecuteAsync(null);
            Assert.True(login.ShowServer);
            Assert.False(string.IsNullOrEmpty(login.ErrorMessage));
            await Shot(window, "login-C-unreachable");
            window.Close();

            LoginViewModel.Now = () => new DateTime(2026, 11, 9, 14, 0, 0);
            Assert.Equal("Good afternoon", login.Greeting);
            LoginViewModel.Now = () => new DateTime(2026, 11, 9, 19, 0, 0);
            Assert.Equal("Good evening", login.Greeting);
        }
        finally
        {
            LoginViewModel.Now = old;
            ThemeService.Apply(Quotation.Contracts.AppTheme.Light);
        }
    }

    [AvaloniaFact]
    public async Task Company_name_from_settings_is_remembered_for_the_next_sign_in()
    {
        using var server = new TestServer(products: 20, customers: 10, today: DateOnly.FromDateTime(DateTime.Today));
        var admin = await server.LoginAsAdminAsync();
        var all = await admin.SettingsAsync();
        all.Company.CompanyName = "Acme Precision Tools";
        await admin.SaveSettingsAsync(all);

        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };
        var session = new AppSession(config, _ => server.CreateApi());
        var main = new MainWindowViewModel(session);
        var login = Assert.IsType<LoginViewModel>(main.Content);
        Assert.Equal("Quotations", login.CompanyName);
        var window = new MainWindow { DataContext = main, Width = 1366, Height = 768 };
        window.Show();
        await Shot(window, "login-new-pc");
        login.Username = "admin";
        login.Password = "admin123";
        await login.LoginCommand.ExecuteAsync(null);
        await UiHelpers.WaitUntil(() => config.CompanyName == "Acme Precision Tools");
        Assert.Equal("Acme Precision Tools", ClientConfig.Load(config.FilePath).CompanyName); // saved on this PC

        var next = new LoginViewModel(session, () => { });
        Assert.Equal("Acme Precision Tools", next.CompanyName);
        Assert.Equal("AP", next.CompanyInitials);
        if (main.Content is ShellViewModel shell) shell.Dispose();
        main.Content = next;
        await Shot(window, "login-after-settings-name");
        window.Close();
    }

    [AvaloniaFact]
    public async Task User_name_is_filled_in_for_standard_users_but_never_for_admin()
    {
        using var server = new TestServer(products: 10, customers: 5, today: DateOnly.FromDateTime(DateTime.Today));
        await server.LoginAsStandardAsync(); // makes the Standard user "ravi"
        var config = new ClientConfig { FilePath = Path.Combine(server.DataDirectory, "c.json") };

        async Task SignInAsync(string user, string password)
        {
            var login = new LoginViewModel(new AppSession(config, _ => server.CreateApi()), () => { }) { Username = user, Password = password };
            await login.LoginCommand.ExecuteAsync(null);
        }

        await SignInAsync("ravi", "ravi-pass");
        Assert.Equal("ravi", new LoginViewModel(new AppSession(config, _ => server.CreateApi()), () => { }).Username);

        // The administrator signs in on Ravi's PC: Ravi's name is still filled in next time.
        await SignInAsync("admin", "admin123");
        Assert.Equal("ravi", new LoginViewModel(new AppSession(config, _ => server.CreateApi()), () => { }).Username);

        // A PC where the older version remembered "admin": the box stays empty.
        config.LastUsername = "admin";
        Assert.Equal("", new LoginViewModel(new AppSession(config, _ => server.CreateApi()), () => { }).Username);
    }
}
