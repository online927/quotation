using Avalonia;

namespace Quotation.Desktop;

internal static class Program
{
    /// <summary>Named while the app runs, so the installer (AppMutex) can ask the user to close it before updating.</summary>
    public const string RunningMutexName = "TSQuotationClientRunning";

    [STAThread]
    public static void Main(string[] args)
    {
        using var running = TryCreateMutex(RunningMutexName);
        using var runningGlobal = TryCreateMutex(@"Global\" + RunningMutexName);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static Mutex? TryCreateMutex(string name)
    {
        try
        {
            return new Mutex(false, name);
        }
        catch (Exception)
        {
            return null; // only used to detect a running copy during setup
        }
    }
}
