namespace Tally.Simulator;

/// <summary>
/// Deterministic, realistic sample data resembling an Indian industrial-supplies trader:
/// measuring instruments, cutting tools, cables, hand tools. Includes the specific items
/// and customers used in the specification examples.
/// </summary>
public sealed class SampleDataset
{
    public SimCompany Company { get; } = new();
    public List<SimStockItem> StockItems { get; } = [];
    public List<SimStockGroup> StockGroups { get; } = [];
    public List<SimUnit> Units { get; } = [];
    public List<SimLedger> Ledgers { get; } = [];
    public List<SimLedgerGroup> LedgerGroups { get; } = [];

    /// <summary>Tally's current period (Alt+F2).</summary>
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateOnly CurrentDate { get; set; }

    private long _alterId;
    private readonly Random _rng;

    public long NextAlterId() => ++_alterId;
    public long MaxAlterId => _alterId;

    private SampleDataset(int seed) => _rng = new Random(seed);

    public static SampleDataset Create(int products = 20000, int customers = 3000, int seed = 42, DateOnly? today = null)
    {
        var d = new SampleDataset(seed);
        var t = today ?? DateOnly.FromDateTime(DateTime.Today);
        var fyStart = t.Month >= 4 ? t.Year : t.Year - 1;
        d.PeriodFrom = new DateOnly(fyStart, 4, 1);
        d.PeriodTo = new DateOnly(fyStart + 1, 3, 31);
        d.CurrentDate = t;
        d.BuildUnits();
        d.BuildGroups();
        d.BuildKnownItems();
        d.BuildGeneratedItems(Math.Max(0, products - d.StockItems.Count));
        d.BuildLedgerGroups();
        d.BuildKnownLedgers();
        d.BuildGeneratedLedgers(Math.Max(0, customers - d.Ledgers.Count(l => l.Parent.Contains("Debtors"))));
        return d;
    }

    private string NewGuid()
    {
        var bytes = new byte[16];
        _rng.NextBytes(bytes);
        return new Guid(bytes).ToString();
    }

    private void BuildUnits()
    {
        foreach (var (name, formal, dec) in new[]
                 {
                     ("NOS", "Numbers", 0), ("MTR", "Metres", 2), ("SET", "Sets", 0), ("PCS", "Pieces", 0),
                     ("KGS", "Kilograms", 3), ("BOX", "Boxes", 0), ("PKT", "Packets", 0), ("ROLL", "Rolls", 0),
                 })
        {
            Units.Add(new SimUnit { Guid = NewGuid(), AlterId = NextAlterId(), Name = name, FormalName = formal, Decimals = dec });
        }
    }

    private static readonly (string Group, string Parent, string Hsn, decimal Gst)[] Groups =
    [
        ("Measuring Instruments", "", "9017", 18),
        ("MITUTOYO", "Measuring Instruments", "90173029", 18),
        ("INSIZE", "Measuring Instruments", "90173029", 18),
        ("BAKER", "Measuring Instruments", "90318000", 18),
        ("Cutting Tools", "", "8207", 18),
        ("Drill Bits", "Cutting Tools", "82075000", 18),
        ("Taps & Dies", "Cutting Tools", "82074090", 18),
        ("End Mills", "Cutting Tools", "82077010", 18),
        ("Cables & Wires", "", "8544", 18),
        ("Flexible Cables", "Cables & Wires", "85444999", 18),
        ("Hand Tools", "", "8205", 18),
        ("Spanners", "Hand Tools", "82041120", 18),
        ("Screwdrivers", "Hand Tools", "82054000", 18),
        ("Power Tools", "", "8467", 18),
        ("Safety Products", "", "6506", 18),
        ("Abrasives", "", "6805", 18),
    ];

    private void BuildGroups()
    {
        foreach (var (g, p, hsn, gst) in Groups)
        {
            StockGroups.Add(new SimStockGroup
            {
                Guid = NewGuid(), AlterId = NextAlterId(), Name = g, Parent = p,
                Gst = [new SimGst { Hsn = hsn, Rate = gst }],
            });
        }
    }

    private SimStockItem AddItem(string name, string group, string unit, string hsn, decimal gst, decimal? rate,
        string partNo = "", string category = "", string description = "", IEnumerable<string>? aliases = null,
        bool inherit = false, bool legacy = false)
    {
        var item = new SimStockItem
        {
            Guid = NewGuid(),
            AlterId = NextAlterId(),
            Name = name,
            PartNo = partNo,
            Parent = group,
            Category = category,
            Unit = unit,
            Description = description,
            Aliases = aliases?.ToList() ?? [],
            LegacyFormat = legacy,
            Gst = inherit ? [new SimGst { InheritFromGroup = true }] : [new SimGst { Hsn = hsn, Rate = gst }],
        };
        if (rate is not null) item.StandardPrices.Add((new DateOnly(2024, 4, 1), Math.Round(rate.Value * 0.92m, 0)));
        if (rate is not null) item.StandardPrices.Add((new DateOnly(2025, 4, 1), rate.Value));
        StockItems.Add(item);
        return item;
    }

    private void BuildKnownItems()
    {
        AddItem("187-901-10-UNIVERSAL BEVEL PROTRACTOR", "MITUTOYO", "NOS", "90172020", 18, 15600m,
            partNo: "187-901-10", category: "Mitutoyo", description: "Brand: Mitutoyo\nRange: 0-360°, Blade 150 mm");
        AddItem("187-907-UNIVERSAL BEVEL PROTRACTOR 300MM BLADE", "MITUTOYO", "NOS", "90172020", 18, 18950m,
            partNo: "187-907", category: "Mitutoyo");
        AddItem("530-104-VERNIER CALIPER 0-150MM", "MITUTOYO", "NOS", "90173029", 18, 3250m, partNo: "530-104", category: "Mitutoyo");
        AddItem("500-196-30-DIGITAL CALIPER 0-150MM", "MITUTOYO", "NOS", "90173029", 18, 11800m, partNo: "500-196-30", category: "Mitutoyo",
            aliases: ["ABSOLUTE DIGIMATIC CALIPER 150"]);
        AddItem("103-137-OUTSIDE MICROMETER 0-25MM", "MITUTOYO", "NOS", "90173029", 18, 4150m, partNo: "103-137", category: "Mitutoyo");
        AddItem("2046S-DIAL INDICATOR 0-10MM", "MITUTOYO", "NOS", "90318000", 18, 5400m, partNo: "2046S", category: "Mitutoyo");

        // Deliberately ambiguous cable family for AI safety tests.
        AddItem("POLYCAB 3 CORE 2.5 SQMM FLEXIBLE CABLE", "Flexible Cables", "MTR", "85444999", 18, 118m, category: "Polycab");
        AddItem("POLYCAB FR 3 CORE 2.5 SQMM FLEXIBLE CABLE", "Flexible Cables", "MTR", "85444999", 18, 126m, category: "Polycab");
        AddItem("HAVELLS 3 CORE 2.5 SQMM FLEXIBLE CABLE", "Flexible Cables", "MTR", "85444999", 18, 121m, category: "Havells");
        AddItem("POLYCAB 3 CORE 1.5 SQMM FLEXIBLE CABLE", "Flexible Cables", "MTR", "85444999", 18, 76m, category: "Polycab");

        // Items that exercise inheritance and legacy formats.
        AddItem("HSS DRILL BIT 10MM", "Drill Bits", "NOS", "", 0, 145m, category: "Addison", inherit: true);
        AddItem("HSS DRILL BIT 10.5MM", "Drill Bits", "NOS", "", 0, 162m, category: "Addison", inherit: true);
        AddItem("10MM ALLEN KEY SET", "Spanners", "SET", "82041120", 18, 690m, category: "Taparia", legacy: true);
        AddItem("SAFETY HELMET WHITE (RATCHET)", "Safety Products", "NOS", "65061090", 18, 285m, category: "Karam", legacy: true);
        var noRate = AddItem("CARBIDE END MILL 10MM 4 FLUTE", "End Mills", "NOS", "82077010", 18, null, category: "YG-1");
        noRate.PriceLevels.Add((new DateOnly(2025, 4, 1), "Dealer", 1450m));
    }

    private static readonly string[] Brands =
        ["Mitutoyo", "Insize", "Baker", "Yamayo", "Addison", "Totem", "YG-1", "Taparia", "Stanley", "Bosch", "Makita",
         "Polycab", "Havells", "Finolex", "KEI", "Karam", "Norton", "Carborundum", "Jhalani", "Groz"];

    private void BuildGeneratedItems(int count)
    {
        var families = new (string Name, string Group, string Unit, string Hsn, decimal Gst, decimal Min, decimal Max, Func<Random, string> Spec)[]
        {
            ("VERNIER CALIPER", "INSIZE", "NOS", "90173029", 18, 900, 9000, r => $"0-{new[] { 150, 200, 300, 600 }[r.Next(4)]}MM"),
            ("DIGITAL CALIPER", "INSIZE", "NOS", "90173029", 18, 2500, 25000, r => $"0-{new[] { 150, 200, 300 }[r.Next(3)]}MM"),
            ("OUTSIDE MICROMETER", "INSIZE", "NOS", "90173029", 18, 1500, 20000, r => { var s = r.Next(0, 12) * 25; return $"{s}-{s + 25}MM"; }),
            ("DIAL GAUGE", "BAKER", "NOS", "90318000", 18, 1200, 9000, r => $"0-{new[] { 1, 5, 10, 25, 50 }[r.Next(5)]}MM"),
            ("BORE GAUGE", "BAKER", "NOS", "90318000", 18, 6000, 45000, r => $"{r.Next(6, 60)}-{r.Next(60, 160)}MM"),
            ("HSS DRILL BIT", "Drill Bits", "NOS", "82075000", 18, 20, 2500, r => $"{r.Next(1, 40)}.{r.Next(0, 10)}MM"),
            ("SOLID CARBIDE DRILL", "Drill Bits", "NOS", "82075000", 18, 600, 12000, r => $"{r.Next(2, 20)}.{r.Next(0, 10)}MM"),
            ("HSS MACHINE TAP", "Taps & Dies", "SET", "82074090", 18, 150, 3500, r => $"M{r.Next(3, 30)}X{new[] { "0.5", "0.75", "1.0", "1.25", "1.5", "2.0" }[r.Next(6)]}"),
            ("CARBIDE END MILL", "End Mills", "NOS", "82077010", 18, 400, 15000, r => $"{r.Next(2, 25)}MM {new[] { 2, 3, 4, 6 }[r.Next(4)]} FLUTE"),
            ("FLEXIBLE CABLE", "Flexible Cables", "MTR", "85444999", 18, 12, 900, r => $"{new[] { 1, 2, 3, 4, 5, 7 }[r.Next(6)]} CORE {new[] { "0.75", "1", "1.5", "2.5", "4", "6", "10" }[r.Next(7)]} SQMM"),
            ("ARMOURED CABLE", "Cables & Wires", "MTR", "85444999", 18, 80, 3500, r => $"{new[] { 2, 3, 4 }[r.Next(3)]} CORE {new[] { "4", "6", "10", "16", "25", "35" }[r.Next(6)]} SQMM ALU"),
            ("DOUBLE ENDED SPANNER", "Spanners", "NOS", "82041120", 18, 40, 900, r => $"{r.Next(6, 32)}X{r.Next(7, 36)}MM"),
            ("SCREWDRIVER", "Screwdrivers", "NOS", "82054000", 18, 30, 600, r => $"{new[] { "FLAT", "PHILIPS", "TORX" }[r.Next(3)]} {r.Next(3, 10)}X{r.Next(75, 300)}MM"),
            ("ANGLE GRINDER", "Power Tools", "NOS", "84672900", 18, 2500, 18000, r => $"{new[] { 100, 115, 125, 180 }[r.Next(4)]}MM {r.Next(6, 24) * 100}W"),
            ("CUTTING WHEEL", "Abrasives", "PCS", "68042290", 18, 25, 450, r => $"{new[] { 100, 125, 180, 355 }[r.Next(4)]}X{new[] { "1.0", "1.6", "2.5", "3.0" }[r.Next(4)]}MM"),
            ("SAFETY GOGGLES", "Safety Products", "NOS", "90049090", 18, 60, 900, r => new[] { "CLEAR", "DARK", "ANTI-FOG" }[r.Next(3)]),
        };

        var seen = new HashSet<string>(StockItems.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
        var i = 0;
        var guard = 0;
        while (i < count && guard++ < count * 20)
        {
            var f = families[_rng.Next(families.Length)];
            var brand = Brands[_rng.Next(Brands.Length)];
            var code = $"{_rng.Next(100, 999)}-{_rng.Next(100, 999)}";
            var usesCode = _rng.NextDouble() < 0.55;
            var spec = f.Spec(_rng);
            var name = usesCode ? $"{code}-{f.Name} {spec}" : $"{brand.ToUpperInvariant()} {f.Name} {spec}";
            if (!seen.Add(name)) continue;
            var rate = Math.Round((decimal)_rng.NextDouble() * (f.Max - f.Min) + f.Min, 0);
            var inherit = _rng.NextDouble() < 0.1;
            var item = AddItem(name, f.Group, f.Unit, f.Hsn, f.Gst, _rng.NextDouble() < 0.97 ? rate : null,
                partNo: usesCode ? code : "", category: brand,
                description: _rng.NextDouble() < 0.3 ? $"Brand: {brand}" : "",
                aliases: _rng.NextDouble() < 0.1 ? [$"{brand.ToUpperInvariant()} {f.Name.ToLowerInvariant()} {spec}"] : null,
                inherit: inherit, legacy: _rng.NextDouble() < 0.2);
            i++;
        }
    }

    private void BuildLedgerGroups()
    {
        LedgerGroups.AddRange([
            new SimLedgerGroup { Name = "Sundry Debtors", Parent = "Current Assets" },
            new SimLedgerGroup { Name = "Debtors - Bangalore", Parent = "Sundry Debtors" },
            new SimLedgerGroup { Name = "Debtors - Outstation", Parent = "Sundry Debtors" },
            new SimLedgerGroup { Name = "Sundry Creditors", Parent = "Current Liabilities" },
            new SimLedgerGroup { Name = "Sales Accounts", Parent = "" },
        ]);
    }

    private void BuildKnownLedgers()
    {
        Ledgers.Add(new SimLedger
        {
            Guid = NewGuid(), AlterId = NextAlterId(),
            Name = "SONEPAR INDIA PRIVATE LIMITED", MailingName = "Sonepar India Private Limited",
            Parent = "Debtors - Outstation",
            Address = ["Plot No. 12, MIDC Industrial Area", "Chakan, Pune"], State = "Maharashtra", Pincode = "410501",
            Gstin = "27AAACS1234F1Z3", Pan = "AAACS1234F", Contact = "Mr. Rahul Deshmukh",
            Phone = "020-66112233", Mobile = "9822012345", Email = "purchase.pune@sonepar.example",
            ShipTo = [("Sonepar India Pvt Ltd - Bangalore Warehouse", ["No. 5, Peenya Industrial Area", "Bangalore"], "Karnataka", "560058")],
        });
        Ledgers.Add(new SimLedger
        {
            Guid = NewGuid(), AlterId = NextAlterId(),
            Name = "SONEPAR INDIA PVT LTD - CHENNAI", MailingName = "Sonepar India Private Limited",
            Parent = "Debtors - Outstation",
            Address = ["45, Guindy Industrial Estate", "Chennai"], State = "Tamil Nadu", Pincode = "600032",
            Gstin = "33AAACS1234F1Z9", Pan = "AAACS1234F", Email = "purchase.chennai@sonepar.example", LegacyFormat = true,
        });
        Ledgers.Add(new SimLedger
        {
            Guid = NewGuid(), AlterId = NextAlterId(),
            Name = "BHARAT PRECISION ENGINEERING", Parent = "Debtors - Bangalore",
            Address = ["No. 18, 2nd Cross, Peenya 2nd Stage", "Bangalore"], State = "Karnataka", Pincode = "560058",
            Gstin = "29ABCPB1234K1Z2", Contact = "Mr. Suresh", Mobile = "9845098450", Email = "suresh@bharatprecision.example",
        });
        Ledgers.Add(new SimLedger
        {
            Guid = NewGuid(), AlterId = NextAlterId(),
            Name = "CASH CUSTOMER", Parent = "Sundry Debtors", State = "Karnataka", RegistrationType = "Unregistered/Consumer",
        });
        Ledgers.Add(new SimLedger
        {
            Guid = NewGuid(), AlterId = NextAlterId(),
            Name = "ABC STEEL SUPPLIERS", Parent = "Sundry Creditors", State = "Karnataka", Gstin = "29AABCA1111A1Z1",
        });
        Ledgers.Add(new SimLedger { Guid = NewGuid(), AlterId = NextAlterId(), Name = "Sales - GST 18%", Parent = "Sales Accounts" });
    }

    private static readonly (string State, string Code, string City, string Pin)[] Places =
    [
        ("Karnataka", "29", "Bangalore", "5600"), ("Karnataka", "29", "Mysore", "5700"), ("Karnataka", "29", "Hubli", "5800"),
        ("Tamil Nadu", "33", "Chennai", "6000"), ("Tamil Nadu", "33", "Coimbatore", "6410"), ("Maharashtra", "27", "Pune", "4110"),
        ("Maharashtra", "27", "Mumbai", "4000"), ("Telangana", "36", "Hyderabad", "5000"), ("Kerala", "32", "Kochi", "6820"),
        ("Andhra Pradesh", "37", "Visakhapatnam", "5300"), ("Gujarat", "24", "Ahmedabad", "3800"), ("Delhi", "07", "New Delhi", "1100"),
    ];

    private static readonly string[] NameParts1 =
        ["SRI", "SHREE", "NEW", "ROYAL", "SUPREME", "GLOBAL", "UNITED", "PRECISION", "ACE", "STAR", "VIJAYA", "LAKSHMI",
         "GANESH", "BALAJI", "KRISHNA", "SAI", "OM", "JAI", "PIONEER", "APEX", "ALPHA", "DELTA", "MODERN", "METRO"];
    private static readonly string[] NameParts2 =
        ["ENGINEERING", "INDUSTRIES", "TOOLS", "ENTERPRISES", "AUTOMATION", "FABRICATORS", "MACHINE WORKS", "COMPONENTS",
         "TECHNOLOGIES", "ELECTRICALS", "HARDWARE", "AGENCIES", "TRADERS", "SYSTEMS", "CASTINGS", "FORGINGS"];
    private static readonly string[] Suffixes = ["", "", " PVT LTD", " PRIVATE LIMITED", " LLP", " & CO."];

    private void BuildGeneratedLedgers(int count)
    {
        var seen = new HashSet<string>(Ledgers.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
        var i = 0;
        var guard = 0;
        while (i < count && guard++ < count * 20)
        {
            var place = Places[_rng.Next(Places.Length)];
            var name = $"{NameParts1[_rng.Next(NameParts1.Length)]} {NameParts2[_rng.Next(NameParts2.Length)]}{Suffixes[_rng.Next(Suffixes.Length)]}";
            if (!seen.Add(name))
            {
                name = $"{name} - {place.City.ToUpperInvariant()}";
                if (!seen.Add(name)) continue;
            }
            var pan = $"{RandLetters(5)}{_rng.Next(1000, 9999)}{RandLetters(1)}";
            var registered = _rng.NextDouble() < 0.9;
            Ledgers.Add(new SimLedger
            {
                Guid = NewGuid(),
                AlterId = NextAlterId(),
                Name = name,
                MailingName = name,
                Parent = place.City == "Bangalore" ? "Debtors - Bangalore" : _rng.NextDouble() < 0.5 ? "Debtors - Outstation" : "Sundry Debtors",
                Address = [$"No. {_rng.Next(1, 500)}, {new[] { "1st", "2nd", "3rd", "4th" }[_rng.Next(4)]} Main Road", $"{place.City} Industrial Area", place.City],
                State = place.State,
                Pincode = place.Pin + _rng.Next(10, 99),
                Gstin = registered ? $"{place.Code}{pan}1Z{_rng.Next(0, 10)}" : "",
                RegistrationType = registered ? "Regular" : "Unregistered/Consumer",
                Pan = pan,
                Contact = _rng.NextDouble() < 0.6 ? $"Mr. {new[] { "Ravi", "Kumar", "Prakash", "Anil", "Mahesh", "Imran", "Joseph" }[_rng.Next(7)]}" : "",
                Mobile = $"9{_rng.Next(100000000, 999999999)}",
                Email = _rng.NextDouble() < 0.7 ? $"purchase{_rng.Next(1, 999)}@example.in" : "",
                LegacyFormat = _rng.NextDouble() < 0.3,
            });
            i++;
        }
    }

    private string RandLetters(int n) => new(Enumerable.Range(0, n).Select(_ => (char)('A' + _rng.Next(26))).ToArray());
}
