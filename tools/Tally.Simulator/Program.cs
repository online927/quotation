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
            using var reader = new StreamReader(req.Body, Encoding.UTF8);
            var xml = engine.Handle(await reader.ReadToEndAsync());
            return Results.Text(xml, "text/xml", Encoding.UTF8);
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
