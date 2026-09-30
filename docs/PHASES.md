# Delivery phases

Each phase ends with: build → tests → fixes → summary → how to test.

| # | Phase | Status |
|---|---|---|
| 1 | Architecture and repository setup | ✅ Done |
| 2 | Tally connectivity | ✅ Done |
| 3 | Customer/product synchronization | ✅ Done |
| 4 | Local product/customer search | ✅ Done |
| 5 | Quotation UI | ⏳ |
| 6 | Quotation calculation engine | ✅ Done |
| 7 | Quotation numbering | ✅ Done |
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

---

## Phase 4 — Local product/customer search

**Delivered**

* **Search engine** (`Quotation.Core/Search`): in-memory inverted index rebuilt after every sync.
  * Exact, prefix (binary search), contains (trigram) and typo-tolerant matching
    (Damerau–Levenshtein: 1 edit up to 5 letters, 2 edits for longer words; also while typing).
  * Unit/format normalization: `10 mm` = `10mm`, `2.5 sq.mm` = `2.5sqmm`, metres/meters/mtr, `"` = inch.
  * Part numbers with or without separators: `187-901-10` = `18790110` = `187 901 10`.
  * **Numbers are never fuzzy-matched**: `2.5sqmm` never matches `1.5sqmm`; `10mm` never matches `10.5mm`/`110mm`.
  * Field-weighted ranking (name, alias, part no. > brand > group/description), exact/starts-with
    bonuses, all-terms-first, and *match evidence* (exact name/identifier, fuzzy used) for the AI safety rules.
  * Natural-language filler words ignored ("please quote for the universal bevel protractor").
  * 20,000 products: index build ≈ 0.6 s, **average query ≈ 5 ms**.
* Customers searchable by name, alias, mailing name, **GSTIN**, phone/mobile, e-mail, contact, city.
* Server: `/api/products/search`, `/api/products/{id}`, `/api/customers/search`, `/api/customers/{id}`;
  searches keep working while Tally is offline (served from the synchronized copy).
* Desktop: **Products** and **Customers** pages with search-as-you-type (120 ms debounce),
  ↓ to move into results, detail panel (HSN, GST + source, rate + source/date, aliases,
  billing and ship-to addresses, GSTIN checksum warning).

**How to test**

`dotnet test --filter Search`. In the app (after a sync) open **Products** and type
`universal bevel`, `10mm`, `3 core 2.5 sq mm cable`, `18790110`, `univresal`;
open **Customers** and type `SONEPAR` or a GSTIN.

---

## Phase 6 — Quotation calculation engine

**Delivered** (`Quotation.Core/Calculation`, `Quotation.Core/Validation`) — deterministic code, never AI:

* Line amount = Qty × Rate − Discount %, rounded to paise (half away from zero, like Tally).
* Subtotal, packing & forwarding (fixed amount or % of subtotal), total quantity (+ unit when all lines share one).
* GST presentation setting:
  * **RateOnly** (default, like the Tally quotation): GST % column only, totals exclusive of GST.
  * **ComputeTax**: CGST + SGST (same state) or IGST (other state), P&F apportioned across GST rates.
* Optional round-off to the rupee with a separate round-off amount.
* Indian number format (`1,23,45,678.90`) and amount in words with lakh/crore
  (`INR Eighteen Thousand Four Hundred Eight and Fifty paise Only`).
* Pre-approval validation: customer, products from Tally, quantity > 0, rate > 0, discount 0–100 %,
  GST rate must be a notified rate, HSN/SAC present and 4/6/8 digits, unit present, date inside the
  active financial year, P&F not negative — every problem is reported, nothing is silently fixed.

**How to test**: `dotnet test --filter "CalculationTests"`.

---

## Phase 7 — Quotation numbering (+ quotation service)

**Delivered**

* **Central numbering** on the Quotation Server: the number is allocated inside the same database
  transaction that saves the quotation, under a server-wide lock, with a UNIQUE index as final
  safeguard. Tested with 60 simultaneous creations from 5 simulated PCs → 60 distinct, gap-free numbers.
* One series per financial year (`NumberSeries`), pattern from Settings (`TSQ{FY}-{SEQ}` → `TSQ2526-3247`),
  **continue from the existing Tally series** (admin sets next number, e.g. 3248; cannot go below a used number),
  numbers never reused (cancelled quotations keep theirs), provisional next-number preview.
* Financial-year rules: quotation date must be inside the active FY (from Tally); on FY change a new
  series starts at the configured first number.
* **Quotation service** (`/api/quotations`): create, update (optimistic concurrency — edits from two
  PCs are detected), approve, cancel, duplicate, list/search, validation, PDF download/regeneration.
  * Tally is authoritative: item name, HSN, GST, unit, buyer GSTIN/state are re-applied from Tally data on
    every save; the user may override the rate (the Tally rate is kept for reference), discount, quantity,
    description, addresses and reference fields.
  * **Approval**: quick incremental sync → re-apply Tally values → if a Tally rate changed the quotation is
    updated and returned for review → full validation → data-freshness check (stale data needs an explicit,
    audited override, which can be disabled) → number → save → PDF. An approved quotation is saved before
    the PDF is rendered, so a PDF failure never loses the approval (it can be regenerated).
  * Place of supply (consignee state) decides CGST+SGST vs IGST when tax computation is enabled.
  * Every action is written to the audit log.

**How to test**: `dotnet test --filter "NumberingTests|QuotationLifecycleTests"`.
Admins can view/set the next number via `GET/PUT /api/numbering` (desktop screen in Phase 5).
