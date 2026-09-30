# Delivery phases

Each phase ends with: build → tests → fixes → summary → how to test.

| # | Phase | Status |
|---|---|---|
| 1 | Architecture and repository setup | ✅ Done |
| 2 | Tally connectivity | ✅ Done |
| 3 | Customer/product synchronization | ⏳ |
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
