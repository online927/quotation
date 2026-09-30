# Delivery phases

Each phase ends with: build → tests → fixes → summary → how to test.

| # | Phase | Status |
|---|---|---|
| 1 | Architecture and repository setup | ✅ Done |
| 2 | Tally connectivity | ⏳ |
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
