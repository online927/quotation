# Delivery phases

Each phase ends with: build → tests → fixes → summary → how to test.

| # | Phase | Status |
|---|---|---|
| 1 | Architecture and repository setup | ✅ Done |
| 2 | Tally connectivity | ✅ Done |
| 3 | Customer/product synchronization | ✅ Done |
| 4 | Local product/customer search | ✅ Done |
| 5 | Quotation UI | ✅ Done |
| 6 | Quotation calculation engine | ✅ Done |
| 7 | Quotation numbering | ✅ Done |
| 8 | PDF engine (Tally quotation layout) | ✅ Done (to be fine-tuned against your reference PDF) |
| 9 | Claude AI integration | ✅ Done |
| 10 | Gmail integration | ✅ Done |
| 11 | AI Inbox | ✅ Done |
| 12 | Quotation history / audit | ✅ Done |
| 13 | Multi-client / concurrency testing | ✅ Done |
| 14 | Windows packaging / installer | ✅ Done (installers built on Windows/CI) |
| 15 | End-to-end testing | ✅ Done |

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

---

## Phase 5 — Quotation UI

**Delivered** (desktop app)

* **New Quotation** screen laid out like the Tally voucher: Buyer (Bill to), Consignee (Ship to),
  references (Buyer's Ref./Order No. + date, Dispatched through, Destination, Payment terms,
  Other references, Terms of delivery), item table (Sl, Description of Goods + extra description
  lines, HSN/SAC, GST, Due on, Quantity, Rate, per, Disc. %, Amount), remarks, terms & conditions,
  prepared/verified by, totals with P&F (amount or %), taxes (when enabled), round-off,
  total quantity, grand total and amount in words.
* **Customer autocomplete** (name, GSTIN, phone) fills buyer name, address, GSTIN, state + code,
  contact, phone, e-mail from Tally; **Ship-to** offers "Same as buyer", the customer's Tally
  addresses, or a typed consignee.
* **Product autocomplete** with HSN/GST/unit/rate preview; Tally rate pre-filled and editable
  (overrides are highlighted with the original Tally rate).
* **Keyboard flow**: Enter moves to the next field; Product → Enter → Qty → Enter → Rate → Enter →
  Disc % → Enter adds the line and returns to Product; Esc cancels a line edit;
  Ctrl+S save, Ctrl+Enter approve & generate, Ctrl+P open PDF, Ctrl+D duplicate, Ctrl+N new.
* Live totals using the same calculation engine as the server; provisional next number shown.
* Approval workflow in the UI: validation messages listed, stale-data confirmation
  ("Approve anyway (logged)"), Tally-rate-changed review, PDF error with **Regenerate PDF**.
* **Draft Quotations / Today's Quotations / Quotation History** lists with search, Enter/double-click
  to open, one-click **Duplicate**.
* Settings → Quotation: admin can set the **next quotation number** to continue the Tally series.

**How to test**: run simulator + server + desktop (Phase 2). Press **Ctrl+N**, type `sonepar` and
Enter, type `universal bevel`, Enter, `2`, Enter, Enter, Enter → line added; **Ctrl+S**; then
**Ctrl+Enter**. Automated: `dotnet test tests/Quotation.Desktop.Tests`.

---

## Phase 8 — PDF engine (Tally quotation layout)

**Delivered** (`Quotation.Pdf`, PDFsharp — MIT licence)

* Coordinate-drawn A4 quotation in the TallyPrime print structure: centred **QUOTATION** title; boxed
  header with company (optional logo), **Consignee (Ship to)** and **Buyer (Bill to)** on the left and the
  reference grid on the right (Quotation No., Dated, Buyer's Ref./Order No., Dated, Dispatched through,
  Destination, Mode/Terms of Payment, Other References, Terms of Delivery); ruled item table
  (Sl No., Description of Goods with italic extra lines, HSN/SAC, GST Rate, Due on, Quantity, Rate, per,
  Disc. %, Amount) with column rules running to the Total row; subtotal, Packing & Forwarding, taxes and
  round-off rows; **Total** with total quantity and ₹ amount; **Amount Chargeable (in words)** and E. & O.E;
  terms & conditions; Company's PAN, MSME No., IEC, **Declaration**, quotation validity;
  **Company's Bank Details**; Prepared by / Verified by and "for COMPANY … Authorised Signatory" box;
  "This is a Computer Generated Document".
* All company values come from Settings. Fonts are bundled (Liberation Sans — metrically identical to
  Arial — plus DejaVu Sans for ₹), so every PC prints identically.
* Multi-page: header and column headings repeat, "continued to page number N", "Page x of y", rows never
  split (an item longer than a page continues as "(contd.)" without losing lines), totals/words/declaration/
  bank/signature always together on the last page. Long addresses and descriptions wrap within their boxes.
* Tests verify every field is present, no overlapping or clipped text, page-break rules, long
  addresses/descriptions, missing optional fields, and render time.
* Server: PDF generated on approval and stored under `%ProgramData%\TSQuotation\pdf\{FY}\{number}.pdf`
  (atomic write); **Preview** (F11) renders the current draft with a DRAFT watermark without saving;
  a failed render leaves the quotation Approved with the error shown and **Regenerate PDF** available.
* Samples: `docs/samples/sample-quotation-single.pdf`, `docs/samples/sample-quotation-multipage.pdf`.

**Pending your input**: the reference Tally quotation PDF was not in the repository. Please add it
(e.g. `docs/reference/tally-quotation.pdf`); column widths, font sizes and block order are centralised in
`QuotationPdfRenderer` so they can be matched precisely.

**How to test**: approve a quotation in the app (PDF opens automatically) or press **F11** on a draft;
`dotnet test tests/Quotation.Pdf.Tests` (set `PDF_OUTPUT_DIR` to keep the generated files).

---

## Phase 9 — Claude AI integration

**Delivered** (`Quotation.AI`, official Anthropic C# SDK)

* `IAIProvider` abstraction; `ClaudeProvider` implementation on the Messages API with a manual tool loop
  (the application executes every tool). Default model `claude-opus-5-5` (configurable in Settings) with
  adaptive thinking and explicit `medium` effort, strict tool schemas, prompt caching on the static
  system prompt + tools, append-only history (thinking blocks echoed unchanged), server-side refusal
  fallback (beta `server-side-fallback-2026-06-01`, fallback `claude-opus-4-8`), typed error handling
  (rate limit / 5xx / network / bad request → clear message; the request is kept as *Failed*).
* Tools offered to Claude (all read-only): `search_customer`, `get_customer_details`, `search_product`,
  `get_product_details`, `get_current_rate`, `get_quotation_history`, `ask_clarification`,
  `create_quotation_draft` (records a *proposal* only). Only top candidates (default 8) are ever sent —
  never the catalogue.
* **Safety rules enforced in code** (`MatchPolicy`): the application re-runs every search itself; a product
  or customer is accepted only with deterministic evidence (exact part no./name/GSTIN/e-mail, a single
  candidate matching all terms without typo correction, or a decisive ranking margin) *and* agreement with
  the AI's choice. Otherwise the user must select. Invented ids are rejected; missing quantity is asked,
  never assumed; HSN/GST/rate/unit/addresses always come from Tally; e-mail text is framed as untrusted data
  (prompt-injection resistant); the AI's confidence is informational only.
* AI drafts are `PENDING_REVIEW`, **un-numbered**, and cannot be approved by the AI; a person's save/approve
  assigns the number and generates the PDF.
* Server: `/api/ai/analyze`, `/api/ai/requests`, `/create-draft`, `/dismiss`; analyses stored in `AiRequests`
  with model and token usage; every step audited.
* Desktop **AI Inbox**: type a request (Ctrl+Enter), see customer/product matches, pick among candidates when
  ambiguous, set missing quantities, **Create Draft** → opens in the quotation editor for review.
* Tests: 20 orchestration/safety tests with a scripted model (no network), 6 server tests, 1 UI test.

**How to test**: Settings → Claude AI: enable, paste an API key, Save. AI Inbox → type
"Create quotation for Bharat Precision for 2 universal bevel protractors" → draft opens; try
"Please quote 10 pcs 3 core 2.5 sqmm cable for Bharat Precision" → you are asked to choose the cable.

---

## Phase 10 — Gmail integration

**Delivered** (`Quotation.Gmail`, Google.Apis.Gmail.v1)

* **OAuth 2.0 installed-app flow**: admin clicks *Connect Gmail*; the desktop opens Google's sign-in page and
  receives the one-time code on `http://127.0.0.1:{free port}/`; the server exchanges it for a refresh token
  (read-only scope `gmail.readonly`) and stores it encrypted (DPAPI). The password is never seen; the client
  secret never leaves the server; state values are single-use and expire in 10 minutes; tokens are redacted
  from logs.
* **Polling** of a configurable Gmail search (default `label:Quotations newer_than:14d`) every few minutes, or
  *Check now*. Each message is recorded with its **Gmail message id (unique key)** before processing:
  `UNPROCESSED → PROCESSING → DRAFT_CREATED / COMPLETED (needs review) / IGNORED (not a quotation) / FAILED`.
  Restarts resume stale PROCESSING rows and reuse an existing analysis — never a second draft. Failures are
  retried up to 3 times. If the AI is not configured, e-mails wait and are processed once it is.
* Plain-text body (or HTML converted to text), sender, subject, received date; the sender's e-mail address is
  matched against Tally customer e-mails to identify the customer.
* Desktop **Gmail Connection** page: status, account, query, last check/error, per-status counts, message list.

## Phase 11 — AI Inbox

The AI Inbox (built in Phase 9) now also lists Gmail requests (subject, sender, received time, e-mail text):
*Ready for review* items already have a draft (**Open Draft**); *Needs clarification* items show the reason
(e.g. "3 possible products found") with **select customer / select product / quantity** and **Create Draft**;
*Failed* items show the error; items can be dismissed. Dashboard shows "AI Inbox needs attention".

**How to test**: Google Cloud Console → create an OAuth client of type *Desktop app* → download the JSON →
Settings → Gmail: paste JSON, enable, set the query → Save → Gmail Connection → **Connect Gmail**.
Send yourself a test e-mail with the label, press **Check now**, open the AI Inbox.
Automated: `dotnet test --filter "GmailTests|GmailPageTests"`.

---

## Phase 12 — Quotation history / audit / diagnostics

**Delivered**

* **Global search** in the header (Ctrl+F, Enter): quotation number, customer, GSTIN, product, reference or a
  date (`30-09-2026`, `30/09/26`, `30-Sep-2026`); several words narrow the result.
* History record per quotation: id, number, FY, date, customer + GSTIN snapshot, products, quantities, rates,
  discounts, GST, P&F, totals, PDF location, created by/at, approved by/at, source (MANUAL/AI/GMAIL), status.
* **Recent customers and recent products** (per user) as one-click picks on the quotation screen.
* **Audit trail** per quotation in the editor (created, updated, approved with/without stale data, rates
  refreshed from Tally, PDF generated/failed/downloaded, cancelled, duplicated, AI draft created …).
* Admin **Diagnostics** screen: server version/runtime/OS/machine, data directory, database size, counts,
  Tally/Gmail/AI state, log files, full audit log with filter, and the server log tail (credentials redacted
  again at read time).

**How to test**: create a few quotations, press Ctrl+F and search by number, customer, product or today's date;
open a quotation → *History / audit trail* → *Load history*; Diagnostics (admin).

---

## Phase 13 — Multi-client / concurrency

**Verified by automated tests**

* 6 client PCs concurrently searching, creating, approving (with PDF) and listing quotations **while a full
  20,000-product / 3,000-customer sync writes to the database**: 0 errors, 90 quotations, numbers unique and
  gap-free, sync succeeded (≈6 s total).
* 60 simultaneous creations from 5 PCs (Phase 7), edit conflicts between two PCs detected (optimistic
  concurrency), concurrent sync requests rejected, overlapping Gmail polls cannot double-process a message,
  the unique index rejects a duplicate number even if the application logic were bypassed.

**Hardening**

* SQLite: WAL journal, private cache (no shared-cache table locks), 30 s busy timeout; all writes go through
  the single server process.
* **Automatic daily online backups** (SQLite backup API, consistent while running) in
  `%ProgramData%\TSQuotation\backups`, kept 30 days; on-demand backup for admins (`POST /api/diagnostics/backup`).

**How to test**: `dotnet test --filter "ConcurrencyStressTests|NumberingTests"`.

---

## Phase 14 — Windows packaging / installer

**Delivered** (`installer/`)

* `publish.ps1` (Windows) / `publish.sh` (cross-publish): runs the tests, then publishes **self-contained,
  single-file, compressed win-x64** executables — no .NET installation needed on any PC:
  `TSQuotation.Server.exe`, `TSQuotation.exe` (desktop), `TallySimulator.exe` (demo/testing).
* **TSQuotationServer-Setup.exe** (Inno Setup): installs to Program Files, registers the **Windows Service**
  `TSQuotationServer` (delayed auto-start, automatic restart on failure), opens TCP 5080 for domain/private
  networks, creates `%ProgramData%\TSQuotation` (database, PDFs, logs, backups — never deleted on uninstall),
  keeps the existing `appsettings.json` on upgrade, stops/starts the service around upgrades.
* **TSQuotation-Setup.exe**: desktop app with Start-menu/desktop shortcuts; asks for the server address
  (or `/VERYSILENT /SERVER=http://server-pc:5080` for silent roll-out), stored as the default for new users.
* CI (GitHub Actions, Windows runner) installs Inno Setup and uploads both installers as build artifacts.

**How to test**: on a Windows PC with the .NET 10 SDK and Inno Setup 6: `.\installer\publish.ps1` →
`publish\installers\`. Or download the `windows-installers` artifact from the CI run.

---

## Phase 15 — End-to-end testing and documentation

**Delivered**

* **Real-process end-to-end test** (`tests/Quotation.E2E.Tests`): starts the Tally simulator and the Quotation
  Server as separate processes (real HTTP, background workers enabled, file logging) and drives them like two
  desktop PCs: first-run password change → company & Tally settings (incl. an API key) → Tally test → full sync →
  search → quotation from a second PC → approval with PDF (content verified) → **hard kill + restart** (data
  intact, numbering continues, duplicate uses the current Tally rate) → **Tally outage** (search still works,
  approval requires the logged override) → database, PDFs and logs on disk → **no API key or password in any log**.
* `docs/SETUP.md`: installation, Tally configuration, connecting to TallyPrime Server, initial sync, company
  details, Claude API, Gmail OAuth, first quotation, backups/upgrades, troubleshooting.
* WhatsApp: `IMessageChannel` extension point and message template in `Quotation.Core/Messaging`,
  deliberately **not** implemented or registered in V1.

### Test inventory (349 automated tests)

| Suite | Tests | Covers |
|---|---|---|
| Core | 102 | financial year, numbering patterns, GST/GSTIN/state codes, search & fuzzy matching (20k perf), calculations, amount in words, validation |
| Tally | 54 | XML client, load guard (one request at a time, slow/timeout → background holds back), last rates from sales/quotation vouchers (per type via Tally's index, weekly), read-only guard (blocked write requests never reach Tally), errors/offline/timeouts, company & FY detection, master parsing (both layouts), GST inheritance, rates, incremental filter |
| PDF | 18 | Tally print layout (Printed on/By, page titles, header grid, tax lines directly under the items, seal, proforma HSN summary), all fields, no overlaps/clipping, multi-page, unsplit rows, long descriptions/addresses, watermark, speed |
| AI | 20 | orchestration and safety rules with a scripted model: ambiguity, wrong/invented ids, missing quantity, customers, e-mail framing, strict schemas |
| Server | 121 | GST by default and per document, Without GST, users' editing period, delete/restore with number reuse and 30-day removal, user management and admin recovery, DNU items, seal PIN and blocking, proforma invoices (own series, convert, list filter, PDF), auth/roles/settings/secrets, Tally API, concurrent status checks, re-read after mapping upgrade, last rates (Tally vouchers, app approvals, switch-off, slow Tally, stop, night-only), sync (full/incremental/deleted/failed/interrupted/20k), search API, numbering & concurrency, lifecycle, PDF failure recovery, AI API, Gmail ingestion & duplicates, history/audit/diagnostics, backups, 6-PC stress test |
| Desktop (headless UI) | 33 | Save as PDF (approve + Number Customer.pdf, seal PIN), Without GST from My Settings, delete/restore, Title Case, login, navigation with Back/Forward, breadcrumbs and the unsaved-changes question (keyboard and mouse), proforma invoices, Tally page, catalogue pages, quotation entry incl. keyboard flow, Tally-style product/customer lists (↑/↓, Enter, mouse, fast typing, Esc), stale override, lists, AI Inbox, Gmail page, OAuth loopback, search/recent/diagnostics, XAML lint |
| End-to-end | 1 | real processes, restart, outage, log hygiene |

## After first use — changes requested by T.SAIFUDDIN & CO.

* **Modern look** (in the spirit of apple.com): light page, white rounded cards, one blue accent, pill buttons,
  grouped sidebar, quieter tables and status bar.
* **Line entry order**: Product → Qty → Rate → Disc % → Due on → Description → Enter adds the line
  (Shift+Enter = new line inside the description).
* **PDF like the Tally prints**: *Printed on … By: <logged-in user>*, `QUOTATION(Page 2)` / `continued ...`,
  Tally's header grid, SGST/CGST/IGST and round-off lines.
* **Proforma invoice mode**: own TSPI series, convert a quotation, Tally proforma layout with HSN/SAC summary.
* **Back navigation**: ‹ › buttons, Alt+←/→, mouse side buttons, Esc, breadcrumbs, pages kept as left,
  Save / Don't Save / Cancel before leaving unsaved work.
* **Tally load guard**: one request at a time with pauses, response time measured, background reading pauses
  while Tally is busy.

**How to test**: `dotnet test --filter "ProformaTests|NavigationTests|TallyLoadGuardTests|TallyPrintLayoutTests"`.

### Second round of changes

1. GST lines (SGST + CGST or IGST) and round off by default; each document keeps its own GST presentation.
2. Title Case while typing in the quotation's text boxes (setting).
3. User management: edit login/display name, role, active; reset password (logs out); change my password; server-PC
   recovery command for a forgotten administrator password.
4. Shift+Enter hint on the description box.
5. Items whose Tally description is DNU are not offered (Products → Show DNU items).
6. Save as PDF: approve if needed and save *Number Customer.pdf* into the PC's folder.
7. Administrators delete to a 30-day Deleted list with restore; the latest number is reused.
8. Users can edit/cancel for 7 days after first saving; administrators always.
9. Packing & forwarding, taxes and round off directly under the items, like Tally.
10. Without GST for one quotation (My Settings; 0 % items, "Packing & Forwarding - Without GST", no tax lines).
11. Customer → Buyer's Ref./Order No. → Enter → items.
12. Company seal on the PDF with a 4-digit PIN (blocked after 5 wrong tries).

**How to test**: `dotnet test --filter "GstAndEditingRulesTests|UsersDnuSealTests|OfficeFeaturesUiTests|TallyPrintLayoutTests"`.

### Round 3 (office feedback)

1. Customer → **Other references** → Enter → items (Buyer's Ref. and the other boxes with Tab or the mouse).
2. Editor buttons in groups: row 1 Save Draft, Approve & Generate PDF, Save as PDF + Seal, with Cancel and Delete
   apart on the right; row 2 Preview, Open PDF, Regenerate PDF | New, Duplicate, Convert to Proforma. Shortcut keys in grey.
3. Party details on the PDF as Tally's "Display name for Parties: Name Only": company name, address, GSTIN, state,
   **Phone** and E-Mail — the contact person's name is not printed (it stays on the screen).
4. Settings → Party Details (administrators), like Tally's print options: contact details, contact person, name and
   address from the Tally ledger, address in one line, display name (name/alias), GSTIN, PAN, state, consignee and
   PIN code, with Preview PDF. Party blocks print as Tally: aligned labels, "Contact" = phone, PIN joined to the
   address ("Bangalore - 562111"), long lines shrunk to one line, consignee with contact details too.
5. Only administrators change the quotation date; for users it is the day of first saving (server-enforced).
6. Settings → Quotation: own headings for the quotation and proforma invoice PDFs (empty = standard).
7. Settings → Party Details → Address width: half the box (the address wraps in the left half).

### Round 4

1. Show Consignee details: Yes / No / Only when different / Buyer/Consignee when same (default; one block).
2. Fit on one page (admin, on): a document just over one page prints up to 10 % smaller on one page.
3. Dark mode per user (top-bar button, My Settings → Appearance: Light / Dark / Same as Windows; saved with the login).
4. Remarks in bold on the PDF.
5. Recent customers and recent products removed from the quotation screen.
6. Proforma invoices saved in their own folder (My Settings; default "Proforma Invoices" in the quotation folder).
7. Clearer highlight of the chosen row in search lists and tables (light blue with a blue bar).
8. Due on removed from the item entry, the item list and the PDF.

### Round 5

1. Wider Part No. column (codes such as 345-250-30 in full), narrowest Quantity column; quantities and amounts that do not
   fit print slightly smaller instead of being cut.
2. Part No. column on quotations too; admin switches for quotations and proforma invoices (both on).
3. Modern (Z) layout from the T.SAIFUDDIN & CO template package (docs/templates/modern-z), with Disc % and HSN/SAC columns,
   wrapped names, further pages for many items, and the seal; the small Z button saves it as "Number Customer Z.pdf".

### Round 6

1. The Z button is replaced by a PDF layout per user (My Settings → PDF layout: Tally / Zoho, saved with the login);
   Settings → Quotation → Default PDF layout (Tally) for users who have not chosen. Every PDF the user makes follows it.

### Round 7

1. Buyer/consignee address printed in at most 3 lines (both layouts): long addresses run on with commas and shrink a
   little when needed, never cut.
2. Speed check in the status bar (⚡ ms) and Diagnostics → Slow requests (client and server time, server busy with,
   likely cause).
3. Sync every 3 hours (180 minutes) instead of 15.
4. No last rates from Tally vouchers (switch in Settings → Tally, off); the app remembers its own approved rates.
5. Save Draft, New, Duplicate and Convert to Proforma removed from the quotation screen (Ctrl+S kept, Ctrl+D removed;
   Duplicate stays in the lists).
6. Approve & Generate PDF and Open PDF save "Number Customer.pdf" (the same file as Save as PDF).
7. Buyer, consignee and references fold into one line (customer, other ref.) with an eye button.

### Round 8

1. All quotation-screen buttons on one line (shortcuts in the tooltips); Regenerate PDF only shown when making the PDF
   failed at approval.

### Round 9

1. Packing & forwarding is an amount only: the P&F % box (quotation screen) and the default P&F % (Settings) were
   removed. An older quotation saved with a % opens showing the same amount, so its total does not change.
2. Shorter labels: *Customer*, *GSTIN*, *State*, *Product*, *Description (Shift + Enter for next line)*; the button
   is *Add line* (Enter in the tooltip).
3. Remarks, terms and prepared / verified by are folded into one line; the 👁 button opens them.
4. The move-up button on item lines and the amount in words on screen were removed (the PDF still prints the
   amount in words).

### Round 10

1. The packing & forwarding box starts empty (counts as 0.00; the amount on the right still shows 0.00).
2. Items are moved by dragging the grip (⋮⋮) at the left of a line; Sl. No. are renumbered.
3. The description box is a quarter of its former width; remarks, terms and prepared / verified by are half width.

### Round 11 (new look, from the office's design picture; screen only)

1. Section titles with icons (Items, Remarks), blue Totals bar, bigger bold titles; icons in the action buttons
   (colours unchanged), slightly taller action buttons, round bin icon on item lines, bolder status badge.
2. Totals: evenly spaced rows, the Total in a light-blue band that is the bottom edge of the box.
3. Colours and corners from the picture (light mode): cool page background, deeper navy sidebar, softer borders,
   28 px input boxes with 6 px corners, 10 px card corners.
4. Home icon and a short breadcrumb (home › current page); top search box says "Search quotations".
5. Sidebar keyboard shortcuts (Ctrl+N, Ctrl+Shift+N, Ctrl+I, Ctrl+H, Ctrl+D) switched off; larger sidebar icons.
6. No grey example text in the Customer and Product boxes; "Source" shown only for AI Inbox / Gmail quotations;
   the number beside the date only while provisional; Edit and remove appear on the line under the mouse;
   the scroll bar runs beside the cards.

### Round 12

1. The app always opens maximised.
2. "Ref. dated" is typed (no calendar, no grey hint): 05-11-2026, 5/11/26, 05-Nov-2026 … day first, shown as 05-Nov-2026.
3. No "Tally rate …" hint under an item's rate on screen.
4. Client PCs connect to the server by its IPv4 address first, give up on an address that does not answer after
   3 seconds (instead of Windows' 21 s) and keep the connection open between requests: the long "Network or PC name"
   waits in Diagnostics → Slow requests (e.g. 21 s, 42 s) are avoided.

### Round 14

1. "Ref. dated": small calendar (icon in the box) besides typing; white title bar on Windows 11 (dark in dark mode).
2. The server address moved from the status bar to My Settings → Connection (with the PC name); ⊘ on Cancel.
3. "Saved as TSQ2627-6. TSQ2627-5 was used by another user." when someone saved first.
4. Open PDF on a changed approved quotation asks to save and approve again first.
5. Unsaved work is kept on the PC every 30 s and offered back after a crash or power cut (no number used).
6. Page titles with icons on every screen, icons on Refresh/New buttons and dashboard tiles.
7. Messages appear for a few seconds in the bottom-right corner (errors stay on the page).
8. Several users: "Rasik (PC) also has this quotation open" (server keeps who has what open, in memory only);
   a save clash offers "Save mine as a new quotation" or "See their changes" with "Put my changes back";
   "Created / Changed by … · Approved by …" under the title.

### Round 15

1. Item descriptions are remembered per item (last non-empty one, any customer; Tally's description otherwise).
2. My Settings → Appearance: text size Normal / Large, spacing Comfortable / Compact, Reduce animations, and themes
   (Default Light, Default Dark, Same as Windows, Sunset, Lagoon, Graphite, Frost, Glass (Colourful / Blue / Black / Green /
   Maroon), Neon Night, Cyberpunk). Saved with the login.
3. Won / Lost / Follow-up (administrator switch, off by default): outcome row on approved quotations, "To follow up"
   dashboard tile and list, outcome column and filter in History; proforma conversion marks Won.
4. WhatsApp button (administrator switch, off by default, editable message): copies the PDF and opens the customer's chat.
5. Approval no longer asks Tally; "approve anyway?" only when the last sync is older than 24 hours.
6. Automatic client updates from the server (Update now / Later; "Clients must update" removes Later).
7. Backups every 3 hours with a second copy folder; dashboard health warnings for administrators;
   Diagnostics → Export everything (.tsq) and Restore from backup file (.tsq / .db).
8. Unsaved work opens again by itself with "Last quotation restored …".
9. "Line 2 deleted while editing line 1" fixed: line buttons never keep the keyboard, Edit goes to Quantity,
   a removed line has Undo.
10. No status bar: Connected / Disconnected dot in the top bar with details, a closable warning strip; version and
    financial year in My Settings.
11. Light animations (page fade, corner messages, warnings, buttons, menus); "Bro, are you trying to close?" and
    "Leaving already, bro?" questions.

### Round 16

1. Personal seals: each user (not administrators) has their own seal picture and 4-digit PIN, set by an administrator
   in Settings → Users → Edit (or given the former company seal picture). The Seal box shows only for users with a seal;
   administrators do not seal. The company seal and the "All users can seal" switch are removed; the seal size and place
   stay in Settings → Company → Seal on PDFs.
2. Drag PDF: an approved quotation's PDF can be dragged into WhatsApp, an e-mail or a folder.
3. WhatsApp follow-up button in the To Follow Up list, with its own editable message.
4. "Approve & Generate PDF" is now "Approve"; "Cancel Quotation" is "Cancel". The buttons stay on one line: on a narrow
   window Delete, Cancel, Open PDF, Preview and WhatsApp show only their icon.
5. No flash on hover: the sidebar and item highlights appear without a fade, sidebar options no longer shrink on click.
6. Glass and Frost themes: suggestion lists, the calendar, corner messages and questions are solid.
7. My Settings → PDF file name: number first (as before) or customer first.
8. Themes: Glass (Colourful), Cyberpunk and Lagoon removed (users on them get Default Light); Graphite has a graphite-grey
   background.

### Round 17

1. Pay by UPI QR code on quotations (not proforma invoices): Tally layout in the right half of Terms of Delivery, Zoho
   layout at the bottom right; only "Pay by UPI" and the UPI ID printed; the customer's UPI app gets the UPI ID, the
   quotation total and number. Settings → Company (administrators): UPI ID and an on/off switch (off by default).
2. Seals: "Save user" also saves the seal PIN (no separate Set PIN); the users list shows each user's seal state; the seal
   size and place moved to Settings → Users → Edit → Seal; "Use the former company seal picture" removed.
3. Settings → Users, Claude AI and Gmail scroll.
4. The Total band keeps inside the Totals box in every theme (rounder glass/frost corners).
5. Cancel on a quotation that was never saved also forgets its recovery copy (it is not restored again).

### Round 18

1. The administrator makes no quotations or proforma invoices: no New Quotation / New Proforma, Duplicate, Make proforma
   or AI Inbox "Create Draft". On a document the administrator has Preview, Open PDF, the history, Cancel and Delete
   (the server refuses the rest: "Administrators do not make quotations"). `Server:AdministratorsMakeDocuments = true`
   in appsettings.json allows it again for an office with nobody else.
2. One administrator, everyone else a Standard user: no Administrator tick box; new users are Standard; the server refuses
   a second administrator; on the first start any extra administrator becomes Standard ("admin" stays).
3. Settings → Party Details: "Show Contact (phone)" (with the contact person's name) and "Show E-mail" are separate ticks,
   both starting as the old "Contact details" tick.
4. An item without a rate shows an empty Rate box (not 0.00); Approve still asks for a rate.
5. Settings → Quotation → Proforma invoices switch, off: no New Proforma, Proforma list, filter or Make proforma, and the
   server refuses new ones. Proforma invoices already made stay in Quotation History.
6. New sign-in screen: greeting, company name (from Settings, remembered on each PC), server address under
   "Server settings" (opens by itself when the server cannot be reached).
7. Dashboard without the New Quotation / New Proforma buttons and shortcuts.
8. Themes: Glass (Maroon) with a maroon menu and white boxes; new More Maroon (dark, maroon boxes).
9. Quotation buttons in a floating bar: Approve and Save as PDF pills, icon tiles for Preview, Open PDF, WhatsApp, Cancel
   and Delete; the Seal is a switch; the drag-PDF handle is removed; "Unsaved changes" is a chip.
10. Striped item lines, friendly empty lists, a loading shimmer, error messages in a closable red banner on every page,
    Settings sections as a list on the left, and a side menu that folds to icons (remembered on each PC).

### Round 19

1. Themes: Parchment (aged paper, forest-green menu, gold) and Forest & Gold (dark green, gold), with drawn mountains
   and pines (original line art, no pictures shipped). More Maroon removed (its users get Glass (Maroon)); Glass (Maroon)
   boxes have a soft maroon blush.
2. My Settings → Background picture (this PC): any picture behind the pages, kept on that PC only.
3. Sign-in fills in the last Standard user's name on that PC, never the administrator's.
4. Shorter quotation button bar (icon and name on one line).
5. Glass and Frost themes: more room inside typing boxes, so the text cursor no longer pokes past the round corners.
6. Passwords: at least 4 characters (was 6).
7. App icon: a black-and-gold product box (window, taskbar, shortcuts, installers).
8. The Windows 11 title bar takes each theme's top-bar colours.

### Round 20

1. Quotation button bar: the buttons sit in the middle of their box (empty message lines no longer push them down) with
   10 px of room above and below.

### Round 21

1. A quotation or proforma invoice that has been approved (even if it was edited back to draft later) can be cancelled
   only by the administrator; users no longer see Cancel on it. The server refuses it too.
2. Wider Description box when adding an item (30 % of the line instead of 25 %).
3. Enter in Packing & Forwarding moves to Approve (or Save as PDF) without pressing it.
4. Descriptions are remembered only for items with a part number.
5. Open PDF removed for users (the administrator keeps it); My Settings → "After Save as PDF: Open the PDF" (on at
   first); Ctrl+P is Save as PDF for users.
6. Number boxes: the up/down buttons sit on the box (no grey block or square end; an unusable arrow just fades).
7. The app is called "Quotations" (window, shortcuts, installers, Apps & features, Windows service display name and
   firewall rule: "Quotations Server"); program and data folders keep their TSQuotation names.
8. Settings → Quotation → "Users and others' quotations" (off at first): users see everyone's quotations but change,
   approve, cancel, duplicate, print and seal only their own; a Gmail draft belongs to the first user who saves it.
9. Settings → Users redesigned: people list with search on the left, the chosen person with Account / Seal / Password
   tabs on the right, "+ Add" for a new user.

### Round 22

1. New theme Hero Light: navy menu, white boxes, bold red highlights, with drawn light rays behind the pages and a city
   skyline along the bottom (original line art, no pictures shipped).

### Round 23

1. Focus highlight on buttons (e.g. Approve after Enter in P&F): a soft glow in the theme's highlight colour instead of
   the old dotted outline.

### Round 24

1. Paste a list: copy a list of items from WhatsApp, e-mail or Excel and press Ctrl+V in the Product box. A "Pasted list"
   box shows each line with the item found (green), close matches to choose from (orange) or "Not found" with a Search
   button (red); quantities are read from "x 2", "2 nos", "qty 2", "- 2" or a number at the end (none = 1). "Add N lines"
   adds them with the Tally rates as typing would. Greetings, signatures and phone numbers are skipped. Items are looked up
   in the app's own item list on the server; Tally is not asked. A single line pastes into the box as before.
2. Jump by number: typing a quotation number in the top search box ("142" or "TSQ2627-142") lists the quotation(s) with
   that number below the box (this year's first, quotations before proformas); Enter opens the highlighted one, ↑ / ↓
   pick another, Esc closes the list. Anything else (or a number no quotation has) searches Quotation History as before.
3. Part numbers without dashes or with spaces ("18790110", "187 901 10") already find "187-901-10" (no change needed).

### Round 25

1. Pay by UPI: Settings → Company → "Payment note": Quotation number / Order number / Both (Both at first, e.g.
   "TSQ2627-142 PO/4471"; the order number is the Buyer's Ref./Order No.; at most 50 characters). "Test QR (₹1)" shows a
   test QR from what is typed (before Save) to check the name and UPI ID with a phone.
2. Zoho layout foot: no sign-off ("Regards, name, company"; its Settings box is removed); Company's PAN / MSME No / IEC at
   the left, bank details in the middle (slightly smaller text when long), the Pay by UPI QR 10 % smaller at the bottom
   right with its UPI ID level with the last bank line; the seal at the bottom middle.
3. Zoho layout: "Order No: PO/4471 dt. 28 Oct 2026" under Ref No.; a GST % column; "E. & O.E" under the total; "This is a
   Computer Generated Document" at the foot; "Page X of Y" when there is more than one page.
4. Before Approve, a GST check: a GSTIN from another state than the party's, a GSTIN of the wrong shape, or a party with no
   state asks "Approve anyway" / "Go back" and says which GST the quotation gets (IGST or CGST + SGST).

### Round 26

1. Formater page (menu: under New Quotation, for users who make quotations): the customer and items are pasted or typed
   as text, the way of the separate Formater app, without labels: the first line is the customer, the second line the
   other reference (the next lines may be its address, GSTIN, phone, e-mail), then the items "name or part no., discount, qty, rate[, unit][, description]";
   indented lines are an item's description. (Labelled lines such as Company:, Order No:, Other Ref:, Date:, GST: still
   work.) "What the app understood" shows the customer, items, GST and totals as you type.
   - The customer and items are matched to the Tally lists when it is clear (read only; Tally is never changed); otherwise
     they are printed as typed and kept only in this app, with "Tally has: … — Use" offered; "Keep as typed" undoes a match.
   - Approve (Ctrl+E) puts it on the quotation screen and approves it there like any quotation: the next quotation number,
     Quotation History, the GST check, seal and PDF folder; Formater quotations always print in the Zoho layout.
   - Preview (F11) makes the PDF without saving or using a number; "Open in editor" opens it on the quotation screen.
   - Terms and T&C are not typed here: they come from Settings → Company as on any new quotation.
   - Same look as the quotation page in every theme (its styles are shared: Styles/DocumentStyles.axaml).

### Round 27

1. Formater: blank lines now decide the parts of the text. Part 1 is the customer (its first line is the name; the next
   lines are the address, GSTIN, phone, e-mail); part 2 is the other reference; part 3 is the items. A part whose lines
   read as items is items at once (so the other reference may be left out, and items typed without a blank line are
   still items, never address lines).
2. Formater item lines are "item name, qty, rate, discount": the discount is optional and is a plain number ("%" may be
   typed). Also read: "name, qty" (the Tally rate is used), "530-104 5 3250 10%" with spaces, rates with commas
   ("12,011"), and the older order "name, 10%, qty, rate".
3. A blank line before each item: the item's first line is "name, qty, rate, discount" and the lines under it (no
   indenting needed) are its description. A line under an item that is clearly an item itself (a pasted list) is still
   read as a new item.
4. In the customer part, address lines may start with "-" (or "*"): each is one address line and the "-" is not printed.
   After the first blank line a "-" no longer means address.
5. Formater page:
   - Line numbers at the left of the text; a line with a problem is marked red (red number, bar and band), and each problem
     has "Go to line N", which selects that line.
   - P&F has its own box under the text (an amount in rupees; Alt+P goes there); it is in the totals, the PDF and the
     quotation.
   - A pasted WhatsApp / e-mail text is cleaned: "[11/11, 10:15 am] Ravi:" times and names and lines that are only a greeting
     ("Hi sir", "Good morning", "Thanks", "Regards") are taken out; a note says what, with Undo.
   - "Use" on a Tally customer / item is remembered on the server (new table FormaterMatches, this app's database only;
     Tally is never changed): the same typing (any case and spacing) is matched to it next time on every PC, with a purple
     "Remembered" tag and "Forget".
6. Quotation screen: Cancel on a quotation not yet saved stays on the page (it starts again blank; with nothing typed it
   does nothing). Enter on a highlighted button presses it (Enter in P&F highlights Approve, Enter again approves).
   Alt+P puts the cursor in Packing & Forwarding.
7. Ctrl+P (quotation screen and Formater) prints straight to the Windows default printer, with no print window and no PDF
   viewer: an approved, unchanged quotation prints its PDF; a draft prints the DRAFT preview without saving. PDFium
   (Apache-2.0, bblanchon.PDFium.Win32) draws the pages on the printer.
8. The theme button in the top bar goes through all themes in order (Same as Windows skipped); a message names the theme.
   New themes with drawn scenery: Ocean (sun, gulls, waves) and Midnight Stars (dark: moon, stars, hills and pines).
9. Greetings (Settings, administrator switch, off at first, for everyone): "السلام عليكم / Assalamu Alaikum" after signing
   in, "Alhamdulillah الحمد لله" when a quotation is approved, "جزاك الله خيرا / JazakAllah Khair" for a second after Yes
   on closing the app.
10. Ctrl+Alt+Shift+X opens a hidden XO game (you against the computer, score, New game); Esc closes it.
11. Log out asks "Are you logging out, bro?".

### Round 28

1. Formater page: a "PDF Tally | Zoho" switch in the button bar (at first the user's own layout from My Settings); the layout
   chosen is kept with the quotation (Quotations.PdfLayout) for Preview, Approve and the PDF later. Formater quotations made
   before keep the Zoho layout.
2. Ctrl+P on the quotation lists (Today's Quotations, Drafts, Quotation History …) prints the clicked quotation straight to
   the printer (approved: its PDF; otherwise DRAFT); with none clicked: "Click a quotation first."
3. Settings: the Greetings switch shows only On / Off; new administrator switch "XO game" (On / Off, off at first): when off,
   Ctrl+Alt+Shift+X does nothing.
4. Quotation screen: where the date cannot be changed it reads like the quotation number ("Dated 14-Nov-2026", no grey box).
5. Themes removed: Neon Night and Glass (Black) (their users get Default Dark), Sunset and Frost (Default Light).
6. PDFs (Tally and Zoho layouts): when the items go on to a second page, the first page is filled; only the items that do
   not fit go on with the totals (before, the first page was cut to the room the last page needs, leaving it a quarter empty).
7. Quotation screen: a gear next to Items: after changing a line, Enter goes on to the next line, either the whole line
   (cursor in Quantity) or one column top to bottom (Item name, Description, Quantity, Rate or Discount); "Going down: Rate ·
   line 2 of 3" shows meanwhile. Off at first; remembered on the PC.


### Round 29

1. Modern look on every screen (soft borderless buttons with symbols, soft typing boxes, floating lists with a tick, round
   calendar days, − / + number boxes, pill tabs, status chips, one-line help with ⓘ, small rounded menus), Settings → Company
   in cards, the Reload button removed, a your-name menu and a round Log out button in the top bar.
2. Gear menu: switches, "Next line", go up instead of down, the same discount on all lines, hide the HSN and GST columns.
3. Keys: F1 shows all shortcuts; F5 refreshes every list; Alt+C, Alt+I, Ctrl+Shift+S, Ctrl+O, Ctrl+W, Ctrl+G (Formater).
4. Themes: Maroon redesigned (Maroon Frame, same colours); new theme Maroon Mahal.
5. Tally: item names with ° and other special signs are read correctly (Unicode); the search finds "A B auto" and "AB auto".
6. Mac app (.dmg, Apple Silicon and Intel) and the Android phone app (APK), both connected to the Quotation Server.

### Round 30

1. New theme **Tally**, looking like TallyPrime: a dark blue-green top bar, a light blue page, white boxes with thin lines,
   square corners and no shadows; the chosen menu item and list row in a yellow bar; menu headings in capitals with one red
   letter in each item — on the Dashboard (or in the menu) pressing that letter opens the page, like Gateway of Tally.
   The Dashboard's number boxes have a dark heading strip; a light band shows where you are.
2. A key bar on the right lists the app's keys (F1, F5, Ctrl+S, Ctrl+Enter, Alt+C …): the ones this page uses in black, the
   others grey; clicking one works like pressing it.
3. Quotation screen: "Quotation" (or "Proforma Invoice") in a dark box, like a Tally voucher type; Approve first asks
   "Approve? Yes or No" (Y / N, or Enter / Esc), like Tally's "Accept?".
4. The Tally theme is one more theme in the list, on Windows, on the Mac (the key bar shows ⌘ and ⌥) and on the Android
   phone (More → Theme): there the page headings sit in a dark blue-green bar, boxes are square with thin lines, the number
   boxes have a dark strip, the chosen tab is yellow, and Approve asks "Approve? Yes or No".
5. Formater: typing or pasting at the moment the page opens no longer shows the customer and items of the older text.

### Round 31

1. Keyboard highlight: no square black box around buttons, choices, switches, tick boxes, tabs and drop-downs any more; a soft
   ring in the theme colour that follows each control's own shape (inside the shape where it would otherwise be cut off).
2. Floating lists (Customer, Product, drop-downs, calendars): a soft shadow with room around it, so no square lines.
3. Formater: the typing box is back on the left of the page (it had slipped behind the result boxes).
4. Gear next to Items: several columns can be on together (e.g. Rate and Discount): Enter goes through the chosen boxes of a
   line from left to right, then on to the same boxes of the next line. "Next line" (the whole line) goes on by itself. The
   menu stays open while switches are turned on and off (a click outside closes it).
5. Theme Maroon Mahal removed; its users get Glass (Maroon).
6. Quotation screen: the heading is the quotation number ("TSQ2627-1 (provisional)" until it is saved) instead of "New
   Quotation"; the date sits right above "Ref. dated", inside the References box. The item table headings are bold.
7. Quotation lists (Today's, Drafts, History): the Source column is left out for now; clearer Open and Duplicate buttons.

### Round 32

1. Quotation lists (Today's, Drafts, History): the Item column is left out.
2. Installers: the publisher shows as "Mohamed" (no more "Unknown publisher" in the installer details).
3. Tally page: the help line shows the real automatic sync times (changes every few hours, a full sync every night); "Sync
   changes" (F9) reads only what changed in Tally, for anyone, right after editing items, prices or customers in Tally.
4. My Settings → WhatsApp (this PC): the WhatsApp button opens the WhatsApp app or WhatsApp Web in the browser.
5. Themes Default Light, Default Dark and Same as Windows removed: their users get Glass (Blue) (Midnight Stars for the dark
   ones); Glass (Blue) is the theme at first start.
6. Glass (Green) is now **Grass Green**, with clouds, a pale sun, soft hills and grass at the foot of the pages.
7. Dashboard: the Gmail and Claude AI status chips no longer touch.
8. Seal PIN question: a rubber stamp instead of the question mark.
9. Hide menu / Show menu: the side menu glides between full width and icons only (no glide with "Reduce animations").
10. Phone and Mac connect with the server PC's name (e.g. TSSERVER), not only its IP address: when they cannot look the name
    up, they ask the office network and the Quotation Server answers (a small UDP listener on port 5080, with its own
    firewall rule in the server installer); Windows' own name service also answers for a server not yet updated. The
    address found is kept for 10 minutes and asked again when the server PC gets a new address.
11. Update question: no "An administrator asks everyone to update now" sentence.
12. My Settings → Selected text (this PC): Soft blue (the default; the old solid blue hid the letters), Blue sketch (white
    letters), Yellow highlighter, Theme colour (a see-through tint of the theme's colour).
13. Server backups and restores close only the copies' own database connections (they closed every open connection of
    the program, so a request at the moment of a backup could fail once); the tests' servers do the same, which also
    ends a rare test failure on Windows.

Server update needed (10: the finder and its firewall rule; 13).

### Round 33

1. Themes renamed: Glass (Blue) is now **Frost**, Glass (Maroon) is now **Hatims Maroon** (same looks; everyone keeps their
   theme).
2. Quotation page heading: the number in a coloured "ticket" (document icon, small "QUOTATION NO." over the big number, the
   theme's colour, soft shadow); "(provisional)" after it only before the first save; the status as a chip with a coloured
   dot (● Draft, ● Approved …); the date on the right in a pill with a calendar icon and no word "Dated" (still a date box
   for the administrator before approval). Every theme, Tally included (square, flat).
3. Sums in number boxes (Quantity, Rate, Disc. %, P&F and every other number box): type "=12*2", "=3159-5%" (5 % off),
   "=1500+10%", "=21978/1.18" (the rate without GST) or "=200*10%"; the answer shows under the box in a green chip, and
   Enter or leaving the box puts it in.
4. Paste Excel rows with prices: rows copied from Excel (or a price list) come into the pasted-list window with their
   quantity, rate and discount; the columns are guessed (headings such as Qty / Rate / Disc are read when there are any),
   and a chip over each column lets you say what it is (Name, Part No, Qty, Rate, Disc %, Left out). The rate from the
   sheet is used, with the Tally rate shown under it when they differ.
5. Paste a price column into Rate: a column of prices copied from Excel and pasted into the Rate box fills the rates of
   the lines from the line being edited downwards ("12 prices pasted into Rate — lines 1 to 12", with Undo).
6. Stars: ☆ / ★ on each row of the product and customer search lists (PC, Mac and phone): starred ones come first in your
   own searches, under "★ YOUR STARRED ITEMS", then "OTHER MATCHES". Each user has their own stars. The automatic matching
   (paste a list, Formater, the phone's message) still takes the best match. Administrator switch: Settings → Quotation
   → Stars (on at first).
7. Google Drive backup (Settings → Backup & updates): every database backup and every approved PDF is also copied to the
   office Google Drive ("TS Quotations" → "Database backups", and "Approved PDFs" with a folder per month), every 15
   minutes when there is something new, and on "Back up now". Ticks for backups and PDFs; the card shows the Google
   account, the last copy and what went wrong. Uses the Google account of the Gmail connection: connect it again once so
   Google asks to allow Google Drive (and switch on "Google Drive API" in the same Google Cloud project as the Gmail API).
   The app can only see the files it puts there.
8. Phone: Photo button in "New from a message": take a photo of an enquiry (or choose a picture, e.g. a WhatsApp
   screenshot); Claude AI reads it into the message box (customer, then "item, qty, rate" lines) to check before Make
   quotation. Needs Claude AI set up (Settings → Claude AI).
9. Phone: Speak button in "New from a message": say each item ("two bevel protractors at fifteen thousand six hundred");
   each becomes a line ("Bevel protractor, 2, 15600"), then say the next or tap Stop. Uses the phone's own speech
   recognition (Indian English); the app asks for no microphone or camera permission.
10. Festival looks: on Eid (green night, gold crescent and star, lanterns, mosque domes) and Diwali (plum night, fireworks,
    rangoli, diyas) the app wears the festival look by itself and greets everyone ("Eid Mubarak!  ·  Today at a glance"),
    then goes back to each user's own theme. PC, Mac and phone. Eid dates follow a table (the look is on from the day
    before to two days after, as the moon can move it by a day). Administrator switch: Settings → Quotation → Festival
    looks (on at first).
11. Gmail Connection page: the sign-in line says Gmail is read-only and Google Drive is used only for the backup copies.

Server update needed (6: stars; 7: Google Drive backup; 8: reading photos; 10: the administrator's switch).

### Round 34

1. Quotation page heading, simpler: the number on one line in a small light box in the theme's colour (no "QUOTATION
   NO." over it; pointing at it still says "Quotation No."); the status chip and the date pill a little smaller. Square
   in the Tally theme.
2. Top bar: the user is one round icon with the initial (pointing at it shows "ravi · Standard"); clicking it opens the
   same menu (Change my password, My Settings, Log out).
3. Top bar: the connection is a round button with signal bars: three green = connected, two orange = a warning (old data,
   Tally busy, a slow server), one red = not connected; the details when pointing at it or clicking.
4. Fixed: "… also has this quotation open. Changes by both of you can clash" for quotations nobody had open. A quotation
   left for another page (kept for Back) kept counting as open; now it counts only while it is on someone's screen.
5. The right Alt key works for Alt+P (P&F), Alt+C, Alt+I and Alt+← / → (on many keyboard layouts the right Alt is
   "AltGr", which Windows reports as Ctrl+Alt).
6. P&F row in Without GST mode: no more overlapping text ("Packing & Forwarding ₹" with "Without GST" in small under it).
7. My Settings → Sharp text (this PC, on at first): sharper ClearType-style letters on Windows and the small text half a
   point bigger; Off = the old look.
8. Approved quotation: "Clear" on the right (where Cancel was) starts a new, empty quotation; the approved one stays as it
   is (Back returns to it). Also Ctrl+N. Drafts keep Cancel; administrators keep Cancel and Delete.

### Round 35

1. Wrong GSTIN catch: a GSTIN whose last character (the check letter, worked out from the other 14) does not match gets a
   red ring and a red note under the GSTIN in the Buyer box ("Looks mistyped: the last letter should be S. Correct it in
   Tally; the app picks it up at the next sync."), and a red "GSTIN looks mistyped" chip on the folded customer line.
   Approve (and Save as PDF, and Approve from the Formater page) asks first in the GST question: "Go back" or "Approve
   anyway". Drafts are never stopped. Tally is only read; the GSTIN is corrected in Tally.
   (The test Tally's made-up GSTINs now have correct check letters.)
2. GST when the buyer and the ship-to (consignee) are in different states: a "GST by" choice under the consignee, shown
   only when the two states give a different GST (e.g. buyer in Maharashtra, goods to Karnataka, our company in
   Karnataka): "Buyer's state · Maharashtra → IGST" or "Ship-to state · Karnataka → CGST + SGST". New quotations start
   on the buyer's state (the usual "bill to – ship to" rule); the totals change at once; the choice is saved with the
   quotation (reprints and duplicates stay the same). Quotations made before keep the ship-to state, as before.
3. Keys on the quotation screen: Insert puts the cursor in the Customer box (the buyer box opens when it is folded;
   Alt+C does the same now); Delete on an approved quotation clears it like Ctrl+N (a new empty quotation, the approved
   one stays as it is), but never on a draft and never while typing in a box. Both are on the F1 card and the key bar.
   (Found while testing: the "GST by" choices of the page that is leaving and the one that comes in must not affect each
   other, or the app froze; they are now grouped per page.)
4. Settings: the switches are named "Greet", "Fest" and "XO" (were Greetings, Festival looks, XO game), each just On / Off
   with no explanation text.
5. The Graphite theme is removed (PC, Mac and phone); anyone who had it gets Frost.
6. Settings → Users: "Delete user" (red, at the bottom of the Account tab) for users, not the administrator. It asks
   first ("Delete ravi? They can no longer sign in. Their quotations stay, with their name."); the user is signed out on
   every PC, their seal picture and stars are removed; their quotations and the history keep their name.
7. Clear button (approved quotation): now a solid button like Approve, easy to see on every theme; and it shows only
   on the New quotation page after approving there, not on a quotation opened from Today's Quotations or History
   (Ctrl+N and Delete follow the same rule).
8. Names without "TS": the UPI example in Settings is now "yourcompany@okhdfcbank"; files are named Quotation-…
   (installers Quotation-Setup / QuotationServer-Setup, Mac .dmg, phone .apk, exports, backups). The data folders,
   program and service keep their old names so an update finds the same database and saved passwords.
9. The eye buttons (show / hide the buyer box and the remarks box, and the eye in the password box) now sit in the
   middle of their circle (the eye was pinned to the top of its square box, about 3 dots too high).
10. Delivery time per line: My Settings → "Delivery time on items (this PC)" (off at first) adds a Delivery box next to
    Description. Type anything, or pick (none) / Ex-stock / 1 week / 2–3 weeks / 4–6 weeks from its ▾ list (times typed
    often join the list). Empty by default; Enter never stops there, Enter in it adds the line. The line shows a chip
    (green "Ex-stock", orange a waiting time) and the PDF (every layout) prints "Delivery: …" under the item.
11. Seal PIN switch (Settings → Features, off by default): off = the Seal switch seals without asking a PIN (the seal
    picture is still needed); on = the 4-digit PIN as before.
12. Zoho PDF: "Prepared by <name>" at the bottom instead of "Printed on … By: <login>"; our state and code under the
    GSTIN; Dispatch, Destination and Delivery next to Date and Terms (each only when filled), with each label right
    next to its value (the wide gap after "Date :" is gone).
13. Gear menu: a small ✕ at its top right closes it.
14. Click sounds: a soft click when a quotation is saved, two quick clicks when it is approved. My Settings → "Silent"
    (this PC) is on at first, so nothing is heard until it is turned off.
15. The Clear button is filled with the theme's own colour (maroon, green, gold …), with dark text on the gold themes.
16. New PDF layout "Minimal" (white space, thin lines, small grey labels) next to Tally and Zoho: My Settings, Settings
    → PDF and the Formater page.
17. Cancel on a quotation that was never saved asks "Discard this quotation? Nothing is saved; the number is not
    used." (Discard / No).
18. Settings has two new tabs: "PDF" (layout, Zoho heading, Part No. column, fit on one page, headings) and "Features"
    (Proforma invoices, users and others' quotations, Won / Lost, WhatsApp, Greet, Stars, Fest, XO, Seal PIN).

Server update needed (2: the server works out the GST by the chosen state; the database gets the new column by itself;
6: deleting a user is done by the server; 10, 11, 12, 16: the delivery line, the seal without PIN, the Zoho changes and
the Minimal layout are made by the server).

### Round 36

1. Google Drive backup: the folder name is typed in Settings → Backup & updates ("Folder name", at first "TS Quotations").
   A new name: new copies go to the new folder; the old folder stays as it is.
2. Claude AI photo reader: uses the company name from Settings → Company (not a fixed name), and a new "What we sell" box
   in Settings → Claude AI (e.g. "tools and instruments").
3. Gear menu on the quotation page: "Raise all rates by … %" (a minus number lowers them), with Undo in the notice.
4. Diagnostics (administrator) → "PDFs of a month": pick the month, tick Quotations / Proforma invoices, "Download ZIP"
   saves one ZIP in Documents (e.g. Quotations-2026-11.zip with "TSQ2627-101 Sonepar India ….pdf" files).
5. Diagnostics (administrator) → "Who did what": the day's work from the audit log (time, user, chip such as Approved /
   Cancelled / Saved / Settings, the quotation with its customer and value); pick the day and the user.
6. USD quotation (My Settings, next quotation only): switch "USD quotation" and "1 USD = ₹ …". The next quotation is in US
   dollars, without GST: Tally rates are shown in dollars, the screen and the PDF show "$", the words "USD … Cents Only"
   and the line "Amounts in US dollars (USD), without GST. 1 USD = ₹ …". The switch turns itself off once that
   quotation is saved; the rate is remembered. A copy (Duplicate) is in rupees again. USD quotations never count as
   "last rates".
7. Copy from Tally invoice (Settings → Features switch, Off at first): on the quotation page, after the customer is picked,
   "Copy from Tally invoice" lists that customer's sales invoices of the last 12 months (read from Tally only on click,
   one customer, one request; Tally is not changed). Pick one: its items are ticked (untick to leave out), the invoice
   rate beside today's rate with a +X% chip; items no longer in Tally are greyed and skipped; choose "Today's rate" or
   "Invoice rate"; "Add N items". Discount is not copied.
8. Datasheets: Products page → an item → "Add datasheet (PDF)…" (administrator), with Open / Replace / Remove and the tick
   "Add to the quotation PDF when this item is quoted" (on at first). Quoting the item adds the datasheet pages after the
   quotation pages (every layout).
9. Evening summary (Settings → Features switch, Off at first; time, at first 19:00; e-mail address): at that time the
   administrator's screen shows the day's summary (quotations and value, per user, won / lost, drafts still open,
   follow-ups tomorrow) with a WhatsApp button, once a day. With an e-mail address it is also e-mailed through the Gmail
   connection (connect Gmail again once, so Google allows sending). Also in Diagnostics → Who did what → "Summary".
10. My Settings → WhatsApp (this PC): a third choice "Inside this app": WhatsApp opens in its own window that stays
    signed in, apart from the browser (scan the QR code once). It uses the Edge or Chrome already on the PC (no bigger
    download); without either, WhatsApp Web opens in the browser.

Server update needed (1, 2, 4, 5, 6, 7, 8, 9 are done by the server; the database gets the new columns and the
datasheet table by itself).

### Round 37

1. Delivery box: clicking a time in its list (Ex-stock, 1 week …) now fills the box (it only closed the list before).
2. Gear menu: "Same delivery on all lines": click a time (or type one and Apply) and every line gets it, with Undo;
   "(none)" clears them all.
3. USD quotations show "$" in Today's Quotations, History, the number search and the phone app; list totals keep rupees
   and dollars apart ("₹ … + $ …"); the evening summary adds "+ $ …".
4. "1 USD = ₹" must be between ₹ 1 and ₹ 500 (a slip like 0.84 or 8420 is refused).
5. Who did what: "Approvals and cancels only" switch.
6. Quotation lists are searched also by item code (with or without dashes), the item's other names in Tally, HSN,
   remarks, contact person and phone; a search with "_" or "%" now finds what it should.
7. Delivery box is On by default (PCs that had it Off get it On once; it can be switched off in My Settings).
8. Page Down / Page Up on a quotation opened from a list: the next / previous quotation of that list ("2 of 25").
9. Duplicate copies the quotation as it is: every rate stays the same (Tally's rate is no longer put on the copy); the
   "Refresh rates when duplicating" tick box is removed from Settings.
10. USD: the "1 USD = ₹" rate is not remembered (the box is empty again after the USD quotation is saved); a duplicate of
    a USD quotation stays in dollars; a USD quotation converted to a proforma invoice stays in dollars.
11. Settings → PDF: "USD note on the PDF": the full line with the rupee rate (as before), "Amounts in US dollars (USD)."
    only, or no note.
12. Datasheets: one catalogue uploaded for several items is added to the quotation PDF only once.
13. Paste a column from Excel into Qty or Disc % (like prices into Rate): the lines fill from the top, with Undo.
14. My Settings → "Item rows (this PC)": Bigger (the default: larger item text, taller rows) or Normal.
15. Settings is shown only to the administrator; users keep My Settings.

Server update needed (3, 4, 6, 9, 10, 11 and 12 are done by the server).

### Round 38

1. Quotation page: the eye that hides the opened customer section sits at the top right of the References box.
2. Delivery is never remembered: a delivery line typed inside a description ("Delivery: 2-3 weeks", "Ex-stock") is left
   out of the remembered description (old ones are cleaned once), and the Delivery box list shows only (none), Ex-stock,
   1 week, 2–3 weeks and 4–6 weeks (typed times are no longer added).
3. Zoho PDF: "Date :" and the date in bold; the small lines under an item and the Sub Total / P&F / GST / Total rows sit
   closer together; an empty bank row (e.g. A/c Holder's Name) is left out.
4. F9 anywhere in the PC/Mac app: "Sync changes" with Tally (messages when it starts and ends; on the F1 card).
5. Minimal PDF: long buyer / ship-to names, addresses and e-mails wrap inside their own column; PAN, MSME and IEC have their
   own COMPANY column next to BANK.
6. Question boxes: Tab / Shift+Tab (and ← / →) go round inside the box and never to the page behind; a box brings the
   window to the front and puts the keyboard on its main button; Settings → Users: Enter adds / saves the user.
7. Ctrl+D copies the picked line just below; Ctrl+↑ / Ctrl+↓ move it (with Undo; on the F1 card).
8. Item search finds words typed together with a mistake ("borgauge" → BORE GAUGE); typo results show "close match".
9. Server off: saving keeps the quotation on this PC ("Waiting to send", orange strip with "Try now") and sends it by
   itself when the server answers (a resend never makes a second copy).
10. Product links: Settings → PDF → "Load product links (Excel)" (column A product code, column B link); a small 🔗 after
    the item name opens its web page; the PDF prints a clickable "View product" line (switch in Settings, tick on each
    quotation).

Server update needed (2, 3, 5, 8, 9 and 10 are done by the server).
