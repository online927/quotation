# Delivery phases

Each phase ends with: build → tests → fixes → summary → how to test.

| # | Phase | Status |
|---|---|---|
| 1 | Architecture and repository setup | ✅ Done |
| 2 | Tally connectivity | ✅ Done |
| 3 | Customer/product synchronization | ✅ Done |
| 4 | Local product/customer search | ⏳ |
| 5 | Quotation UI | ⏳ |
| 6 | Quotation calculation engine | ⏳ |
| 7 | Quotation numbering | ⏳ |
| 8 | PDF engine (Tally quotation layout) | ⏳ |
| 9 | Claude AI integration | ⏳ |
| 10 | Gmail integration | ⏳ |
| 11 | AI Inbox | ⏳ |
| 12 | Quotation history / audit | ⏳ |
| 13 | Multi-client / concurrency testing | ⏳ |
| 14 | Windows packaging / installer | ⏳ |
| 15 | End-to-end testing | ⏳ |

---

## Phase 1 — Architecture and repository setup

**Delivered**

* `docs/ARCHITECTURE.md` — final architecture and technology decisions.
* .NET 10 solution with central package management and CI (Linux tests + Windows build/publish).
* **Quotation.Core**: financial year model (Indian Apr–Mar, configurable), quotation-number
  pattern engine (`TSQ{FY}-{SEQ}` → `TSQ2526-3247`), company and quotation settings model.
* **Quotation.Data**: complete database schema (products, customers, groups, units,
  quotations + lines with buyer/consignee snapshots, number series, e-mail processing,
  sync runs, users, sessions, settings, audit log) with EF Core migrations; SQLite in WAL mode.
* **Quotation.Server**: ASP.NET Core service (runs as a Windows Service), login with
  PBKDF2-hashed passwords and revocable bearer sessions, admin/user roles, forced password
  change for the bootstrap admin, settings API with write-only encrypted secrets (DPAPI on
  Windows), audit log, dashboard/status API, log files with credential redaction.
* **Quotation.Desktop**: login screen, main shell with keyboard navigation
  (Ctrl+N / Ctrl+I / Ctrl+H / Ctrl+D), status bar (Tally / FY / last sync / server),
  dashboard, full Settings screen (company, quotation, Tally, Claude, Gmail, users).
* Tests: unit (FY, numbering), server integration (auth, roles, settings, secrets, redaction),
  headless UI (login → password change → dashboard; every page renders).

**How to test**

```bash
dotnet test
dotnet run --project src/Quotation.Server          # http://localhost:5080
dotnet run --project src/Quotation.Desktop
```
Log in as `admin` / `admin`, set a new password, open **Settings**, enter company details,
change the number pattern and press **Preview** to see the example number, **Save**.

---

## Phase 2 — Tally connectivity

**Delivered**

* **Quotation.Tally**: XML-over-HTTP client for TallyPrime.
  * Export-only request builder with inline TDL collections (filters, computed fields);
    the client refuses to send anything that is not an `Export` request.
  * Streaming XML parsing (20k items never loaded as one document), tolerant of Tally's
    invalid `&#4;` character references, UTF-8/UTF-16 responses, and `LINEERROR`/`RESPONSE` errors.
  * Clear errors for: Tally not running / port closed, timeout, no company open,
    configured company not open (lists the open companies).
  * Company discovery and **active financial year detection** from Tally's current period
    (`##SVFromDate`), falling back to Tally's current date.
* **Tally simulator** (`tools/Tally.Simulator`): behaves like TallyPrime's XML port with a
  realistic dataset (20,000 stock items, 3,000 customers, TallyPrime 3+ and legacy GST/HSN/address
  layouts, group-inherited GST, price levels, ambiguous cable variants, Sonepar, Universal Bevel
  Protractor). Used by tests and for demos without Tally.
* **Server**: connection monitor (every 60 s), `POST /api/tally/test`, `GET /api/tally/companies`;
  active FY priority: admin override → Tally → last known Tally value → system date.
* **Desktop**: Tally Connection page (status, company, active FY, last check, last sync,
  counts, test button Ctrl+T, troubleshooting steps).

**How to test**

```bash
dotnet test
# Terminal 1 — simulated Tally on port 9000
dotnet run --project tools/Tally.Simulator
# Terminal 2 — server;  Terminal 3 — desktop app
dotnet run --project src/Quotation.Server
dotnet run --project src/Quotation.Desktop
```
Open **Tally Connection** → **Test connection**. Stop the simulator and test again to see the
disconnected state. With real Tally: set **Settings → Tally → address** to the PC running
TallyPrime (e.g. `http://192.168.1.10:9000`).

---

## Phase 3 — Customer/product synchronization

**Delivered**

* **Master readers** for stock items, stock groups, units and customer ledgers, supporting both
  the TallyPrime 3+ layout (dated HSN/GST, mailing and GST-registration details, multiple
  addresses) and the older flat layout.
* **GST/HSN resolution** exactly as Tally applies it: latest *applicable-from* entry on the date,
  inheritance from the stock group chain ("As per Company/Stock Group"), CGST+SGST or IGST heads.
* **Rate resolution**: Standard Selling Price (default) or a named Price Level, date-effective.
  Items without a price in Tally get *no* rate (the user must enter one) — nothing is invented.
* Customers: only ledgers under the configured groups (default *Sundry Debtors* and all sub-groups);
  state code from GSTIN (fallback: state name); PAN from Tally or GSTIN; ship-to addresses kept
  for the consignee choice.
* Part numbers from Tally, or derived from names like `187-901-10-UNIVERSAL BEVEL PROTRACTOR`.
* **Full and incremental sync**: incremental uses Tally's AlterID (skips entirely when nothing
  changed), detects deletions via a GUID-only listing, re-resolves inherited GST when a group
  changes, and switches to full automatically when mapping settings change.
* One transaction per sync — an interrupted/failed sync never leaves partial data; runs left
  "Running" by a crash are marked failed at start-up; only one sync at a time.
* Scheduler: full sync when empty, incremental every 15 min (configurable), nightly full sync.
* Raw Tally GST/price data stored per product so rates/GST can be re-resolved for any date offline.
* Desktop: sync buttons (F9 = sync changes), progress, sync history, and an admin **diagnostics**
  view that shows the raw Tally XML for any one master (to verify field mapping on the real Tally).
* Performance: full sync of 20,000 products + 3,000 customers from the simulator ≈ 15–20 s;
  incremental sync of one change < 2 s.

**How to test**

Run the simulator, server and desktop app (see Phase 2), open **Tally Connection**, press
**Full sync**, then check the product/customer counts. Change something in the simulator-backed
data (tests do this automatically: `dotnet test --filter Sync`).
With real Tally: after the first sync, use **Diagnostics** with an exact item name to confirm
HSN, GST and rate fields are present. If they are empty, set **Settings → Tally → Fetch mode**
to `All` and sync again.
