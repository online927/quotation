# TS Quotation System

Quotation management and AI quotation automation for **TallyPrime + TallyPrime Server**
(T.SAIFUDDIN & CO.).

* Reads customers, stock items, units, HSN/SAC, GST and selling rates from Tally (read-only).
* Tally-style, keyboard-driven quotation entry with instant product/customer search (20k+ items).
* Central quotation numbering per active financial year (e.g. `TSQ2526-3247`) safe across many PCs.
* PDF that reproduces the existing Tally quotation layout.
* Claude AI: natural-language quotations and Gmail quotation requests → drafts for human approval.

| Document | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Architecture, technology choices, Tally/AI/Gmail/PDF design |
| [docs/SETUP.md](docs/SETUP.md) | Installation, Tally, Gmail, Claude, company details, first quotation, troubleshooting |
| [docs/PHASES.md](docs/PHASES.md) | Delivery phases, what each delivered, and how to test it |

## Status

All 15 phases are implemented and covered by 280 automated tests (unit, integration, headless UI,
real-process end-to-end). Sample output: [single-item PDF](docs/samples/sample-quotation-single.pdf),
[multi-page PDF](docs/samples/sample-quotation-multipage.pdf).

## Solution layout

```
src/Quotation.Core         Domain, calculations, numbering, search, validation, messaging extension point
src/Quotation.Contracts    DTOs shared between server and desktop (incl. AI analysis results)
src/Quotation.Data         EF Core (SQLite) database and migrations
src/Quotation.Tally        TallyPrime XML/HTTP client, master parsers, GST/rate resolution
src/Quotation.Pdf          Tally-style quotation PDF renderer
src/Quotation.AI           AI provider abstraction, Claude provider, quotation assistant, match policy
src/Quotation.Gmail        Gmail OAuth (installed app) and read-only mailbox client
src/Quotation.Server       Central Quotation Server (ASP.NET Core, Windows Service)
src/Quotation.ApiClient    Typed HTTP client for the server
src/Quotation.Desktop      Desktop application (Avalonia)
tools/Tally.Simulator      Simulated TallyPrime (20k items) for development, demos and tests
installer/                 Publish scripts and Inno Setup installers (server + client)
tests/                     Unit, integration, headless UI and end-to-end tests
```

## Developer quick start

Requires the .NET 10 SDK.

```bash
dotnet test                                   # run all tests
dotnet run --project tools/Tally.Simulator    # simulated TallyPrime on :9000
dotnet run --project src/Quotation.Server     # server on http://localhost:5080 (login admin/admin)
dotnet run --project src/Quotation.Desktop    # desktop app
```
