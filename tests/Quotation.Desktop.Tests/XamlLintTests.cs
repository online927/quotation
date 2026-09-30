using System.Text.RegularExpressions;

namespace Quotation.Desktop.Tests;

public partial class XamlLintTests
{
    [GeneratedRegex(@"<Run\b[^>]*Text=""\{Binding(?![^""]*Mode=OneWay)[^""]*""")]
    private static partial Regex TwoWayRun();

    /// <summary>
    /// Run.Text binds TwoWay by default in Avalonia; with StringFormat this causes an infinite
    /// update loop (stack overflow). Every Run binding must be OneWay.
    /// </summary>
    [Fact]
    public void Run_bindings_are_one_way()
    {
        var root = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src", "Quotation.Desktop"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => TwoWayRun().Matches(File.ReadAllText(f)).Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();
        Assert.Empty(offenders);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Quotation.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
