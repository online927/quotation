using Quotation.Core.Formater;

namespace Quotation.Core.Tests;

public class SpokenItemTests
{
    [Theory]
    [InlineData("two bevel protractors at fifteen thousand six hundred", "Bevel protractor, 2, 15600")]
    [InlineData("2 bevel protractors at 15,600", "Bevel protractor, 2, 15600")]
    [InlineData("bevel protractor 2 nos at 15600 each", "Bevel protractor, 2, 15600")]
    [InlineData("vernier caliper 300 mm quantity 2 rate 3159", "Vernier caliper 300 mm, 2, 3159")]
    [InlineData("vernier caliper 0-300mm 2 3159", "Vernier caliper 0-300mm, 2, 3159")]
    [InlineData("5 digital calipers", "Digital caliper, 5")]
    [InlineData("digital caliper 150 mm 5 pieces", "Digital caliper 150 mm, 5")]
    [InlineData("micrometer", "Micrometer, 1")]
    [InlineData("one micrometer at ₹4,250", "Micrometer, 1, 4250")]
    [InlineData("ten dial gauges at two thousand three hundred and fifty rupees", "Dial gauge, 10, 2350")]
    [InlineData("3 surface plates for 1.5 lakh", "Surface plate, 3, 150000")]
    [InlineData("a dozen spanners at 120", "Spanner, 12, 120")]
    [InlineData("Mitutoyo 500-196 quantity 4 at 21978", "Mitutoyo 500-196, 4, 21978")]
    [InlineData("4 boxes of drill bits at 900", "Boxes of drill bits, 4, 900")]
    public void A_spoken_item_becomes_a_message_line(string spoken, string line) => Assert.Equal(line, SpokenItem.ToLine(spoken));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("at 200")]
    public void Nothing_like_an_item_gives_nothing(string spoken) => Assert.Null(SpokenItem.ToLine(spoken));
}
