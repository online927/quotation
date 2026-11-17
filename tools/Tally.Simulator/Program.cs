using System.Text;

namespace Tally.Simulator;

/// <summary>
/// TallyPrime simulator: answers XML export requests on http://localhost:9000 like TallyPrime does.
/// Usage: TallySimulator [--port 9000] [--products 20000] [--customers 3000]
/// </summary>
public static class SimulatorHost
{
    public static async Task Main(string[] args)
    {
        var port = ArgValue(args, "--port", 9000);
        var products = ArgValue(args, "--products", 20000);
        var customers = ArgValue(args, "--customers", 3000);

        var dataset = SampleDataset.Create(products, customers);
        var engine = new TallySimulatorEngine(dataset);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = builder.Build();

        app.MapPost("/", async (HttpRequest req) =>
        {
            // Like TallyPrime: a UTF-16 request (charset=utf-16) is answered in UTF-16, anything else in UTF-8.
            var unicode = req.ContentType?.Contains("utf-16", StringComparison.OrdinalIgnoreCase) == true;
            var encoding = unicode ? (Encoding)new UnicodeEncoding(false, false) : new UTF8Encoding(false);
            using var reader = new StreamReader(req.Body, encoding);
            var xml = engine.Handle(await reader.ReadToEndAsync());
            return Results.Bytes(encoding.GetBytes(xml), unicode ? "text/xml; charset=utf-16" : "text/xml; charset=utf-8");
        });
        app.MapGet("/", () => Results.Text("<RESPONSE>TallyPrime Server is Running</RESPONSE>", "text/xml"));

        Console.WriteLine($"Tally simulator: company '{dataset.Company.Name}', {dataset.StockItems.Count} stock items, " +
                          $"{dataset.Ledgers.Count} ledgers, period {dataset.PeriodFrom:dd-MMM-yyyy} to {dataset.PeriodTo:dd-MMM-yyyy}.");
        Console.WriteLine($"Listening on http://localhost:{port}  (Ctrl+C to stop)");
        await app.RunAsync();
    }

    private static int ArgValue(string[] args, string name, int fallback)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
    }
}
