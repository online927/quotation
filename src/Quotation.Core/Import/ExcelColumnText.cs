namespace Quotation.Core.Import;

/// <summary>
/// Round 39 (DX-1): the cells of one column copied from Excel as text. Excel puts each cell on its own line and wraps a
/// cell that has Alt+Enter lines in quotes ("line 1\nline 2", a quote inside doubled ""), so such a cell stays one cell.
/// Only the first column of each row is kept.
/// </summary>
public static class ExcelColumnText
{
    public static List<string> Cells(string? text)
    {
        var cells = new List<string>();
        if (string.IsNullOrEmpty(text)) return cells;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.EndsWith('\n')) text = text[..^1];
        var cell = new System.Text.StringBuilder();
        var i = 0;
        var atStart = true;
        var quoted = false;
        var skipToRowEnd = false;
        while (i < text.Length)
        {
            var c = text[i];
            if (skipToRowEnd)
            {
                if (c == '\n') { skipToRowEnd = false; atStart = true; }
                else if (c == '"' && quoted) { /* inside a quoted later column */ }
                i++;
                continue;
            }
            if (atStart && c == '"')
            {
                quoted = true;
                atStart = false;
                i++;
                continue;
            }
            atStart = false;
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i += 2; continue; }
                if (c == '"') { quoted = false; i++; continue; }
                cell.Append(c);
                i++;
                continue;
            }
            if (c == '\t')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                skipToRowEnd = true;
                i++;
                continue;
            }
            if (c == '\n')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                atStart = true;
                i++;
                continue;
            }
            cell.Append(c);
            i++;
        }
        if (!skipToRowEnd || cell.Length > 0) cells.Add(cell.ToString());
        return cells.Select(c => c.Trim()).ToList();
    }
}
