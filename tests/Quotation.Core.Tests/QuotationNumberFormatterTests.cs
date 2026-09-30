using Quotation.Core.Domain;
using Quotation.Core.Numbering;

namespace Quotation.Core.Tests;

public class QuotationNumberFormatterTests
{
    private static readonly FinancialYear Fy2526 = new(2025);

    [Theory]
    [InlineData("TSQ{FY}-{SEQ}", 3247, "TSQ2526-3247")]
    [InlineData("TSQ{FY}-{SEQ:5}", 42, "TSQ2526-00042")]
    [InlineData("QT/{FY_LABEL}/{SEQ}", 7, "QT/2025-26/7")]
    [InlineData("Q{FYS2}{FYE2}{SEQ:4}", 12, "Q25260012")]
    [InlineData("{FYS4}-{FYE4}-{SEQ}", 1, "2025-2026-1")]
    public void Formats_patterns(string pattern, int seq, string expected)
    {
        Assert.Equal(expected, QuotationNumberFormatter.Format(pattern, Fy2526, seq));
    }

    [Theory]
    [InlineData("TSQ{FY}")]
    [InlineData("TSQ{FY}-{SEQ}-{SEQ}")]
    [InlineData("TSQ{XX}-{SEQ}")]
    [InlineData("TSQ{FY-{SEQ}")]
    [InlineData("")]
    public void Rejects_invalid_patterns(string pattern)
    {
        Assert.Throws<FormatException>(() => QuotationNumberFormatter.Validate(pattern));
    }

    [Fact]
    public void Parses_sequence_back()
    {
        Assert.Equal(3247, QuotationNumberFormatter.TryParseSequence("TSQ{FY}-{SEQ}", Fy2526, "TSQ2526-3247"));
        Assert.Equal(42, QuotationNumberFormatter.TryParseSequence("TSQ{FY}-{SEQ:5}", Fy2526, "tsq2526-00042"));
        Assert.Null(QuotationNumberFormatter.TryParseSequence("TSQ{FY}-{SEQ}", Fy2526, "TSQ2627-3247"));
        Assert.Null(QuotationNumberFormatter.TryParseSequence("TSQ{FY}-{SEQ}", Fy2526, "garbage"));
    }

    [Fact]
    public void File_name_is_sanitized()
    {
        Assert.Equal("QT-2025-26-7", QuotationNumberFormatter.ToFileName("QT/2025-26/7"));
        Assert.Equal("TSQ2526-3247", QuotationNumberFormatter.ToFileName("TSQ2526-3247"));
    }

    [Fact]
    public void Financial_year_transition_changes_prefix()
    {
        var march = FinancialYear.For(new DateOnly(2026, 3, 31));
        var april = FinancialYear.For(new DateOnly(2026, 4, 1));
        Assert.Equal("TSQ2526-9999", QuotationNumberFormatter.Format("TSQ{FY}-{SEQ}", march, 9999));
        Assert.Equal("TSQ2627-1", QuotationNumberFormatter.Format("TSQ{FY}-{SEQ}", april, 1));
    }
}
