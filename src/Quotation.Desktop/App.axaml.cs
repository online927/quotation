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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = new AppSession(ClientConfig.Load(), url => QuotationApiClient.Create(url));
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(session) };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
