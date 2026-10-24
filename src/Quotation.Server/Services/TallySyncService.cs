using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Core.Gst;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Tally;

namespace Quotation.Server.Services;

public sealed class SyncAlreadyRunningException() : Exception("A synchronization is already running.");

/// <summary>Raw Tally values kept per product so GST/HSN/rate can be re-resolved for any date without Tally.</summary>
public sealed record ProductTallyData(
    List<TallyGstEntry> Gst,
    List<TallyGstEntry> Hsn,
    List<TallyPrice> StandardPrices,
    List<TallyPrice> PriceLevels);

public sealed record GroupTallyData(List<TallyGstEntry> Gst, List<TallyGstEntry> Hsn);

/// <summary>
/// Synchronizes Tally masters into the local database. Full sync replaces everything; incremental
/// sync fetches only masters whose AlterID is higher than the last synchronized value and detects
/// deletions with a lightweight GUID listing. All writes happen in one transaction, so an
/// interrupted sync never leaves partial data.
/// </summary>
public sealed class TallySyncService(
    IServiceScopeFactory scopes,
    TallyGateway gateway,
    SettingsService settings,
    RuntimeStatus runtime,
    FinancialYearService fy,
    LastRateService lastRates,
    TimeProvider clock,
    ILogger<TallySyncService> log)
{
    public const string SettingsHashKey = "state:sync.settingshash";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Raised after a sync changed products or customers (search indexes subscribe).</summary>
    public event Action? DataChanged;

    public bool IsRunning => _gate.CurrentCount == 0;

    /// <summary>Tells the search indexes that product data changed outside a sync (e.g. last rates after an approval).</summary>
    public void NotifyDataChanged() => DataChanged?.Invoke();

    private CancellationTokenSource? _lastRatesCts;

    /// <summary>Stops a running last-rate read after the current request. False when none is running.</summary>
    public bool StopLastRates()
    {
        var cts = _lastRatesCts;
        if (cts is null) return false;
        cts.Cancel();
        return true;
    }

    /// <summary>
    /// Reads last quoted/sold rates completely from Tally's vouchers (started from the Tally page or by the nightly
    /// sync). Light on Tally: one voucher type and one week per request, with pauses; can be stopped at any time.
    /// </summary>
    public async Task<SyncRunDto> RunLastRatesAsync(string triggeredBy, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new SyncAlreadyRunningException();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _lastRatesCts = cts;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var run = new SyncRun { Kind = SyncKind.LastRates, Status = SyncStatus.Running, StartedUtc = clock.GetUtcNow().UtcDateTime, TriggeredBy = triggeredBy };
        try
        {
            db.SyncRuns.Add(run);
            await db.SaveChangesAsync(ct);
            runtime.SetSync(true, "Reading last rates from Tally…");
            var info = await gateway.Companies().GetCompanyInfoAsync(settings.Quotation.FinancialYearStartMonth, cts.Token);
            var result = await lastRates.RebuildAsync(db, info.Company.MaxVoucherAlterId, fy.Today(), m => runtime.SetSync(true, m), cts.Token);
            run.Status = result.Failed ? SyncStatus.Failed : SyncStatus.Succeeded;
            run.ProductsChanged = result.Changed;
            run.Message = result.Message;
            if (result.Changed > 0) DataChanged?.Invoke();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            run.Status = SyncStatus.Failed;
            run.Message = "Stopped. Tally was not changed; the previous last rates are kept.";
        }
        catch (TallyException ex)
        {
            run.Status = SyncStatus.Failed;
            run.Message = "Reading last rates stopped: " + ex.Message;
        }
        finally
        {
            _lastRatesCts = null;
            run.FinishedUtc = clock.GetUtcNow().UtcDateTime;
            foreach (var e in db.ChangeTracker.Entries<Product>().Where(e => e.State != EntityState.Unchanged).ToList()) e.State = EntityState.Detached;
            await db.SaveChangesAsync(CancellationToken.None);
            runtime.SetSync(false, null);
            _gate.Release();
        }
        log.LogInformation("Last rates run {Status}: {Message}", run.Status, run.Message);
        return ToDto(run);
    }

    public async Task<SyncRunDto> RunAsync(SyncKind requested, string triggeredBy, CancellationToken ct)
    {
        if (requested == SyncKind.LastRates) return await RunLastRatesAsync(triggeredBy, ct);
        if (!await _gate.WaitAsync(0, ct)) throw new SyncAlreadyRunningException();
        try
        {
            return await RunCoreAsync(requested, triggeredBy, ct);
        }
        finally
        {
            runtime.SetSync(false, null);
            _gate.Release();
        }
    }

    /// <summary>
    /// Bump when the way Tally data is read changes, so the next sync re-reads every master once
    /// (2: Tally's reserved "Not Applicable" stock category is no longer used as a brand).
    /// </summary>
    private const int MappingVersion = 2;

    private async Task<SyncRunDto> RunCoreAsync(SyncKind requested, string triggeredBy, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var today = fy.Today();
        var tally = settings.Tally;
        var settingsHash = $"v{MappingVersion};{string.Join("|", tally.CustomerGroups)};{tally.RateSource};{tally.PriceLevel};{tally.BrandField};{tally.ManufacturerField};{tally.DerivePartNumberFromName}";

        var lastRun = await db.SyncRuns.Where(r => r.Kind != SyncKind.LastRates && (r.Status == SyncStatus.Succeeded || r.Status == SyncStatus.Skipped))
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        var previousHash = settings.Get<StateString>(SettingsHashKey).Value;
        var kind = requested;
        if (kind == SyncKind.Incremental && (lastRun?.MaxAlterId is null || previousHash != settingsHash || !await db.Products.AnyAsync(ct)))
        {
            kind = SyncKind.Full; // nothing to be incremental against, or mapping settings changed
        }

        var run = new SyncRun { Kind = kind, Status = SyncStatus.Running, StartedUtc = now, TriggeredBy = triggeredBy };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(ct);
        runtime.SetSync(true, $"{kind} sync started");
        var sw = Stopwatch.StartNew();

        try
        {
            var masters = new TallyMasterService(gateway.CreateClient(),
                tally.FetchMode.Equals("All", StringComparison.OrdinalIgnoreCase) ? TallyFetchMode.All : TallyFetchMode.Selected);
            var info = await gateway.Companies().GetCompanyInfoAsync(settings.Quotation.FinancialYearStartMonth, ct);
            runtime.SetTally(true, info.Company.Name, null, clock.GetUtcNow().UtcDateTime, info.ActiveFinancialYearStart);
            if (info.ActiveFinancialYearStart is { } fyStart) await fy.RememberDetectedAsync(fyStart, ct);
            var maxAlterId = info.Company.MaxMasterAlterId;

            if (kind == SyncKind.Incremental && maxAlterId > 0 && maxAlterId == lastRun!.MaxAlterId)
            {
                // Masters unchanged, but new quotations/invoices may carry newer last rates.
                var rates = await UpdateLastRatesAsync(db, info.Company.MaxVoucherAlterId, today, ct);
                run.Status = SyncStatus.Skipped;
                run.MaxAlterId = maxAlterId;
                run.FinishedUtc = clock.GetUtcNow().UtcDateTime;
                run.Message = ("No changes in Tally masters since the last sync. " + rates.Message).Trim();
                await db.SaveChangesAsync(ct);
                if (rates.Changed > 0) DataChanged?.Invoke();
                return ToDto(run);
            }
            long? since = kind == SyncKind.Incremental ? lastRun!.MaxAlterId : null;

            runtime.SetSync(true, "Reading units and stock groups…");
            var units = await ToListAsync(masters.GetUnitsAsync(ct), ct);
            var groups = await ToListAsync(masters.GetStockGroupsAsync(null, ct), ct);

            runtime.SetSync(true, "Reading stock items…");
            var items = await ToListAsync(masters.GetStockItemsAsync(since, ct), ct);
            runtime.SetSync(true, "Reading customers…");
            var ledgers = await ToListAsync(masters.GetLedgersAsync(tally.CustomerGroups, today, since, ct), ct);

            HashSet<string>? liveItemGuids = null, liveLedgerGuids = null;
            if (kind == SyncKind.Full)
            {
                liveItemGuids = items.Select(i => i.Guid).ToHashSet();
                liveLedgerGuids = ledgers.Select(l => l.Guid).ToHashSet();
            }
            else
            {
                runtime.SetSync(true, "Checking for deleted masters…");
                liveItemGuids = (await ToListAsync(masters.GetStockItemIdsAsync(ct), ct)).Select(i => i.Guid).ToHashSet();
                liveLedgerGuids = (await ToListAsync(masters.GetLedgerIdsAsync(tally.CustomerGroups, ct), ct)).Select(i => i.Guid).ToHashSet();
            }

            runtime.SetSync(true, "Saving…");
            // All changes are prepared in memory and written by the single SaveChanges below, which runs in one
            // transaction: the database is locked for writing only while the rows are written, so quotations
            // being saved on other PCs never wait behind the preparation of a 20,000-item sync.
            var syncedAt = clock.GetUtcNow().UtcDateTime;

            UpsertUnits(db, units, syncedAt);
            var groupMap = UpsertGroups(db, await db.StockGroups.ToListAsync(ct), groups, syncedAt);
            var groupsChanged = since is null || groups.Any(g => g.AlterId > since);

            var existingProducts = await db.Products.ToDictionaryAsync(p => p.TallyGuid, ct);
            foreach (var item in items)
            {
                if (!existingProducts.TryGetValue(item.Guid, out var product))
                {
                    product = new Product { TallyGuid = item.Guid };
                    db.Products.Add(product);
                    existingProducts[item.Guid] = product;
                }
                ApplyItem(product, item, groupMap, tally, today, syncedAt);
                run.ProductsChanged++;
            }
            if (groupsChanged && since is not null)
            {
                // Group GST changes do not alter the items' AlterID: re-resolve inheriting items.
                var changedGuids = items.Select(i => i.Guid).ToHashSet();
                foreach (var p in existingProducts.Values.Where(p => p.GstInherited && !changedGuids.Contains(p.TallyGuid)))
                {
                    ResolveProductGst(p, groupMap, today);
                }
            }
            foreach (var p in existingProducts.Values)
            {
                var alive = liveItemGuids.Contains(p.TallyGuid);
                if (p.IsDeleted == alive)
                {
                    p.IsDeleted = !alive;
                    if (!alive) run.ProductsDeleted++;
                }
            }

            var existingCustomers = await db.Customers.ToDictionaryAsync(c => c.TallyGuid, ct);
            foreach (var ledger in ledgers)
            {
                if (!existingCustomers.TryGetValue(ledger.Guid, out var customer))
                {
                    customer = new Customer { TallyGuid = ledger.Guid };
                    db.Customers.Add(customer);
                    existingCustomers[ledger.Guid] = customer;
                }
                ApplyLedger(customer, ledger, syncedAt);
                run.CustomersChanged++;
            }
            foreach (var c in existingCustomers.Values)
            {
                var alive = liveLedgerGuids.Contains(c.TallyGuid);
                if (c.IsDeleted == alive)
                {
                    c.IsDeleted = !alive;
                    if (!alive) run.CustomersDeleted++;
                }
            }

            run.Status = SyncStatus.Succeeded;
            run.MaxAlterId = maxAlterId > 0 ? maxAlterId : Math.Max(items.Select(i => i.AlterId).DefaultIfEmpty(0).Max(), since ?? 0);
            run.FinishedUtc = clock.GetUtcNow().UtcDateTime;
            run.Message = $"{items.Count} stock items and {ledgers.Count} customers read in {sw.Elapsed.TotalSeconds:0.0} s.";
            await db.SaveChangesAsync(ct);
            await settings.SaveAsync(SettingsHashKey, new StateString { Value = settingsHash }, "system", ct);

            // Daytime syncs only pick up new quotations/invoices; the complete read runs at night or on request.
            var lastRateResult = kind == SyncKind.Incremental || !settings.Tally.UseLastRate
                ? await UpdateLastRatesAsync(db, info.Company.MaxVoucherAlterId, today, ct)
                : new LastRateResult(0, lastRates.IsBuilt ? "" : LastRateService.NotReadYetMessage);
            if (lastRateResult.Message.Length > 0)
            {
                run.Message += " " + lastRateResult.Message;
                await db.SaveChangesAsync(ct);
            }

            log.LogInformation("{Kind} sync completed: {Products} products, {Customers} customers changed, {PDel}/{CDel} deleted, {Seconds:0.0}s",
                kind, run.ProductsChanged, run.CustomersChanged, run.ProductsDeleted, run.CustomersDeleted, sw.Elapsed.TotalSeconds);
            if (run.ProductsChanged + run.CustomersChanged + run.ProductsDeleted + run.CustomersDeleted + lastRateResult.Changed > 0 || kind == SyncKind.Full)
            {
                DataChanged?.Invoke();
            }
            return ToDto(run);
        }
        catch (Exception ex) when (ex is TallyException or OperationCanceledException or DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var failed = await db.SyncRuns.FirstAsync(r => r.Id == run.Id, CancellationToken.None);
            failed.Status = SyncStatus.Failed;
            failed.FinishedUtc = clock.GetUtcNow().UtcDateTime;
            failed.Message = ex is OperationCanceledException ? "Synchronization was cancelled." : ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            if (ex is TallyUnavailableException) runtime.SetTally(false, null, ex.Message, clock.GetUtcNow().UtcDateTime, null);
            log.LogWarning("{Kind} sync failed: {Message}", kind, failed.Message);
            return ToDto(failed);
        }
    }

    /// <summary>Last rates are an addition: a problem reading vouchers never fails the master sync.</summary>
    private async Task<LastRateResult> UpdateLastRatesAsync(QuotationDbContext db, long companyVoucherAlterId, DateOnly today,
        CancellationToken ct)
    {
        try
        {
            return await lastRates.UpdateAsync(db, companyVoucherAlterId, today, ct);
        }
        catch (TallyException ex)
        {
            // Drop any half-applied product changes; keep the sync run itself tracked.
            foreach (var e in db.ChangeTracker.Entries<Product>().Where(e => e.State != EntityState.Unchanged).ToList()) e.State = EntityState.Detached;
            log.LogWarning("Last rates were not updated: {Message}", ex.Message);
            return new LastRateResult(0, "Last rates were not updated: " + ex.Message);
        }
    }

    /// <summary>Marks syncs left "Running" by a crash or restart as failed.</summary>
    public static async Task RecoverInterruptedAsync(QuotationDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var stale = await db.SyncRuns.Where(r => r.Status == SyncStatus.Running).ToListAsync(ct);
        foreach (var r in stale)
        {
            r.Status = SyncStatus.Failed;
            r.FinishedUtc = nowUtc;
            r.Message = "Interrupted (server restarted). No partial data was saved.";
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }

    public static SyncRunDto ToDto(SyncRun r) => new(r.Id, r.Kind, r.Status, r.StartedUtc, r.FinishedUtc,
        r.ProductsChanged, r.CustomersChanged, r.ProductsDeleted, r.CustomersDeleted, r.TriggeredBy, r.Message);

    // ---------------- mapping ----------------

    private static void UpsertUnits(QuotationDbContext db, List<TallyUnit> units, DateTime syncedAt)
    {
        var existing = db.Units.ToDictionary(u => u.TallyGuid);
        var live = new HashSet<string>();
        foreach (var u in units)
        {
            live.Add(u.Guid);
            if (!existing.TryGetValue(u.Guid, out var unit))
            {
                unit = new Unit { TallyGuid = u.Guid };
                db.Units.Add(unit);
            }
            unit.Name = u.Name;
            unit.FormalName = u.FormalName;
            unit.DecimalPlaces = u.DecimalPlaces;
            unit.AlterId = u.AlterId;
            unit.IsDeleted = false;
            unit.LastSyncedUtc = syncedAt;
        }
        foreach (var u in existing.Values.Where(u => !live.Contains(u.TallyGuid))) u.IsDeleted = true;
    }

    private static Dictionary<string, TallyGstResolver.GroupGst> UpsertGroups(QuotationDbContext db, List<StockGroup> existingList,
        List<TallyStockGroup> groups, DateTime syncedAt)
    {
        var existing = existingList.ToDictionary(g => g.TallyGuid);
        var live = new HashSet<string>();
        var map = new Dictionary<string, TallyGstResolver.GroupGst>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            live.Add(g.Guid);
            map[g.Name] = new TallyGstResolver.GroupGst(g.Name, g.Parent, g.Gst, g.Hsn);
        }
        var today = DateOnly.FromDateTime(syncedAt);
        foreach (var g in groups)
        {
            if (!existing.TryGetValue(g.Guid, out var group))
            {
                group = new StockGroup { TallyGuid = g.Guid };
                db.StockGroups.Add(group);
            }
            var resolved = TallyGstResolver.Resolve([], [], g.Name, map, today);
            group.Name = g.Name;
            group.Parent = g.Parent;
            group.AlterId = g.AlterId;
            group.Hsn = resolved.Hsn;
            group.GstRate = resolved.Rate;
            group.TallyDataJson = JsonSerializer.Serialize(new GroupTallyData(g.Gst.ToList(), g.Hsn.ToList()), Json);
            group.IsDeleted = false;
            group.LastSyncedUtc = syncedAt;
        }
        foreach (var g in existing.Values.Where(g => !live.Contains(g.TallyGuid))) g.IsDeleted = true;
        return map;
    }

    private static void ApplyItem(Product p, TallyStockItem item, IReadOnlyDictionary<string, TallyGstResolver.GroupGst> groups,
        TallySettings tally, DateOnly today, DateTime syncedAt)
    {
        p.AlterId = item.AlterId;
        p.Name = item.Name;
        p.Aliases = string.Join("\n", item.Aliases);
        p.PartNumber = item.PartNumber.Length > 0 ? item.PartNumber
            : tally.DerivePartNumberFromName ? PartNumberHeuristics.FromName(item.Name) : "";
        p.StockGroup = item.Parent;
        p.Category = item.Category;
        p.Brand = Pick(tally.BrandField, item);
        p.Manufacturer = Pick(tally.ManufacturerField, item);
        p.Unit = item.BaseUnit;
        p.Description = string.Join("\n", new[] { item.Description, item.Notes }.Where(s => s.Length > 0));
        p.TallyDataJson = JsonSerializer.Serialize(
            new ProductTallyData(item.Gst.ToList(), item.Hsn.ToList(), item.StandardPrices.ToList(), item.PriceLevels.ToList()), Json);
        ResolveProductGst(p, groups, today);

        var rate = TallyRateResolver.Resolve(item.StandardPrices, item.PriceLevels, tally.RateSource, tally.PriceLevel, today);
        p.Rate = rate.Rate;
        p.RateDate = rate.Date;
        p.RateSource = rate.Source;
        p.IsDeleted = false;
        p.LastSyncedUtc = syncedAt;
    }

    private static void ResolveProductGst(Product p, IReadOnlyDictionary<string, TallyGstResolver.GroupGst> groups, DateOnly today)
    {
        var data = JsonSerializer.Deserialize<ProductTallyData>(p.TallyDataJson, Json);
        if (data is null) return;
        var gst = TallyGstResolver.Resolve(data.Gst, data.Hsn, p.StockGroup, groups, today);
        p.Hsn = gst.Hsn;
        p.GstRate = gst.Rate;
        p.GstSource = gst.Source;
        p.GstInherited = gst.Inherited;
    }

    private static string Pick(string field, TallyStockItem item) => field switch
    {
        "Category" => item.Category,
        "StockGroup" => item.Parent,
        _ => "",
    };

    private static void ApplyLedger(Customer c, TallyLedger l, DateTime syncedAt)
    {
        c.AlterId = l.AlterId;
        c.Name = l.Name;
        c.Aliases = string.Join("\n", l.Aliases);
        c.MailingName = l.MailingName.Length > 0 ? l.MailingName : l.Name;
        c.LedgerGroup = l.Parent;
        c.Address = string.Join("\n", l.Address);
        c.StateName = l.State;
        c.StateCode = IndianStates.ResolveCode(l.Gstin, l.State);
        c.Pincode = l.Pincode;
        c.Country = l.Country;
        c.Gstin = l.Gstin;
        c.GstRegistrationType = l.RegistrationType;
        c.Pan = l.Pan.Length > 0 ? l.Pan : Gstin.Pan(l.Gstin);
        c.ContactPerson = l.Contact;
        c.Phone = l.Phone;
        c.Mobile = l.Mobile;
        c.Email = l.Email;
        c.ShipToJson = l.ShipToAddresses.Count == 0 ? "" : JsonSerializer.Serialize(l.ShipToAddresses, Json);
        c.IsDeleted = false;
        c.LastSyncedUtc = syncedAt;
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source, CancellationToken ct)
    {
        var list = new List<T>();
        await foreach (var x in source.WithCancellation(ct)) list.Add(x);
        return list;
    }

    public sealed class StateString
    {
        public string Value { get; set; } = "";
    }
}
