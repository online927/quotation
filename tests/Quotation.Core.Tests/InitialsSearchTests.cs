using Quotation.Core.Search;

namespace Quotation.Core.Tests;

/// <summary>Initials typed apart or together find the same company ("A B auto" = "AB auto" = "A.B. auto").</summary>
public class InitialsSearchTests
{
    private static readonly string[] Names =
    [
        "AB AUTO PARTS", "A.B. AUTOMOBILES", "A B AUTO WORKS", "A&B AUTO SPARES", "AB-AUTO ENGINEERING",
        "BHARAT AUTO SERVICE", "BOSCH AUTOMOTIVE", "ACE AUTOMATION", "A N TRADERS",
    ];

    private static SearchIndex<string> Index() => new(Names, [new SearchField<string>("Name", s => s, 1.0, IsPrimaryName: true)]);

    [Theory]
    [InlineData("A B auto")]
    [InlineData("AB auto")]
    [InlineData("a.b. auto")]
    [InlineData("a&b auto")]
    [InlineData("a-b auto")]
    public void Every_spelling_of_the_initials_finds_every_AB_company_and_nothing_else(string query)
    {
        var found = Index().Search(query, 20).Select(h => h.Item).ToHashSet();
        Assert.Equal(
            ["AB AUTO PARTS", "A.B. AUTOMOBILES", "A B AUTO WORKS", "A&B AUTO SPARES", "AB-AUTO ENGINEERING"],
            Names.Where(found.Contains));
    }

    [Fact]
    public void Initials_that_spell_a_stop_word_are_kept()
    {
        Assert.Equal("A N TRADERS", Index().Search("a n traders", 5)[0].Item);
        Assert.Contains("an", TextNormalizer.QueryTokens("A N traders"));
    }

    [Theory]
    [InlineData("10 m cable", "10m")]
    [InlineData("2 x 3 mm", "x")]
    [InlineData("v belt b 42", "b")]
    public void Units_and_lone_letters_are_not_joined(string query, string expected)
    {
        Assert.Contains(expected, TextNormalizer.QueryTokens(query));
    }

    [Fact]
    public void Index_has_both_the_letters_and_the_joined_initials()
    {
        var tokens = TextNormalizer.IndexTokens("A.B. AUTOMOBILES");
        Assert.Contains("a", tokens);
        Assert.Contains("b", tokens);
        Assert.Contains("ab", tokens);
    }
}
