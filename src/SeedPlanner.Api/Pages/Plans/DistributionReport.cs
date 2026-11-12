using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SeedPlanner.Api.Pages.Plans;

/// <summary>A table of the report: a title, the column headings and the rows (all text).</summary>
public record ReportTable(string Title, string[] Columns, List<string[]> Rows);

/// <summary>The report of a distribution: what the user chose to export.</summary>
public record ReportDocument(string Title, string Subtitle, List<ReportTable> Tables);

/// <summary>Writes a <see cref="ReportDocument"/> as CSV (for Excel) or as an Excel file (.xlsx), without extra packages.</summary>
public static class ReportWriters
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // ---------------------------------------------------------------- CSV

    /// <summary>Semicolon separated with a byte order mark, so that German Excel opens it with the right characters.</summary>
    public static byte[] Csv(ReportDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append(Line(new[] { doc.Title, doc.Subtitle }));
        foreach (var table in doc.Tables)
        {
            sb.Append("\r\n");
            sb.Append(Line(new[] { table.Title }));
            sb.Append(Line(table.Columns));
            foreach (var row in table.Rows) sb.Append(Line(row));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Line(IEnumerable<string> cells) => string.Join(';', cells.Select(Cell)) + "\r\n";

    // Quote when needed; a text that starts like a formula gets a leading apostrophe so Excel does not run it
    private static string Cell(string value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !double.TryParse(value, out _)) value = "'" + value;
        return value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    // ---------------------------------------------------------------- XLSX

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>One sheet per table with a bold heading row.</summary>
    public static byte[] Xlsx(ReportDocument doc)
    {
        var tables = doc.Tables.Where(t => t.Rows.Count > 0).ToList();
        if (tables.Count == 0) tables.Add(new ReportTable(doc.Title, [doc.Title], []));
        var names = SheetNames(tables);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                string.Concat(tables.Select((_, i) =>
                    $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) +
                "</Types>");

            Add(zip, "_rels/.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                $"<Relationship Id=\"rId1\" Type=\"{Rel}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");

            Add(zip, "xl/workbook.xml",
                $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\"><sheets>" +
                string.Concat(names.Select((n, i) => $"<sheet name=\"{Esc(n)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) +
                "</sheets></workbook>");

            Add(zip, "xl/_rels/workbook.xml.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                string.Concat(tables.Select((_, i) =>
                    $"<Relationship Id=\"rId{i + 1}\" Type=\"{Rel}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) +
                $"<Relationship Id=\"rId{tables.Count + 1}\" Type=\"{Rel}/styles\" Target=\"styles.xml\"/></Relationships>");

            Add(zip, "xl/styles.xml",
                $"<styleSheet xmlns=\"{Main}\">" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");

            for (var i = 0; i < tables.Count; i++)
                Add(zip, $"xl/worksheets/sheet{i + 1}.xml", Sheet(tables[i]));
        }
        return stream.ToArray();
    }

    private static string Sheet(ReportTable table)
    {
        var sb = new StringBuilder();
        sb.Append($"<worksheet xmlns=\"{Main}\"><cols>");
        for (var c = 0; c < table.Columns.Length; c++)
        {
            var longest = new[] { table.Columns[c] }.Concat(table.Rows.Select(r => c < r.Length ? r[c] : "")).Max(s => (s ?? "").Length);
            sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{Math.Min(60, longest + 3)}\" customWidth=\"1\"/>");
        }
        sb.Append("</cols><sheetData>");
        AppendRow(sb, 1, table.Columns, bold: true);
        for (var r = 0; r < table.Rows.Count; r++) AppendRow(sb, r + 2, table.Rows[r], bold: false);
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int row, string[] cells, bool bold)
    {
        sb.Append($"<row r=\"{row}\">");
        for (var c = 0; c < cells.Length; c++)
        {
            var text = cells[c] ?? "";
            var reference = Column(c) + row;
            var style = bold ? " s=\"1\"" : "";
            // Whole numbers are numbers (for sums); everything else is text
            if (!bold && int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && text == n.ToString())
                sb.Append($"<c r=\"{reference}\"{style}><v>{n}</v></c>");
            else
                sb.Append($"<c r=\"{reference}\"{style} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(text)}</t></is></c>");
        }
        sb.Append("</row>");
    }

    // A, B, ... Z, AA, AB, ...
    private static string Column(int index)
    {
        var name = "";
        for (var i = index + 1; i > 0; i = (i - 1) / 26) name = (char)('A' + (i - 1) % 26) + name;
        return name;
    }

    // At most 31 characters, without []:*?/\ and unique
    private static List<string> SheetNames(List<ReportTable> tables)
    {
        var names = new List<string>();
        foreach (var t in tables)
        {
            var clean = new string(t.Title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
            if (clean.Length == 0) clean = "Sheet";
            if (clean.Length > 31) clean = clean[..31];
            var name = clean;
            for (var n = 2; names.Contains(name, StringComparer.OrdinalIgnoreCase); n++)
            {
                var suffix = $" {n}";
                name = clean[..Math.Min(clean.Length, 31 - suffix.Length)] + suffix;
            }
            names.Add(name);
        }
        return names;
    }

    // Removes characters XML does not allow, then escapes the rest
    private static string Esc(string value)
    {
        var clean = new string((value ?? "").Where(XmlConvert.IsXmlChar).ToArray());
        return System.Security.SecurityElement.Escape(clean) ?? "";
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml);
    }
}
