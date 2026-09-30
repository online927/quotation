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
| [docs/SETUP.md](docs/SETUP.md) | Installation and configuration |
| [docs/PHASES.md](docs/PHASES.md) | Delivery phases, status and how to test each one |

## Solution layout

```
src/Quotation.Core         Domain, calculations, numbering, search, validation
src/Quotation.Contracts    DTOs shared between server and desktop
src/Quotation.Data         EF Core (SQLite) database and migrations
src/Quotation.Server       Central Quotation Server (ASP.NET Core, Windows Service)
src/Quotation.ApiClient    Typed HTTP client for the server
src/Quotation.Desktop      Desktop application (Avalonia)
tools/                     Tally simulator and utilities
tests/                     Unit, integration and headless UI tests
```

## Developer quick start

Requires the .NET 10 SDK.

```bash
dotnet test                                   # run all tests
dotnet run --project src/Quotation.Server     # server on http://localhost:5080 (login admin/admin)
dotnet run --project src/Quotation.Desktop    # desktop app
```
