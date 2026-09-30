using Quotation.Tally;
using Tally.Simulator;

namespace Quotation.Tally.Tests;

public sealed class Sim
{
    public TallySimulatorEngine Engine { get; }
    public SimulatorHttpHandler Handler { get; }

    public Sim(int products = 300, int customers = 200, DateOnly? today = null)
    {
        Engine = new TallySimulatorEngine(SampleDataset.Create(products, customers, today: today ?? new DateOnly(2026, 9, 30)));
        Handler = new SimulatorHttpHandler(Engine);
    }

    public TallyXmlClient Client(string company = "", int timeoutSeconds = 30) =>
        new(new HttpClient(Handler, disposeHandler: false),
            new TallyConnectionOptions("http://tally-sim:9000", company, TimeSpan.FromSeconds(timeoutSeconds)));
}
