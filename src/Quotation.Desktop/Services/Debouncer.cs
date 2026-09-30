namespace Quotation.Desktop.Services;

/// <summary>Runs only the last action requested within the delay window (search-as-you-type).</summary>
public sealed class Debouncer(TimeSpan delay)
{
    private CancellationTokenSource? _cts;

    public void Run(Func<CancellationToken, Task> action)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _ = RunCoreAsync(action, cts.Token);
    }

    private async Task RunCoreAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            await action(ct);
        }
        catch (OperationCanceledException) { }
    }
}
