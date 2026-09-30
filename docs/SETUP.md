# TS Quotation System — Setup Guide

This guide takes you from nothing to your first approved quotation PDF.

```
 SERVER PC  ─ TallyPrime Server + a TallyPrime instance with the company open (XML port 9000)
            ─ TS Quotation Server (Windows Service, port 5080)  ← database, numbering, PDFs, AI, Gmail
 CLIENT PCs ─ TS Quotation (desktop app)  → talks only to the Quotation Server
```

The Quotation Server only **reads** from Tally. It never creates or changes vouchers or masters in Tally.

---

## 1. Requirements

| Item | Requirement |
|---|---|
| Server PC | Windows 10/11 or Windows Server 2016+, 64-bit, 4 GB RAM, 2 GB free disk. Always on during office hours. Usually the TallyPrime Server PC. |
| Client PCs | Windows 10/11 64-bit. No .NET installation needed (the app is self-contained). |
| Network | Client PCs must reach the server on **TCP 5080**. The server must reach TallyPrime on **TCP 9000**. |
| TallyPrime | TallyPrime (any current release) with the company open in one TallyPrime instance that stays running. |
| Optional | Claude API key (AI assistant). A Google account/Workspace with Gmail (e-mail requests). |

---

## 2. Install the Quotation Server (once, on the server PC)

1. Run **`TSQuotationServer-Setup-x.y.z.exe`** as administrator and accept the defaults.
   The installer:
   * installs to `C:\Program Files\TSQuotation\Server`,
   * registers and starts the Windows Service **TS Quotation Server** (starts automatically, restarts on failure),
   * opens Windows Firewall TCP 5080 for domain/private networks,
   * creates the data folder `C:\ProgramData\TSQuotation` (database, PDFs, logs, backups).
2. Check it runs: on the server open `http://localhost:5080/api/health` in a browser → `{"status":"ok",…}`.
3. Optional settings in `C:\Program Files\TSQuotation\Server\appsettings.json` (restart the service after changes):
   * `"Urls": "http://0.0.0.0:5080"` — change the port if 5080 is in use (then update the firewall rule and the clients).
   * `"Server": { "DataDirectory": "" }` — leave empty for `C:\ProgramData\TSQuotation`.

Service control: `services.msc` → *TS Quotation Server*, or `sc stop TSQuotationServer` / `sc start TSQuotationServer`.

---

## 3. Configure TallyPrime (XML/HTTP access)

The Quotation Server talks to TallyPrime through TallyPrime's supported XML-over-HTTP interface.

1. On the PC that will serve the data (recommended: the server PC), start **TallyPrime** and open the company.
2. Press **F1 (Help) → Settings → Connectivity → Client/Server configuration**:
   * **TallyPrime acts as**: `Both` (or `Server`)
   * **Enable ODBC**: `Yes` (not required, harmless)
   * **Port**: `9000`
   Accept (Ctrl+A). Restart TallyPrime if asked.
3. If this TallyPrime instance is not on the same PC as the Quotation Server, allow inbound **TCP 9000** in that PC's Windows Firewall.
4. **With TallyPrime Server**: the XML port is provided by the TallyPrime *application*, not by the TallyPrime Server
   service. Keep one TallyPrime instance logged in with the company open (for example on the server PC, logged in as
   a dedicated Tally user that has display-only rights).
5. **Active financial year**: the Quotation Server uses the *current period* of that TallyPrime instance
   (Alt+F2 → Period). Keep it set to the current financial year. An administrator can override it in
   Settings → Tally if needed.

Test from the server PC: open `http://localhost:9000` in a browser — TallyPrime answers
`<RESPONSE>TallyPrime Server is Running</RESPONSE>`.

---

## 4. Install the desktop app (each client PC)

1. Run **`TSQuotation-Setup-x.y.z.exe`**. When asked for the **Server address**, enter `http://SERVER-PC-NAME:5080`
   (or its IP, e.g. `http://192.168.1.10:5080`).
   For silent roll-out: `TSQuotation-Setup-x.y.z.exe /VERYSILENT /SERVER=http://192.168.1.10:5080`.
2. Start **TS Quotation**. The login screen shows the server address — press **Test** to check the connection.
3. First login: user **`admin`**, password **`admin`**. You must set a new password immediately.
4. Create a user for each person: **Settings → Users → Add user** (they choose their own password at first login).
   Only administrators can change settings, Tally/Gmail connections and view Diagnostics.

---

## 5. Connect to Tally and run the initial synchronization

1. **Settings → Tally**:
   * **TallyPrime XML/HTTP address**: `http://localhost:9000` if TallyPrime runs on the server PC, otherwise
     `http://TALLY-PC:9000`.
   * **Company name**: leave empty to use the company open in TallyPrime, or type the exact name.
   * **Customer ledger groups**: default `Sundry Debtors` (sub-groups included). Add others if customers are kept elsewhere.
   * **Selling rate source**: `StandardPrice` (Standard Selling Price in the stock item) or `PriceLevel` + the price level name.
   * **Save (Ctrl+S)**.
2. **Tally Connection → Test connection (Ctrl+T)** → shows the company and the active financial year.
3. Press **Full sync**. 20,000 items typically take well under a minute. The page shows the counts and
   *Last synchronized*.
4. Check one product: **Products** → search a known item → HSN, GST and Rate should match Tally.
   If HSN/GST/rate are empty: **Tally Connection → Diagnostics** → fetch the raw XML for that item to see what
   Tally returns; if fields are missing, set **Settings → Tally → Fetch mode = All** and run **Full sync** again.

After this, changes in Tally are picked up automatically every 15 minutes (**F9 / Sync changes** for an immediate
update) and a full sync runs nightly at 02:00 (configurable).

---

## 6. Company details and quotation settings

**Settings → Company** (everything printed on the PDF): company name, address lines, mobile/phone, GSTIN/UIN,
state name and code, e-mail, PAN, MSME number, IEC, bank details (account holder, bank, account number, branch,
IFSC), optional logo (a PNG/JPG path **on the server PC**, e.g. `C:\ProgramData\TSQuotation\logo.png`),
authorised signatory label, default prepared/verified by, declaration, validity days and text, terms &
conditions (one per line), default payment terms and footer.

**Settings → Quotation**:
* **Number pattern**: default `TSQ{FY}-{SEQ}` → `TSQ2526-3247`. Tokens: `{FY}` 2526, `{FY_LABEL}` 2025-26,
  `{FYS2}`/`{FYE2}`, `{FYS4}`/`{FYE4}`, `{SEQ}` or `{SEQ:4}` (zero-padded). Press **Preview**.
* **Next quotation number**: to continue your Tally quotation series, enter the number after your last Tally
  quotation (last was TSQ2526-3247 → enter **3248**) and press **Set next number**. Stop numbering quotations in
  Tally from that point to avoid overlaps.
* **GST presentation**: `RateOnly` (GST % per line, totals without GST — like the Tally quotation) or
  `ComputeTax` (CGST+SGST or IGST added; requires the company *state code*).
* Packing & forwarding default, round-off, refresh rates when duplicating, stale-data rules, HSN required.

---

## 7. Configure the Claude AI assistant (optional)

1. Create an API key at <https://console.anthropic.com> (Settings → API keys). Add billing/credits.
2. **Settings → Claude AI**: tick **Enable**, keep the model `claude-opus-5-5` (or enter another Claude model id),
   paste the **API key**, **Save**. The key is stored encrypted on the server and is never shown again or logged.
3. Test: **AI Inbox** → type `Create quotation for <customer> for 2 <product>` → **Analyse**.

What is sent to Claude: the request/e-mail text and the names/part numbers/units of the few best-matching
products and customers returned by the search (never the whole catalogue, never prices you have not been
asked about). Claude cannot create or approve quotations: it proposes; the application checks everything
against Tally; a person approves.

---

## 8. Connect Gmail (optional)

The app uses Google's OAuth sign-in; it never asks for your Gmail password. Access is **read-only**.

**A. Create an OAuth client (once)**
1. Go to <https://console.cloud.google.com> → create a project (e.g. "TS Quotation").
2. **APIs & Services → Library** → enable **Gmail API**.
3. **APIs & Services → OAuth consent screen**: User type **Internal** (Google Workspace) or **External**
   (personal Gmail). Fill in the app name and your e-mail. Add the scope `.../auth/gmail.readonly`.
   For *External*, add your Gmail address as a test user **and then press "Publish app"** — apps left in
   "Testing" lose access after 7 days.
4. **APIs & Services → Credentials → Create credentials → OAuth client ID → Application type: Desktop app**.
   Download the JSON file.

**B. Configure the Quotation System**
1. In Gmail create a label, e.g. **Quotations**, and a filter that applies it to quotation requests
   (or apply it manually).
2. **Settings → Gmail**: tick **Enable**, set the **Gmail search query** (default
   `label:Quotations newer_than:14d`), poll interval, paste the **entire content** of the downloaded JSON
   into *OAuth client JSON*, **Save**.
3. **Gmail Connection → Connect Gmail** (administrator). Your browser opens Google's page; sign in with the
   mailbox, allow read access. The page says "Gmail connected"; the app shows the connected account.
4. **Check now** processes matching e-mails immediately; afterwards the server checks every few minutes.

Each e-mail is processed once (its Gmail message id is recorded). Results appear in the **AI Inbox**:
*Ready for review* (a draft was created — **Open Draft**) or *Needs clarification* (choose the customer/product,
enter the quantity → **Create Draft**). Nothing is ever sent to customers automatically.

---

## 9. Your first quotation

**Manually (keyboard)**
1. **Ctrl+N** (New Quotation). The provisional number and today's date are shown.
2. Type part of the customer name (or GSTIN/phone) → ↓ → **Enter**. Bill-to and ship-to are filled from Tally;
   choose another *Ship to* address if needed.
3. In **Product**, type e.g. `universal bevel` or `187-901` → **Enter** → quantity → **Enter** → rate (Tally rate
   pre-filled, change if needed) → **Enter** → discount % → **Enter**: the line is added. Repeat for more lines.
   Add description lines (brand, model, MOQ) in the box below the product before adding the line.
4. Fill references (buyer's order no., payment terms, delivery …) and packing & forwarding if needed.
5. **Ctrl+S** saves the draft and assigns the quotation number. **F11** opens a preview (DRAFT watermark).
6. **Ctrl+Enter — Approve & Generate PDF**: the app refreshes Tally data, validates everything, generates the PDF
   and opens it. PDFs are saved on the server in `C:\ProgramData\TSQuotation\pdf\<year>\<number>.pdf` and a copy is
   opened from `Documents\Quotations` on your PC for e-mailing.

**With AI**: AI Inbox → type the request → **Analyse** → **Open Draft** (or resolve choices → **Create Draft**) →
review → **Ctrl+S** → **Ctrl+Enter**.

**Repeat business**: open an old quotation (History, **Ctrl+H**, or **Ctrl+F** search) → **Duplicate (Ctrl+D)**:
new number, today's date, same customer and products, current Tally rates.

---

## 10. Backups, upgrades and moving the server

* **Automatic backups**: every day to `C:\ProgramData\TSQuotation\backups` (kept 30 days). Include this folder in
  your normal backup. Admins can take one at any time: **Diagnostics → Backup database now**.
* **Restore**: stop the service, copy the chosen backup to `C:\ProgramData\TSQuotation\quotation.db`
  (delete `quotation.db-wal`/`-shm` if present), start the service.
* **Upgrade**: run the new server installer (settings and data are kept), then the new client installer on each PC.
* **Moving to another PC**: install the server there, stop both services, copy `C:\ProgramData\TSQuotation`, start.
  Re-enter the Claude API key and reconnect Gmail (secrets are encrypted per machine).

---

## 11. Troubleshooting

| Symptom | What to check |
|---|---|
| Desktop: "Cannot reach the Quotation Server" | Server service running (`services.msc`)? Correct address on the login screen? Firewall TCP 5080 on the server? Try `http://SERVER:5080/api/health` in a browser on the client. |
| Tally Disconnected | TallyPrime running with the company open? F1 → Settings → Connectivity: acts as *Both*, port 9000? Firewall TCP 9000? Settings → Tally address correct? The exact error is on the Tally Connection page. Searching keeps working with the last synchronized data. |
| "Company 'X' is not open in Tally" | Open that company in TallyPrime, or clear Settings → Tally → Company name. |
| Wrong financial year / numbers | Check TallyPrime's current period (Alt+F2). Or set Settings → Tally → Active FY override. |
| HSN / GST / rate empty or wrong | Tally Connection → Diagnostics raw XML; Settings → Tally → Fetch mode = All; rate source/price level; then Full sync. Items without a selling price in Tally have no rate — enter one in the quotation. |
| Quotation date rejected | Dates must be inside the active financial year. |
| "Rates changed in Tally" on approval | The quotation was updated with the new Tally rates; review and approve again. |
| "Live Tally data could not be retrieved" | Tally is offline or the last sync is old. Approve anyway (logged) if allowed, or fix Tally first. |
| PDF failed | The quotation stays approved; use **Regenerate PDF**. Check Diagnostics → Server log (disk space, logo file path). |
| AI: "not configured" / errors | Settings → Claude AI enabled + key saved? Rate limits or outages are shown on the AI Inbox item; e-mails retry automatically. |
| Gmail: "invalid_grant" / stopped working | The token was revoked or the Google app was left in Testing (7-day expiry): publish the app, then Gmail Connection → Connect Gmail again. |
| "Changed by someone else" | Two PCs edited the same quotation. Reload it and re-apply your change. |
| Logs | Diagnostics → Server log, or `C:\ProgramData\TSQuotation\logs\server-YYYYMMDD.log`. API keys, tokens and passwords are never written to logs. |

---

## 12. For developers / evaluation without Tally

```bash
dotnet test                                                  # all tests
dotnet run --project tools/Tally.Simulator -- --port 9000    # simulated TallyPrime with 20,000 items
dotnet run --project src/Quotation.Server                    # http://localhost:5080
dotnet run --project src/Quotation.Desktop
./installer/publish.sh   (or  .\installer\publish.ps1 on Windows to also build the installers)
```
The simulator is also installed with the server at `C:\Program Files\TSQuotation\Server\tools\TallySimulator.exe`
for training/demo (point Settings → Tally to `http://localhost:9000` while TallyPrime is closed).
