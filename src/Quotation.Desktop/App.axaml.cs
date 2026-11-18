using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Quotation.ApiClient;
using Quotation.Desktop.Services;
using Quotation.Desktop.ViewModels;
using Quotation.Desktop.Views;

namespace Quotation.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services.ThemeService.MotionFactory = () => new Styles.MotionStyles();
        Services.ThemeService.Initialise();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = new AppSession(ClientConfig.Load(), url => QuotationApiClient.Create(url));
            ThemeService.Apply(Quotation.Contracts.AppTheme.Normalize(session.Config.Theme));
            TextSelectionLook.Apply(session.Config.TextSelection);
            // Always opens maximised (fills the screen; taskbar and title bar stay visible).
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(session), WindowState = Avalonia.Controls.WindowState.Maximized };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
