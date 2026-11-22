using Quotation.Core.Gst;

namespace Quotation.Core.Tests;

public class TaxCheckTests
{
    [Fact]
    public void Matching_gstin_and_state_has_no_problem()
    {
        Assert.Empty(TaxCheck.Problems(new TaxCheck.Party("The buyer", "27AAACS1234F1ZS", "Maharashtra", "27")));
        Assert.Empty(TaxCheck.Problems(new TaxCheck.Party("The buyer", "", "Karnataka", "29"))); // no GSTIN (unregistered): fine
    }

    [Fact]
    public void Gstin_from_another_state_mistyped_gstin_and_missing_state_are_reported()
    {
        var other = Assert.Single(TaxCheck.Problems(new TaxCheck.Party("The buyer", "27AAACS1234F1ZS", "Karnataka", "29")));
        Assert.Contains("from Maharashtra (27), but the state is Karnataka (29)", other);
        Assert.Contains(TaxCheck.Problems(new TaxCheck.Party("The buyer", "27AAACS1234", "Maharashtra", "27")), p => p.Contains("looks mistyped"));
        Assert.Contains(TaxCheck.Problems(new TaxCheck.Party("The consignee", "", "", "")), p => p.StartsWith("The consignee has no state"));
    }

    [Fact]
    public void A_gstin_with_the_wrong_check_letter_is_reported_as_mistyped()
    {
        // One typing mistake in Tally: the 15th character no longer matches the other 14
        var problem = Assert.Single(TaxCheck.Problems(new TaxCheck.Party("The buyer", "27AAACS1234F1Z3", "Maharashtra", "27")));
        Assert.Equal("The buyer's GSTIN 27AAACS1234F1Z3 looks mistyped: its last letter should be S.", problem);
        // The consignee is checked the same way; a wrong state and a wrong check letter are both listed
        var both = TaxCheck.Problems(new TaxCheck.Party("The consignee", "33AAACS1234F1Z9", "Karnataka", "29"));
        Assert.Equal(2, both.Count);
        Assert.Contains("from Tamil Nadu (33)", both[0]);
        Assert.EndsWith("its last letter should be Z.", both[1]);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("27AAACS1234F1ZS", null)]
    [InlineData("27aaacs1234f1zs", null)]                // small letters are fine
    [InlineData("29AAGCB7383J1Z4", null)]                // a real GSTIN
    [InlineData("27AAACS1234F1Z3", "Looks mistyped: the last letter should be S")]
    [InlineData("29AAGCB7383J1ZA", "Looks mistyped: the last digit should be 4")]
    [InlineData("27AAACS1234F1Z", "Looks mistyped: a GSTIN has 15 characters, this has 14")]
    [InlineData("27AAACS12E4F1ZS", "Looks mistyped: not in the GSTIN pattern (e.g. 29ABCDE1234F1Z5)")]
    public void Says_in_a_few_words_what_is_wrong_with_a_gstin(string gstin, string? expected) =>
        Assert.Equal(expected, Gstin.Problem(gstin));

    [Fact]
    public void Gst_follows_the_buyer_or_the_ship_to_state_as_chosen()
    {
        Assert.Equal("27", PlaceOfSupply.Code("27", "29", byBuyer: true));
        Assert.Equal("29", PlaceOfSupply.Code("27", "29", byBuyer: false));
        Assert.Equal("29", PlaceOfSupply.Code("", "29", byBuyer: true));   // no state for the chosen one: the other
        Assert.Equal("27", PlaceOfSupply.Code("27", "", byBuyer: false));

        // Our company in Karnataka (29): a buyer in Maharashtra with goods to Karnataka gives a different GST either way
        Assert.True(PlaceOfSupply.ChoiceMatters("29", "27", "29"));
        Assert.Equal("IGST", PlaceOfSupply.GstName("29", "27"));
        Assert.Equal("CGST + SGST", PlaceOfSupply.GstName("29", "29"));
        // Two other states (IGST either way), the same state, or a state missing: nothing to choose
        Assert.False(PlaceOfSupply.ChoiceMatters("29", "27", "33"));
        Assert.False(PlaceOfSupply.ChoiceMatters("29", "27", "27"));
        Assert.False(PlaceOfSupply.ChoiceMatters("29", "27", ""));
        Assert.False(PlaceOfSupply.ChoiceMatters("", "27", "29"));
    }

    [Fact]
    public void Describes_the_gst_the_quotation_gets()
    {
        Assert.Equal("CGST + SGST (Karnataka, same state)", TaxCheck.Describe("29", "29"));
        Assert.Equal("IGST (Maharashtra, another state)", TaxCheck.Describe("29", "27"));
    }
}
