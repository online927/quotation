using System.Diagnostics;
using Quotation.Core.Search;
using Tally.Simulator;
using Xunit.Abstractions;

namespace Quotation.Core.Tests;

public sealed record TestProduct(int Id, string Name, string Aliases = "", string PartNumber = "", string Brand = "", string Group = "", string Description = "");

public class SearchTests(ITestOutputHelper output)
{
    internal static readonly SearchField<TestProduct>[] Fields =
    [
        new("Name", p => p.Name, 1.0, FieldKind.Identifier, IsPrimaryName: true),
        new("Alias", p => p.Aliases, 0.95, FieldKind.Identifier),
        new("PartNumber", p => p.PartNumber, 1.2, FieldKind.Identifier),
        new("Brand", p => p.Brand, 0.6),
        new("Group", p => p.Group, 0.3),
        new("Description", p => p.Description, 0.3),
    ];

    private static readonly List<TestProduct> Sample =
    [
        new(1, "187-901-10-UNIVERSAL BEVEL PROTRACTOR", PartNumber: "187-901-10", Brand: "Mitutoyo", Group: "MITUTOYO"),
        new(2, "187-907-UNIVERSAL BEVEL PROTRACTOR 300MM BLADE", PartNumber: "187-907", Brand: "Mitutoyo", Group: "MITUTOYO"),
        new(3, "530-104-VERNIER CALIPER 0-150MM", PartNumber: "530-104", Brand: "Mitutoyo"),
        new(4, "500-196-30-DIGITAL CALIPER 0-150MM", Aliases: "ABSOLUTE DIGIMATIC CALIPER 150", PartNumber: "500-196-30", Brand: "Mitutoyo"),
        new(5, "POLYCAB 3 CORE 2.5 SQMM FLEXIBLE CABLE", Brand: "Polycab", Group: "Flexible Cables"),
        new(6, "POLYCAB FR 3 CORE 2.5 SQMM FLEXIBLE CABLE", Brand: "Polycab", Group: "Flexible Cables"),
        new(7, "HAVELLS 3 CORE 2.5 SQMM FLEXIBLE CABLE", Brand: "Havells", Group: "Flexible Cables"),
        new(8, "POLYCAB 3 CORE 1.5 SQMM FLEXIBLE CABLE", Brand: "Polycab", Group: "Flexible Cables"),
        new(9, "HSS DRILL BIT 10MM", Brand: "Addison", Group: "Drill Bits"),
        new(10, "HSS DRILL BIT 10.5MM", Brand: "Addison", Group: "Drill Bits"),
        new(11, "10MM ALLEN KEY SET", Brand: "Taparia", Group: "Spanners"),
        new(12, "CARBIDE END MILL 10MM 4 FLUTE", Brand: "YG-1", Group: "End Mills"),
        new(13, "2046S-DIAL INDICATOR 0-10MM", PartNumber: "2046S", Brand: "Mitutoyo"),
        new(14, "SAFETY HELMET WHITE (RATCHET)", Brand: "Karam"),
    ];

    private static SearchIndex<TestProduct> Index() => new(Sample, Fields);

    private static List<int> Ids(string query, int limit = 10) => Index().Search(query, limit).Select(h => h.Item.Id).ToList();

    [Fact]
    public void Universal_bevel_finds_the_protractors()
    {
        var ids = Ids("universal bevel");
        Assert.Equal([1, 2], ids.Take(2).OrderBy(x => x).ToList());
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public void Exact_name_ranks_first()
    {
        Assert.Equal(1, Ids("187-901-10-UNIVERSAL BEVEL PROTRACTOR")[0]);
    }

    [Theory]
    [InlineData("187-901-10", 1)]
    [InlineData("18790110", 1)]
    [InlineData("187 901 10", 1)]
    [InlineData("187-901", 1)]
    [InlineData("530104", 3)]
    [InlineData("2046s", 13)]
    public void Part_numbers_match_with_or_without_separators(string query, int expected)
    {
        Assert.Equal(expected, Ids(query)[0]);
    }

    [Fact]
    public void Ten_mm_shows_ten_mm_items_not_ten_point_five()
    {
        var ids = Ids("10mm");
        Assert.Contains(9, ids);
        Assert.Contains(11, ids);
        Assert.Contains(12, ids);
        Assert.DoesNotContain(10, ids); // 10.5MM
        Assert.Equal(ids.OrderBy(x => x), Ids("10 mm").OrderBy(x => x));
    }

    [Fact]
    public void Cable_specification_returns_all_three_plausible_cables()
    {
        var ids = Ids("3 core 2.5 sqmm cable");
        Assert.Equal([5, 6, 7], ids.OrderBy(x => x).ToList());
        Assert.Equal(ids.OrderBy(x => x), Ids("3 core 2.5 sq mm cable").OrderBy(x => x));
        Assert.Equal(ids.OrderBy(x => x), Ids("3core 2.5sqmm cable").OrderBy(x => x));
    }

    [Fact]
    public void Brand_narrows_results()
    {
        Assert.Equal([7], Ids("havells 3 core 2.5 sqmm"));
        Assert.Equal([5, 6], Ids("polycab 3 core 2.5 sqmm").OrderBy(x => x).ToList());
    }

    [Theory]
    [InlineData("univresal bevel", 1)]     // transposition
    [InlineData("universel bevel", 1)]     // substitution
    [InlineData("bevl protractor", 1)]     // deletion
    [InlineData("vernier calliper", 3)]    // insertion
    [InlineData("protracter", 1)]
    public void Typos_are_tolerated(string query, int expected)
    {
        var hits = Index().Search(query);
        Assert.Contains(hits.Take(2), h => h.Item.Id == expected);
        Assert.True(hits.First(h => h.Item.Id == expected).Evidence.UsedFuzzy);
    }

    [Fact]
    public void Prefix_typing_finds_items_immediately()
    {
        Assert.Contains(1, Ids("univ"));
        Assert.Contains(1, Ids("protr"));
        Assert.Contains(3, Ids("vern cal"));
    }

    [Fact]
    public void Contains_matching_inside_words()
    {
        Assert.Contains(4, Ids("gimatic")); // inside "DIGIMATIC" alias
    }

    [Fact]
    public void Alias_matches()
    {
        var hits = Index().Search("absolute digimatic caliper");
        Assert.Equal(4, hits[0].Item.Id);
        Assert.Contains("Alias", hits[0].Evidence.MatchedFields);
    }

    [Fact]
    public void Natural_language_words_are_ignored()
    {
        Assert.Equal(1, Ids("please quote for the universal bevel protractor")[0]);
    }

    [Fact]
    public void Numbers_are_not_fuzzy_matched()
    {
        Assert.DoesNotContain(8, Ids("3 core 2.5 sqmm")); // 1.5 sqmm must not match 2.5
    }

    [Fact]
    public void Evidence_reports_exact_identifier()
    {
        var hit = Index().Search("187-901-10")[0];
        Assert.True(hit.Evidence.ExactIdentifier);
        Assert.True(hit.Evidence.AllTokensMatched);
        Assert.False(hit.Evidence.UsedFuzzy);
    }

    [Fact]
    public void Empty_and_garbage_queries()
    {
        Assert.Empty(Index().Search(""));
        Assert.Empty(Index().Search("   "));
        Assert.Empty(Index().Search("zzzzqqqq"));
    }

    [Fact]
    public void Filter_excludes_items()
    {
        var hits = Index().Search("universal", filter: p => p.Id != 1);
        Assert.Equal([2], hits.Select(h => h.Item.Id).ToList());
    }

    [Theory]
    [InlineData("2.5 Sq.mm", "2.5sqmm")]
    [InlineData("10 MM", "10mm")]
    [InlineData("100 Meters", "100mtr")]
    [InlineData("Ø10mm", "ø10mm")]
    public void Query_normalization(string input, string expectedFirstToken)
    {
        Assert.Equal(expectedFirstToken, TextNormalizer.QueryTokens(input)[0]);
    }

    [Fact]
    public void Search_20000_products_is_fast()
    {
        var data = SampleDataset.Create(20000, 10, seed: 7);
        var products = data.StockItems.Select((i, n) => new TestProduct(n, i.Name, string.Join("\n", i.Aliases), i.PartNo, i.Category, i.Parent, i.Description)).ToList();
        var sw = Stopwatch.StartNew();
        var index = new SearchIndex<TestProduct>(products, Fields);
        var build = sw.Elapsed;

        string[] queries = ["universal bevel", "10mm", "3 core 2.5 sqmm cable", "vernier", "187-901", "calipr", "drill 10", "m", "grinder 125mm", "safty gogles", "polycab fr"];
        foreach (var q in queries) index.Search(q); // warm-up
        sw.Restart();
        const int rounds = 20;
        for (var r = 0; r < rounds; r++)
            foreach (var q in queries) index.Search(q);
        var perQuery = sw.Elapsed.TotalMilliseconds / (rounds * queries.Length);
        output.WriteLine($"Index build {build.TotalMilliseconds:0} ms for {products.Count} products; average query {perQuery:0.00} ms");

        Assert.Equal("187-901-10-UNIVERSAL BEVEL PROTRACTOR", index.Search("187-901-10 universal bevel")[0].Item.Name);
        Assert.True(perQuery < 25, $"Average query {perQuery:0.00} ms");
        Assert.True(build < TimeSpan.FromSeconds(5));
    }
}
