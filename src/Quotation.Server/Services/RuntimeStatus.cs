namespace Quotation.Server.Services;

/// <summary>Live, in-memory connection state shared by background workers and the API.</summary>
public sealed class RuntimeStatus
{
    private readonly Lock _gate = new();

    public bool TallyConnected { get; private set; }
    public string? TallyCompany { get; private set; }
    public string? TallyError { get; private set; }
    public DateTime? TallyLastCheckedUtc { get; private set; }
    /// <summary>Active financial year start detected from Tally (null when unknown).</summary>
    public int? TallyActiveFinancialYearStart { get; private set; }
    public bool SyncRunning { get; private set; }
    public string? SyncProgress { get; private set; }

    public void SetTally(bool connected, string? company, string? error, DateTime checkedUtc, int? activeFyStart)
    {
        lock (_gate)
        {
            TallyConnected = connected;
            TallyError = error;
            TallyLastCheckedUtc = checkedUtc;
            if (connected)
            {
                TallyCompany = company;
                if (activeFyStart is not null) TallyActiveFinancialYearStart = activeFyStart;
            }
        }
    }

    public void SetSync(bool running, string? progress)
    {
        lock (_gate)
        {
            SyncRunning = running;
            SyncProgress = progress;
        }
    }
}
