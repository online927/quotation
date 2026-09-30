# TS Quotation System — Architecture

This document records the architectural decisions for the quotation system that
works alongside TallyPrime + TallyPrime Server. It answers the "First Task"
questions of the specification and is kept up to date as phases are delivered.

---

## 1. Requirements analysis (summary)

| Concern | Key constraint | Consequence |
|---|---|---|
| Tally | Read-only in V1; must use supported mechanisms | XML-over-HTTP requests only. No writes, no file access to Tally data folders. |
| Scale | 10–20k products, ~3.5k customers, ~70 quotations/day | Local synchronized index; full catalogue never sent to Claude. |
| Multi-PC | Several client PCs create quotations concurrently | One **central Quotation Server** owns the database and the number sequence. |
| Numbering | `TSQ2526-3247`, per active FY, configurable, no duplicates | Transactional counter + unique constraint on the server. |
| Offline | Tally may be down; app must stay usable with cached data | Server keeps the last synchronized copy and reports freshness. |
| AI | Claude understands text/email; never invents Tally data | Claude only extracts intent and chooses among candidates the app supplies; app validates everything. |
| Human-in-the-loop | No auto-send, no final quote without approval | AI can only create DRAFT / PENDING_REVIEW. Only a logged-in user can approve. |
| PDF | Must reproduce the existing Tally quotation | Hand-drawn, coordinate-based PDF layout (not HTML-to-PDF). |
| Security | API keys, OAuth tokens never exposed/logged | Secrets live only on the server, encrypted at rest with Windows DPAPI. |

## 2. Final architecture

```
                         ┌─────────────────────────────────────────────┐
                         │  SERVER PC (same machine as TallyPrime       │
                         │  Server, or any always-on PC on the LAN)     │
                         │                                              │
 ┌──────────────┐  XML/  │  ┌────────────────────────────────────────┐  │
 │ TallyPrime   │  HTTP  │  │  Quotation Server  (Windows Service)   │  │
 │ (instance    │◄───────┼──┤  ASP.NET Core, port 5080               │  │
 │ with company │  :9000 │  │                                        │  │
 │ open, XML    │ (read  │  │  • Tally sync worker (full/incremental)│  │
 │ port on)     │  only) │  │  • Product / customer search index     │  │
 └──────────────┘        │  │  • Quotation engine + numbering        │  │
                         │  │  • PDF renderer                        │  │
   Gmail API ◄───OAuth───┼──┤  • Gmail ingestion worker              │  │
   Claude API ◄──HTTPS───┼──┤  • AI orchestrator (Claude tools)      │  │
                         │  │  • Audit log, diagnostics              │  │
                         │  │  • SQLite database (WAL)               │  │
                         │  └──────────────┬─────────────────────────┘  │
                         └─────────────────┼────────────────────────────┘
                                           │ HTTP/JSON (LAN, bearer token)
               ┌───────────────────────────┼──────────────────────────┐
               ▼                           ▼                          ▼
      ┌─────────────────┐        ┌─────────────────┐        ┌─────────────────┐
      │ Client PC 1     │        │ Client PC 2     │        │ Client PC 3     │
      │ Quotation Desk  │        │ Quotation Desk  │        │ Quotation Desk  │
      │ (desktop app)   │        │ (desktop app)   │        │ (desktop app)   │
      └─────────────────┘        └─────────────────┘        └─────────────────┘
```

**Why a central server instead of per-PC databases:** quotation numbers must be
unique across PCs, the Claude API key and Gmail tokens must not live on every
client, the Gmail inbox must be processed exactly once, and the 20k-product sync
should run once — not once per PC. A single server process is the simplest
design that satisfies all of these.

The client is a thin, fast desktop UI. All business rules (calculation,
numbering, validation, PDF) run on the server so every PC produces identical
results. The calculation engine is also linked into the client so that totals
update instantly while typing; the server recalculates authoritatively on save.

## 3. Technology stack

| Layer | Choice | Reason |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | Long-term support, first-class Windows service hosting, fast, single-file self-contained deployment (no runtime install on client PCs). |
| Server | **ASP.NET Core** minimal APIs hosted as a **Windows Service** | Built-in Windows Service support, robust HTTP, background workers. |
| Database | **SQLite (WAL) via EF Core**, owned exclusively by the server | Zero-administration, one file to back up. Only the server process opens it, so multi-PC concurrency is handled by the server, not by file sharing. EF Core allows a later move to PostgreSQL/SQL Server without rewriting the domain. |
| Desktop client | **Avalonia UI 11** (XAML/MVVM) | Native .NET desktop UI that runs on Windows 10/11 with GPU-accelerated rendering; same programming model as WPF, but supports headless automated UI tests, which let us test the UI in CI. |
| PDF | **PDFsharp 6** (MIT) with a custom layout engine | Precise coordinate drawing needed to reproduce Tally's ruled layout; MIT licence (QuestPDF was rejected because its free licence depends on company revenue). |
| AI | **Official Anthropic C# SDK** behind an `IAIProvider` abstraction | Tool-use (function calling) with structured JSON; replaceable provider. |
| Gmail | **Google.Apis.Gmail.v1** with OAuth 2.0 installed-app flow | Google-supported OAuth; the app never sees the Gmail password. |
| Secrets | **Windows DPAPI** (machine scope) | OAuth tokens and API keys encrypted at rest on the server. |
| Installer | Self-contained single-file publish + **Inno Setup** | One installer for the server, one for clients. |
| Tests | xUnit, in-process `WebApplicationFactory`, Tally simulator, Avalonia headless | Everything testable without a real Tally or internet. |

**Why not Electron:** it would add a Node/Chromium runtime to every PC and
we would still need a server for numbering/secrets. **Why not WPF:** WPF is a
fine choice, but it is Windows-only to build *and test*; Avalonia gives the same
XAML/MVVM model, runs natively on Windows, and allows automated UI tests on
the build server.

## 4. Tally integration approach

TallyPrime exposes an **XML-over-HTTP** interface. When a TallyPrime instance
has the company loaded and *Connectivity → TallyPrime acts as → Both (or Server)*
enabled on port 9000, any program can POST XML requests to it.

With **TallyPrime Server**, the company data is served by the TallyPrime Server
service, but the XML/HTTP port is provided by a **TallyPrime application
instance** (on the server PC or on a client PC) that has the company open.
The Quotation Server is configured with that instance's address, e.g.
`http://192.168.1.10:9000`. Recommended: run a TallyPrime instance on the server
PC itself, logged in with a read-only Tally user.

Requests used (all read-only `Export` requests with inline TDL collections):

| Purpose | Request |
|---|---|
| Discover companies | `Export / Collection` of `Company` (name, GUID, books-from, starting-from, AltMstId) |
| Active financial year / current period | Inline TDL collection computing `##SVFROMDATE`, `##SVTODATE`, `##SVCURRENTDATE` |
| Stock items | `Collection` of `StockItem` with `NATIVEMETHOD` limited to needed fields (name, aliases, parent, base units, description, part no., GST/HSN details, standard selling price, price levels, GUID, AlterID) |
| Stock groups | `Collection` of `StockGroup` (for GST/HSN inherited from group) |
| Units | `Collection` of `Unit` |
| Customers | `Collection` of `Ledger` filtered to configured groups (default *Sundry Debtors*) with address, state, pincode, GSTIN (both legacy `PARTYGSTIN` and TallyPrime 3+ `LEDGSTREGDETAILS.LIST`), mailing details, contact, phone, e-mail, PAN |

Robustness rules: responses are sanitized (Tally emits control-character
entities that are invalid XML), dates are parsed from Tally's `YYYYMMDD`,
amounts such as `15600.00/NOS` are parsed into rate + unit, and GST details are
resolved by *applicable-from* date with fallback to the stock group, then the
company. Tally errors (`<LINEERROR>`) are surfaced as typed exceptions.

**Incremental sync.** Every Tally master carries an `ALTERID`, and the company
carries the highest master AlterID. The sync worker stores the last seen value;
if unchanged, nothing is fetched. Otherwise only masters with
`$AlterID > lastSeen` are requested (TDL `FILTER`). Deletions are detected by a
lightweight GUID-only listing (names and GUIDs, ~1 MB for 20k items) and by the
nightly full sync. Sync runs in a staging transaction so an interrupted sync
never leaves half-written data.

**No writes to Tally.** The Tally client only ever sends `TALLYREQUEST=Export`.
This is enforced in code: the request builder has no import capability and a
unit test asserts that every generated request is an export.

## 5. Database architecture

One SQLite database file on the server (`%ProgramData%\TSQuotation\quotation.db`),
WAL mode, busy-timeout, automatic daily backup copies.

| Table | Purpose |
|---|---|
| `Products` | Synchronized stock items (name, aliases, part no., brand, manufacturer, description, group, unit, HSN, GST %, rate, rate date, Tally GUID/AlterID, deleted flag, synced-at) |
| `StockGroups`, `Units` | Synchronized masters used for GST/HSN inheritance and unit display |
| `Customers` | Synchronized ledgers (billing address, state, state code, GSTIN, PAN, contact, phone, e-mail) |
| `Quotations` | Header with **snapshots** of buyer/consignee/company values, number, FY, status, source, totals, created/approved by/at, PDF path, row-version |
| `QuotationLines` | Line snapshots: product id, name, description lines, HSN, GST %, due on, qty, unit, rate, discount, amount |
| `NumberSeries` | One row per (series, financial year): pattern, next sequence |
| `EmailMessages` | Gmail message id (unique), thread id, received time, status, quotation id, result, attempts |
| `AiConversations` | AI extraction results and candidate lists for the AI Inbox |
| `SyncRuns` | History of sync runs, counts, errors |
| `Users`, `ApiSessions` | Application users and hashed session tokens |
| `Settings` | Company settings and application configuration (JSON) |
| `AuditLog` | Who did what, when, from which PC |

Quotations store snapshots (not just foreign keys) so that a later Tally change
never alters an already generated quotation.

## 6. Multi-PC quotation numbering

1. The number pattern is configurable, e.g. `TSQ{FY2}-{SEQ}` → `TSQ2526-3247`.
2. The financial year is taken from the quotation date and must equal the
   **active financial year** detected from Tally (cached on the server; admin
   override available).
3. A number is allocated **only on the server**, inside a single database
   transaction that (a) increments `NumberSeries.NextSequence` for that FY and
   (b) writes the quotation with the number. A `UNIQUE` index on
   `Quotations.Number` is the final safety net.
4. The server additionally serialises allocations with an in-process lock, so
   concurrent requests from several PCs are queued rather than retried.
5. The starting sequence per FY is configurable, so the new system can continue
   from the existing Tally quotation series (e.g. start at 3248).
6. Numbers are allocated when a user saves or approves a quotation — AI-created
   drafts that are later ignored never consume a number. The new-quotation
   screen shows the *expected* next number, clearly marked as provisional.

## 7. Product indexing (10–20k items)

An in-memory search index is built on the server from the `Products` table at
start-up and rebuilt after every sync (≈100 ms for 20k items).

* **Normalization:** lower-case, punctuation → spaces, unit spacing unified
  (`10 mm` ≡ `10mm`, `2.5 sq mm` ≡ `2.5sqmm`), and a *compact identifier* form
  (`187-901-10` → `18790110`) so part numbers match with or without dashes.
* **Inverted index** token → items, plus a sorted token array for fast
  **prefix** lookups (binary search).
* **Trigram index** for **fuzzy/typo** matching, verified with
  Damerau–Levenshtein distance (1 edit for ≤5 chars, 2 for longer tokens).
* **Field-weighted scoring:** name, alias and part number weigh most; brand,
  manufacturer, group and description less. Bonuses for exact name/alias/part
  number and "starts-with" matches; all query tokens must match (AND) before
  falling back to partial matches.
* Target: < 10 ms per query for 20k products (covered by a performance test).

The same engine indexes customers (name, alias, GSTIN, city, phone, e-mail).

## 8. Claude AI tool architecture

```
User text / e-mail ──► AI Orchestrator ──► IAIProvider (Claude)
                           ▲   │                 │ tool calls (JSON)
                           │   ▼                 ▼
                     Tool executor (app code, read-only DB access)
                     search_customer · search_product · get_product_details
                     get_customer_details · get_current_rate
                     get_quotation_history · ask_clarification
                     create_quotation_draft (validated, draft only)
```

* Claude receives only the request text and the **top candidates** returned by
  the app's search — never the whole catalogue.
* Claude returns structured tool calls. `create_quotation_draft` accepts only
  **ids** of products and customers returned by earlier search calls, plus
  quantities. HSN, GST, rate, unit and addresses are always filled in by the
  application from synchronized Tally data; any values Claude supplies for
  them are ignored.
* Product selection is accepted only when deterministic evidence supports it
  (exact part number / alias / name, or a clear ranking margin with all
  specification tokens matched). Otherwise the line is marked **Needs
  selection** and the user picks from the candidate list. Claude's confidence
  value alone never approves a product.
* Drafts created by AI have status `PENDING_REVIEW`; approval requires a user.

## 9. Gmail integration

* OAuth 2.0 *installed application* flow with the `gmail.readonly` (and later
  `gmail.modify` for labels) scope; the refresh token is encrypted with DPAPI
  on the server. The app never asks for the Gmail password.
* A background worker polls a configured label/query (e.g.
  `label:Quotations is:unread`) every few minutes using the Gmail history API.
* Each message is recorded in `EmailMessages` with the Gmail message id as a
  unique key **before** processing; status transitions
  `UNPROCESSED → PROCESSING → DRAFT_CREATED/FAILED/IGNORED → COMPLETED`.
  A restart resumes `PROCESSING` items idempotently; the unique key prevents
  duplicate drafts.

## 10. PDF rendering approach

* Coordinate-based drawing with PDFsharp: ruled boxes, fixed column grid and
  fonts sized like Tally's print (A4 portrait, 8–9 pt body).
* A layout engine measures every text block (word-wrapping descriptions and
  addresses) before placing it; rows are never split across pages.
* Multi-page: header block (company, buyer, consignee, reference grid) on every
  page, column headings repeated, "continued…" markers, page numbers, and the
  totals, amount-in-words, declaration, bank details and signature block kept
  together on the last page (moved to a new page if they do not fit).
* All company values come from Settings; nothing is hard-coded.

## 11. Folder structure

```
quotation/
├─ docs/                     Architecture, setup, user guide
├─ src/
│  ├─ Quotation.Core/        Domain model, calculation engine, amount-in-words,
│  │                         numbering, financial year, search index, validation
│  ├─ Quotation.Contracts/   DTOs shared by server and client
│  ├─ Quotation.Data/        EF Core DbContext, entities, migrations
│  ├─ Quotation.Tally/       Tally XML client, request builder, parsers, sync
│  ├─ Quotation.Pdf/         Tally-style PDF renderer
│  ├─ Quotation.AI/          IAIProvider, Claude provider, tools, orchestrator
│  ├─ Quotation.Gmail/       Gmail OAuth + ingestion
│  ├─ Quotation.Server/      ASP.NET Core Windows Service (REST API, workers)
│  ├─ Quotation.ApiClient/   Typed HTTP client used by the desktop app
│  └─ Quotation.Desktop/     Avalonia desktop application
├─ tools/
│  └─ Tally.Simulator/       Fake TallyPrime XML server for development/tests
├─ tests/                    Unit, integration and UI tests
└─ installer/                Inno Setup scripts and publish scripts
```

## 12. Future modules (designed for, not built in V1)

* **Messaging (WhatsApp):** `IMessageChannel` interface; a generated quotation
  raises a `QuotationGenerated` event. A WhatsApp channel can subscribe later.
  Disabled in V1.
* **AI capabilities:** the tool registry and `AiConversations` table allow
  adding tools such as "same quotation, quantity changed" or "quote same
  product as last time" without changing the orchestrator.
* **OCR / voice / bulk:** new *request sources* feed the same orchestrator
  (`QuotationSource` already distinguishes MANUAL / AI / GMAIL).
