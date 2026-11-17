using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Quotation.ApiClient;
using Quotation.Mobile.Services;
using Quotation.Mobile.ViewModels;
using Quotation.Mobile.Views;

namespace Quotation.Mobile;

public partial class App : Application
{
    /// <summary>Set by the Android app before it starts (share, call, WhatsApp …); a PC run only records them.</summary>
    public static IPhoneActions Phone { get; set; } = new RecordingPhoneActions();

    /// <summary>Shown to the server as the "PC" this sign-in comes from (Android: the phone's model).</summary>
    public static string DeviceName { get; set; } = "Phone";

    public static QuotationApiClient CreateApi(string url) =>
        new(new HttpClient(ServerConnection.CreateHandler()) { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(40) }, DeviceName);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            var session = new PhoneSession(PhoneSettings.Load(), CreateApi, Phone);
            var main = new MainViewModel(session);
            single.MainView = new MainView { DataContext = main };
            _ = main.StartAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
