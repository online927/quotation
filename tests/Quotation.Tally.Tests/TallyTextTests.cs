namespace Quotation.Tally.Tests;

public class TallyTextTests
{
    [Theory]
    [InlineData("15600.00/NOS", 15600.00)]
    [InlineData(" -1,234.50", -1234.50)]
    [InlineData("₹ 15,600.00/NOS", 15600.00)]
    [InlineData(" 18", 18)]
    [InlineData("9 %", 9)]
    public void Parses_decimals(string input, double expected)
    {
        Assert.Equal((decimal)expected, TallyText.Decimal(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Unparseable_decimals_are_null(string input) => Assert.Null(TallyText.Decimal(input));

    [Fact]
    public void Rate_unit() => Assert.Equal("NOS", TallyText.RateUnit("15600.00/NOS"));

    [Theory]
    [InlineData("20250401", 2025, 4, 1)]
    [InlineData("1-Apr-2025", 2025, 4, 1)]
    [InlineData("01-Apr-25", 2025, 4, 1)]
    public void Parses_dates(string input, int y, int m, int d) => Assert.Equal(new DateOnly(y, m, d), TallyText.Date(input));

    [Fact]
    public void Clean_removes_control_characters() => Assert.Equal("Applicable", TallyText.Clean("\u0004 Applicable"));

    [Fact]
    public void Tdl_string_escapes_quotes() => Assert.Equal("\"A \"\"B\"\" C\"", TallyText.TdlString("A \"B\" C"));
}
