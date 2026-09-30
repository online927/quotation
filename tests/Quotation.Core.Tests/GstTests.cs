using Quotation.Core.Gst;

namespace Quotation.Core.Tests;

public class GstTests
{
    [Theory]
    [InlineData("27AAPFU0939F1ZV", true)]
    [InlineData("27AAPFU0939F1ZX", false)] // wrong check digit
    [InlineData("27AAPFU0939F1Z", false)]  // too short
    [InlineData("AAAPFU0939F1ZVX", false)] // no state code
    [InlineData(null, false)]
    public void Validates_gstin(string? gstin, bool valid) => Assert.Equal(valid, Gstin.IsValid(gstin));

    [Fact]
    public void Check_digit_round_trip()
    {
        const string first14 = "29ABCDE1234F1Z";
        var full = first14 + Gstin.ComputeCheckDigit(first14);
        Assert.True(Gstin.IsValid(full));
        Assert.Equal("ABCDE1234F", Gstin.Pan(full));
    }

    [Theory]
    [InlineData("Karnataka", "29")]
    [InlineData("tamil nadu", "33")]
    [InlineData("Tamilnadu", "33")]
    [InlineData("Orissa", "21")]
    [InlineData("Jammu & Kashmir", "01")]
    [InlineData("Unknownland", null)]
    public void State_codes(string name, string? code) => Assert.Equal(code, IndianStates.CodeForName(name));

    [Fact]
    public void Gstin_state_code_is_preferred()
    {
        Assert.Equal("27", IndianStates.ResolveCode("27AAPFU0939F1ZV", "Karnataka"));
        Assert.Equal("29", IndianStates.ResolveCode("", "Karnataka"));
        Assert.Equal("", IndianStates.ResolveCode(null, null));
    }
}
