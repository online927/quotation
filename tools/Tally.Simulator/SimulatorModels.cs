namespace Tally.Simulator;

public sealed class SimGst
{
    public DateOnly ApplicableFrom { get; set; } = new(2017, 7, 1);
    public string Hsn { get; set; } = "";
    public decimal Rate { get; set; }
    /// <summary>"Specify Details Here" or "As per Company/Stock Group".</summary>
    public bool InheritFromGroup { get; set; }
}

public sealed class SimStockItem
{
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public long AlterId { get; set; }
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public string PartNo { get; set; } = "";
    public string Parent { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "NOS";
    public string Description { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<SimGst> Gst { get; set; } = [];
    /// <summary>Use pre-TallyPrime-3 layout (HSN inside GSTDETAILS.LIST, Central/State/Integrated Tax heads).</summary>
    public bool LegacyFormat { get; set; }
    public List<(DateOnly Date, decimal Rate)> StandardPrices { get; set; } = [];
    public List<(DateOnly Date, string Level, decimal Rate)> PriceLevels { get; set; } = [];
    public bool Deleted { get; set; }
}

public sealed class SimStockGroup
{
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public long AlterId { get; set; }
    public string Name { get; set; } = "";
    public string Parent { get; set; } = "";
    public List<SimGst> Gst { get; set; } = [];
}

public sealed class SimUnit
{
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public long AlterId { get; set; }
    public string Name { get; set; } = "";
    public string FormalName { get; set; } = "";
    public int Decimals { get; set; }
}

public sealed class SimLedger
{
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public long AlterId { get; set; }
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public string Parent { get; set; } = "Sundry Debtors";
    public string MailingName { get; set; } = "";
    public List<string> Address { get; set; } = [];
    public string State { get; set; } = "";
    public string Pincode { get; set; } = "";
    public string Country { get; set; } = "India";
    public string Gstin { get; set; } = "";
    public string RegistrationType { get; set; } = "Regular";
    public string Pan { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Email { get; set; } = "";
    public bool LegacyFormat { get; set; }
    public List<(string Name, List<string> Address, string State, string Pincode)> ShipTo { get; set; } = [];
    public bool Deleted { get; set; }
}

public sealed class SimLedgerGroup
{
    public string Name { get; set; } = "";
    public string Parent { get; set; } = "";
}

public sealed class SimCompany
{
    public string Name { get; set; } = "T.SAIFUDDIN & CO.";
    public string Guid { get; set; } = "c0ffee00-0000-4000-8000-000000000001";
    public DateOnly FinancialYearFrom { get; set; } = new(2012, 4, 1);
    public DateOnly BooksFrom { get; set; } = new(2012, 4, 1);
    public string State { get; set; } = "Karnataka";
    public string Gstin { get; set; } = "29AAAFT0000A1Z0";
}
