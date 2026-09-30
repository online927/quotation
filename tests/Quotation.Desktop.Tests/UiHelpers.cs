using Avalonia.Threading;

namespace Quotation.Desktop.Tests;

public static class UiHelpers
{
    /// <summary>Pumps the UI dispatcher until the condition holds (async view-model loads complete).</summary>
    public static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs) throw new TimeoutException("Condition not met in time.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
