# Quotation PDF — Build Specification (T.SAIFUDDIN & CO)

**Goal:** build an app that takes quotation data (seller, buyer, items, taxes, terms and conditions, sign-off) and exports a one-page A4 PDF that is **visually identical** to `reference/Quotation-100186.pdf` when given `sample_data.json`.

## Package contents

| Path | What it is |
|---|---|
| `SPEC.md` | This document: the source of truth for layout, maths and data. |
| `sample_data.json` | The data behind the reference PDF, including the expected totals. Use it as your test fixture. |
| `reference/Quotation-100186.pdf` | The target output. |
| `reference/Quotation-100186.png` | The same page at 150 dpi, for visual comparison. |
| `reference/make_quotation.py` | A working reference implementation (Python → HTML → headless Chromium → PDF). Running it reproduces the reference PDF exactly. |
| `reference/quotation.html` | The HTML that implementation generates, useful for reading the exact CSS. |
| `assets/logo-banner.svg` | The header logo (icon + lettering), fully vector, no font needed. |
| `assets/logo-square.svg` | The square logo used as the page watermark, fully vector. |
| `fonts/` | Ubuntu Regular, Ubuntu Bold and Montserrat (variable). All are free licences (Ubuntu Font Licence, SIL OFL). |

---

## 1. Rendering approach (strongly recommended)

Generate an **HTML page with absolutely positioned elements**, then print it to PDF with **headless Chromium**: Puppeteer (Node), Playwright (Node/Python), or a server-side equivalent. This is how the reference was made, and it is the only route that guarantees identical text metrics.

- Print with `format: "A4"`, `printBackground: true`, and all margins `0`. Also set CSS `@page { size: A4; margin: 0 }`.
- **Embed the fonts with `@font-face` from the `fonts/` folder.** Wait for `document.fonts.ready` before printing.
- **Fail loudly if a font does not load.** A silent fallback to a system font changes every text width and breaks right alignment.
- Text must use `white-space: nowrap; line-height: 1` (see §5).

**Alternatives:** You can use jsPDF, PDFKit, ReportLab or react-pdf. Use the same fonts and the coordinates in this spec, and draw text at the **baseline** values given in §5. Right-aligned text must be positioned by measuring its width with the embedded font. Expect sub-point drift.

---

## 2. Page and coordinate system

- **Page:** A4 portrait, 595.28 × 841.89 pt (210 × 297 mm). Chromium's A4 output is 595.92 × 842.88 pt, which is fine.
- **Units:** all numbers are **PDF points** (1 pt = 1/72 in).
- **Origin:** the **top-left** corner of the page. x increases to the right and y increases downward.
- **Alignment:**
  - "Left" means x is the left edge of the text.
  - "Right" means x is the **right edge** of the text; the text grows leftward.
- **Pages:** single page only (see §9).

## 3. Colours and fonts

| Token | Hex | Used for |
|---|---|---|
| INK | `#333333` | All regular body text |
| BLACK | `#000000` | Item-row values and totals |
| WHITE | `#FFFFFF` | Table header text |
| MAROON | `#800A08` | Corner shapes, banner lettering |
| LOGO_MAROON | `#8A0000` | Logo icon, watermark |
| DARK | `#1C201F` | Top bar, bottom bar |
| TABLE_HEAD | `#3C3D3A` | Table header band |
| RULE | `#ADADAD` | Row separator and footer rule |
| PAGE_NO | `#AAAAAA` | Page number |

**Fonts:**
- **Document text:** Ubuntu, Regular (400) or Bold (700).
- **Logo lettering:** Montserrat 700 (banner) and Montserrat 500 (watermark).
- **Avoiding a font dependency for the logo:** the SVGs in `assets/` already have the logo lettering converted to outlines. Use those, and the app needs Montserrat only if you rebuild the logo yourself.

## 4. Static artwork (identical on every quotation)

Draw these first, in this order. The order sets stacking: later items draw on top.

| # | Element | Geometry (pt) | Fill |
|---|---|---|---|
| 1 | Watermark | `assets/logo-square.svg` placed at x 83, y 212, size 430 × 393.4, **opacity 0.065** | — |
| 2 | Top bar | rect x 0, y 0, w = page width, h 17 | DARK |
| 3 | Top-right accent | polygon (494.5, 17) → (pageW, 17) → (pageW, 44) → (525, 44) | MAROON |
| 4 | Bottom-left accent | triangle (0, pageH − 28.7) → (0, pageH) → (82, pageH) | MAROON |
| 5 | Bottom bar | rect x 0, h 3.1, its bottom edge on the page bottom | DARK |
| 6 | Footer rule | rect x 39.6, y 791.3, w 527, h 0.7 | RULE |
| 7 | Table header band | rect x 45.6, y 328.8, w 515, h 24 | TABLE_HEAD |
| 8 | Header logo | `assets/logo-banner.svg` placed at x 14, y 46, size 264.15 × 70 | — |

Row separators (rect x 45.6, w 515, h 0.8, colour RULE) are dynamic; see §6.

## 5. Text elements

**Baseline rule:** the *Baseline y* column is where the text baseline sits.
- **HTML/CSS:** use a `div` with `position:absolute; line-height:1; white-space:nowrap` and set `top = baseline − 0.91 × font-size`. This is exactly what the reference code does: `top = T − 0.06 × size`, where `T = baseline − 0.85 × size`.
- **Chromium pixel-snapping:** baselines can shift by up to ±0.4 pt. That is expected.

The table below is the reference instance generated from `sample_data.json`. Rows marked *data* come from the input. The dynamic rows (items, totals, terms and conditions) move as described in §6.

| Text (reference value) | Align | x | Baseline y | Weight | Size (pt) | Colour |
|---|---|---|---|---|---|---|
| T.SAIFUDDIN & CO | left | 46.4 | 145.3 | Bold | 9 | `#333333` |
| No.72 N.R.Road | left | 46.4 | 155.0 | Regular | 8 | `#333333` |
| Bangalore | left | 46.4 | 164.2 | Regular | 8 | `#333333` |
| Karnataka 560002 | left | 46.4 | 173.4 | Regular | 8 | `#333333` |
| +917019740620 | left | 46.4 | 182.7 | Regular | 8 | `#333333` |
| +919739394657 | left | 46.4 | 191.9 | Regular | 8 | `#333333` |
| GSTIN 29AADFT8885H1ZF | left | 46.4 | 201.1 | Regular | 8 | `#333333` |
| Quotation/Proforma | right | 559.9 | 160.3 | Regular | 24 | `#333333` |
| Ref No. 100186 | right | 559.8 | 174.0 | Bold | 10 | `#333333` |
| Other Ref: R | right | 559.8 | 187.0 | Bold | 10 | `#333333` |
| Buyer/Consignee | left | 46.4 | 236.3 | Regular | 10 | `#333333` |
| BuyersHub | left | 46.4 | 246.8 | Bold | 9 | `#333333` |
| Plot 27 , PRT Colony , Karmanghat | left | 46.4 | 256.5 | Regular | 8 | `#333333` |
| Hyderabad Telangana 500079 | left | 46.4 | 265.7 | Regular | 8 | `#333333` |
| India | left | 46.4 | 274.9 | Regular | 8 | `#333333` |
| GSTIN 36AAZFB2304B1ZQ | left | 46.4 | 284.2 | Regular | 8 | `#333333` |
| +919182706754 | left | 46.4 | 293.4 | Regular | 8 | `#333333` |
| sales@buyershub.org.in | left | 46.4 | 302.6 | Regular | 8 | `#333333` |
| Date : | right | 460.3 | 288.7 | Regular | 10 | `#333333` |
| 02 Nov 2026 | right | 559.9 | 287.8 | Regular | 8 | `#333333` |
| S.No | left | 50.9 | 344.6 | Regular | 9 | `#FFFFFF` |
| Item & Description | left | 86.3 | 344.6 | Regular | 9 | `#FFFFFF` |
| Qty | right | 419.2 | 344.6 | Regular | 9 | `#FFFFFF` |
| Rate | right | 475.9 | 344.6 | Regular | 9 | `#FFFFFF` |
| Amount | right | 553.1 | 344.6 | Regular | 9 | `#FFFFFF` |
| 1 | left | 57.8 | 369.2 | Regular | 9 | `#000000` |
| Vernier Hook Depth Gauge | left | 86.3 | 369.2 | Regular | 9 | `#000000` |
| 5.00 | right | 419.2 | 369.2 | Regular | 9 | `#000000` |
| Nos | right | 419.2 | 377.7 | Regular | 7 | `#333333` |
| 12,011.00 | right | 475.9 | 369.2 | Regular | 9 | `#000000` |
| 60,055.00 | right | 553.1 | 369.2 | Regular | 9 | `#000000` |
| Make - Insize | left | 86.3 | 377.6 | Regular | 7 | `#333333` |
| Model : 1234-2001 | left | 86.3 | 389.9 | Regular | 7 | `#333333` |
| Sub Total | right | 451.8 | 416.5 | Regular | 9 | `#000000` |
| 60,055.00 | right | 553.1 | 416.5 | Regular | 9 | `#000000` |
| IGST18 (18%) | right | 451.8 | 441.9 | Regular | 9 | `#000000` |
| 10,809.90 | right | 553.1 | 441.9 | Regular | 9 | `#000000` |
| Total | right | 451.8 | 467.3 | Bold | 9 | `#000000` |
| ₹70,864.90 | right | 553.1 | 467.3 | Bold | 9 | `#000000` |
| Regards, | left | 46.4 | 758.3 | Regular | 8 | `#333333` |
| Name, | left | 46.4 | 768.8 | Regular | 8 | `#333333` |
| Tsaifuddin and Company | left | 46.4 | 779.3 | Regular | 8 | `#333333` |
| Terms & Conditions | left | 46.4 | 717.5 | Regular | 10 | `#333333` |
| 1. Payment: 100% Against Proforma | left | 46.4 | 729.8 | Regular | 8 | `#333333` |
| 2. Packing and forwarding charges extra | left | 46.4 | 740.3 | Regular | 8 | `#333333` |
| 1 | right | 562.8 | 806.1 | Regular | 6 | `#AAAAAA` |

**Which values are data-driven:**
- **Seller block:** the name (bold, 9 pt) and then each `seller.lines[]` entry at 8 pt, every **9.22 pt** down.
- **Title block:** the title (24 pt, right). `Ref No. {ref_no}` (bold 10, right). `Other Ref: {other_ref}` (bold 10, right) **is omitted entirely** when `other_ref` is empty.
- **Buyer block:**
  - `buyer.label` (10 pt) at baseline 236.3.
  - `buyer.name` (bold 9 pt) at baseline 246.8.
  - Each `buyer.lines[]` entry at 8 pt, starting at baseline 256.5 and every 9.22 pt down.
- **Date:** the label `Date :` is fixed text at 10 pt, right-aligned to x 460.3. The value is 8 pt, right-aligned to x 559.9, at baseline 287.8.
- **Terms (optional, hidden in the reference):** when `terms` is non-empty, draw `Terms :` (10 pt, right at x 460.3, baseline 307.7) and the value (8 pt, right at x 559.8, baseline 306.9). When `terms` is empty, draw nothing there; the reference has it empty.
- **Table header labels:** fixed text.
- **Item rows, totals, and terms and conditions:** see §6.
- **Page number:** always `1`.

## 6. Dynamic layout rules

Let `HEAD_TOP = 328.8` (top of the table header band), and give the first row `row_top = HEAD_TOP + 24 = 352.8`. Font sizes and colours for each element are in the §5 table. In the formulas below, `T` is the "top" value: baseline = `T + 0.85 × size`.

**For each item `n` (1-based):**
- **Main line,** at `T = row_top + 8.8`:
  - `n` at x 57.8 (left)
  - `name` at x 86.3 (left)
  - `qty` with 2 decimals, right-aligned at x 419.2
  - `rate` right-aligned at x 475.9
  - `amount` right-aligned at x 553.1
- **Unit:** 7 pt, right-aligned at x 419.2, `T = row_top + 18.9`.
- **Description lines:** `desc[i]` at 7 pt, x 86.3, `T = row_top + 18.9 + 12.3 × i`.
- **Row rule:**
  - `last = row_top + 18.9 + 12.3 × (max(len(desc), 1) − 1)`
  - `rule_y = last + 14.9`
  - Draw the row-separator rect at `rule_y`.
- **Next row:** `row_top = rule_y + 0.8`.

**Totals,** starting from `y = row_top + 9.2` after the last row. Labels are right-aligned at x 451.8 and values at x 553.1, all 9 pt black:
- `Sub Total` at `T = y`.
- **Each tax in order:** `y += 25.4`, then `{label}` and its amount.
- **Total:** `y += 25.4`, then `Total` and `₹{total}`, both **bold**. Only this value carries the `₹` sign.

**Terms and conditions and sign-off,** stacked upward from just above the footer rule. All lines are 8 pt Regular INK at x 46.4, with a line step of 10.5.
- Start with `bottom = 772.5`, which is the `T` of the very last line of the block.
- **Sign-off** (`sign_off[]`, e.g. "Regards," / "Name," / "Tsaifuddin and Company"):
  - `so_top = bottom − 10.5 × (count − 1)`.
  - Each line at `T = so_top + 10.5 × i`.
  - Then `bottom = so_top − 18` (the gap above the sign-off).
  - If `sign_off` is empty, instead set `bottom = bottom − 21`.
- **Terms and conditions** (`terms_and_conditions[]`):
  - `tc_top = bottom − 10.5 × (count − 1)`.
  - Heading `Terms & Conditions` (10 pt) at `T = tc_top − 14`.
  - Each line at `T = tc_top + 10.5 × i`.
  - Omit the heading and lines when the list is empty.
- Lines are printed exactly as given. The user types their own numbering and punctuation.

## 7. Calculations and number formatting

- **Arithmetic:** use **decimal** arithmetic, never binary floats. Use `Decimal` in Python, or `decimal.js`/`big.js` in JavaScript, or integer paise.
- **Amounts:**
  - `amount = qty × rate`
  - `sub_total = Σ amount`
  - For each tax: `tax = round_half_up(sub_total × percent / 100, 2)`
  - `total = sub_total + Σ tax`
- **Money format:**
  - Always 2 decimals, with **Indian digit grouping**: last 3 digits, then groups of 2. Examples: `999.00`, `12,011.00`, `1,23,456.70`, `1,23,45,678.91`.
  - Round half-up.
  - Qty is shown with 2 decimals (`5.00`).
- **GST convention:** the user supplies the taxes.
  - **Inter-state** (seller in Karnataka, buyer elsewhere): `[{"label":"IGST18 (18%)","percent":18}]`.
  - **Intra-state:** `[{"label":"CGST9 (9%)","percent":9},{"label":"SGST9 (9%)","percent":9}]`.
  - Optional enhancement: pick automatically by comparing the first two digits of the seller and buyer GSTINs (both `29` means intra-state).

**Reference check:** `5 × 12,011.00 = 60,055.00`; IGST 18% = `10,809.90`; total = `₹70,864.90`.

## 8. Data model

See `sample_data.json`. Fields:

```
title                 string    e.g. "Quotation/Proforma"
ref_no                string
other_ref             string    optional; empty → line hidden
date                  string    displayed as-is, e.g. "02 Nov 2026" (format DD Mon YYYY)
terms                 string    optional; empty → no Terms line under the date (empty in the reference)
seller.name           string
seller.lines[]        string[]  address + phone lines, last one is GSTIN
buyer.label           string    e.g. "Buyer/Consignee"
buyer.name            string
buyer.lines[]         string[]
items[]               { name, desc: string[], qty: decimal-string, unit, rate: decimal-string }
taxes[]               { label, percent: decimal-string }
terms_and_conditions  string[]  printed as typed (include your own "1.", "2.")
sign_off              string[]  printed as typed under the T&C, e.g. ["Regards,", "Name,", "Tsaifuddin and Company"]
```

Keep numbers as strings in transport so no precision is lost.

## 9. Known limits and suggested enhancements

- **Single page:**
  - About **5 items** fit when each has 2 description lines, or about 7 with 1 line, before the totals reach the terms-and-conditions block.
  - The app must either **block** more items or **implement pagination**: repeat the header band, carry totals to the last page, and set the page number to "n".
- **No text wrapping:**
  - Long item names, address lines or terms run off the edge.
  - Either validate length, or wrap item names within the description column (x 86.3 → ~360) and push the row down.
- **Logo lettering:** set in Montserrat as a close match to the client's original artwork. If the client supplies an official vector logo, replace the two SVGs and keep the same placement boxes.
- **"Terms" vs the terms-and-conditions block:** if the optional `Terms :` field is used, it can contradict the bottom block. That is the user's content; don't auto-edit it.

## 10. Acceptance test

1. Render `sample_data.json` with your app.
2. Rasterise your PDF and `reference/Quotation-100186.pdf` at 100–150 dpi, for example with `pdftoppm -r 150 -png`.
3. Overlay or diff them. Every element must coincide within about 1 pt, with the same fonts, colours and totals.
4. Also test:
   - 3 items with mixed description counts.
   - CGST + SGST taxes.
   - Empty `other_ref`.
   - Empty terms and conditions, empty `sign_off`, and a non-empty `terms`.
   - Large amounts (≥ 1 crore) for Indian grouping.
