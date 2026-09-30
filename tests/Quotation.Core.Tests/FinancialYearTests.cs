using Quotation.Core.Domain;

namespace Quotation.Core.Tests;

public class FinancialYearTests
{
    [Theory]
    [InlineData(2026, 3, 31, 2025)]
    [InlineData(2026, 4, 1, 2026)]
    [InlineData(2025, 12, 15, 2025)]
    [InlineData(2026, 1, 1, 2025)]
    public void For_uses_april_start(int y, int m, int d, int expectedStart)
    {
        Assert.Equal(expectedStart, FinancialYear.For(new DateOnly(y, m, d)).StartYear);
    }

    [Fact]
    public void Labels_and_codes()
    {
        var fy = new FinancialYear(2025);
        Assert.Equal("2025-26", fy.Label);
        Assert.Equal("2526", fy.ShortCode);
        Assert.Equal(new DateOnly(2025, 4, 1), fy.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 31), fy.EndDate);
        Assert.True(fy.Contains(new DateOnly(2026, 3, 31)));
        Assert.False(fy.Contains(new DateOnly(2026, 4, 1)));
    }

    [Fact]
    public void Century_boundary()
    {
        var fy = new FinancialYear(2099);
        Assert.Equal("2099-00", fy.Label);
        Assert.Equal("9900", fy.ShortCode);
    }

    [Fact]
    public void Calendar_year_mode()
    {
        var fy = FinancialYear.For(new DateOnly(2026, 6, 1), startMonth: 1);
        Assert.Equal(2026, fy.StartYear);
        Assert.Equal("2026", fy.Label);
        Assert.Equal(new DateOnly(2026, 12, 31), fy.EndDate);
    }
}
