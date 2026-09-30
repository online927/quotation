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
