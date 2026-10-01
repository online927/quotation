using Quotation.Server.Services;

namespace Quotation.Server.Tests;

public class RuntimeStatusTests
{
    [Fact]
    public void Slow_earlier_check_does_not_overwrite_newer_result()
    {
        var status = new RuntimeStatus();
        var t0 = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);

        // A check started later succeeds first; the earlier (slow) failing check finishes afterwards.
        status.SetTally(true, "T.SAIFUDDIN & CO.", null, t0.AddSeconds(1), 2026);
        status.SetTally(false, null, "Connection refused", t0, null);

        Assert.True(status.TallyConnected);
        Assert.Null(status.TallyError);
        Assert.Equal(t0.AddSeconds(1), status.TallyLastCheckedUtc);
    }

    [Fact]
    public void Newer_failure_replaces_connected_state()
    {
        var status = new RuntimeStatus();
        var t0 = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);

        status.SetTally(true, "T.SAIFUDDIN & CO.", null, t0, 2026);
        status.SetTally(false, null, "Connection refused", t0.AddSeconds(5), null);

        Assert.False(status.TallyConnected);
        Assert.Equal("Connection refused", status.TallyError);
        Assert.Equal("T.SAIFUDDIN & CO.", status.TallyCompany);
    }
}
