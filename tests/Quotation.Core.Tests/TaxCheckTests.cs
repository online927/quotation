using Quotation.Core.Gst;

namespace Quotation.Core.Tests;

public class TaxCheckTests
{
    [Fact]
    public void Matching_gstin_and_state_has_no_problem()
    {
        Assert.Empty(TaxCheck.Problems(new TaxCheck.Party("The buyer", "27AAACS1234F1Z3", "Maharashtra", "27")));
        Assert.Empty(TaxCheck.Problems(new TaxCheck.Party("The buyer", "", "Karnataka", "29"))); // no GSTIN (unregistered): fine
    }

    [Fact]
    public void Gstin_from_another_state_mistyped_gstin_and_missing_state_are_reported()
    {
        var other = Assert.Single(TaxCheck.Problems(new TaxCheck.Party("The buyer", "27AAACS1234F1Z3", "Karnataka", "29")));
        Assert.Contains("from Maharashtra (27), but the state is Karnataka (29)", other);
        Assert.Contains(TaxCheck.Problems(new TaxCheck.Party("The buyer", "27AAACS1234", "Maharashtra", "27")), p => p.Contains("looks mistyped"));
        Assert.Contains(TaxCheck.Problems(new TaxCheck.Party("The consignee", "", "", "")), p => p.StartsWith("The consignee has no state"));
    }

    [Fact]
    public void Describes_the_gst_the_quotation_gets()
    {
        Assert.Equal("CGST + SGST (Karnataka, same state)", TaxCheck.Describe("29", "29"));
        Assert.Equal("IGST (Maharashtra, another state)", TaxCheck.Describe("29", "27"));
    }
}
