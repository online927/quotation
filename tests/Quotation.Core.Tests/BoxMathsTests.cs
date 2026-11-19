using Quotation.Core.Calculation;

namespace Quotation.Core.Tests;

/// <summary>Sums typed into the number boxes ("=12*2", "=3159-5%").</summary>
public class BoxMathsTests
{
    [Theory]
    [InlineData("=12*2", 24)]
    [InlineData("= 12 x 2", 24)]
    [InlineData("=3159-5%", 3001.05)]
    [InlineData("=1500+10%", 1650)]
    [InlineData("=200*10%", 20)]
    [InlineData("=21978/1.18", 18625.4237)]
    [InlineData("=(100+50)*2", 300)]
    [InlineData("=21,978/2", 10989)]
    [InlineData("=-5+10", 5)]
    [InlineData("=50%", 0.5)]
    public void Sums_are_worked_out(string text, double expected) =>
        Assert.True(BoxMaths.TryEvaluate(text, out var v) && v == (decimal)expected, $"{text} gave {v}");

    [Theory]
    [InlineData("12*2")]   // no "=": a plain number box text
    [InlineData("=")]
    [InlineData("=12*")]
    [InlineData("=abc")]
    [InlineData("=10/0")]
    [InlineData("=(1+2")]
    [InlineData("=1 2")]
    public void Anything_else_is_not_a_sum(string text) => Assert.False(BoxMaths.TryEvaluate(text, out _));
}
