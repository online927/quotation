using Avalonia;
using Avalonia.Headless;
using Quotation.Mobile;

[assembly: AvaloniaTestApplication(typeof(Quotation.Mobile.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Quotation.Mobile.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont();
}
