using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SqlSense.Core.Export
{
    /// <summary>
    /// Writes a result table as a minimal Office Open XML workbook (.xlsx): one sheet, bold frozen
    /// header row with an AutoFilter, typed numbers/dates/booleans and inline strings.
    /// </summary>
    public static class XlsxWriter
    {
        /// <summary>Excel's per-cell text limit.</summary>
        public const int MaxCellText = 32767;

        /// <summary>Excel sheet row limit, header included.</summary>
        public const int MaxRows = 1048576;

        private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        // cellXfs indexes in styles.xml
        private const int StyleHeader = 1;
        private const int StyleDateTime = 2;
        private const int StyleDate = 3;

        public static void Write(Stream output, ResultTable table, string sheetName = "Result")
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                Part(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");
                Part(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Part(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"" + MainNs + "\" xmlns:r=\"" + RelNs + "\"><sheets>" +
                    "<sheet name=\"" + SecurityElementEscape(SheetName(sheetName)) + "\" sheetId=\"1\" r:id=\"rId1\"/>" +
                    "</sheets>" +
                    (table.Columns.Count > 0
                        ? "<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">'" +
                          SecurityElementEscape(SheetName(sheetName).Replace("'", "''")) + "'!" + FilterRange(table, absolute: true) + "</definedName></definedNames>"
                        : "") +
                    "</workbook>");
                Part(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");
                Part(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"" + MainNs + "\">" +
                    "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\\ hh:mm:ss\"/><numFmt numFmtId=\"165\" formatCode=\"yyyy\\-mm\\-dd\"/></numFmts>" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"4\">" +
                    "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                    "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                    "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                    "</cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                    "</styleSheet>");

                var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
                using (var stream = entry.Open())
                using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                {
                    WriteSheet(xml, table);
                }
            }
        }

        private static void WriteSheet(XmlWriter xml, ResultTable table)
        {
            var columnCount = table.Columns.Count;
            xml.WriteStartDocument(true);
            xml.WriteStartElement("worksheet", MainNs);
            xml.WriteAttributeString("xmlns", "r", null, RelNs);

            if (columnCount > 0)
            {
                xml.WriteStartElement("sheetViews");
                xml.WriteStartElement("sheetView");
                xml.WriteAttributeString("workbookViewId", "0");
                xml.WriteStartElement("pane");
                xml.WriteAttributeString("ySplit", "1");
                xml.WriteAttributeString("topLeftCell", "A2");
                xml.WriteAttributeString("activePane", "bottomLeft");
                xml.WriteAttributeString("state", "frozen");
                xml.WriteEndElement();
                xml.WriteEndElement();
                xml.WriteEndElement();

                xml.WriteStartElement("cols");
                for (var c = 0; c < columnCount; c++)
                {
                    xml.WriteStartElement("col");
                    var n = (c + 1).ToString(CultureInfo.InvariantCulture);
                    xml.WriteAttributeString("min", n);
                    xml.WriteAttributeString("max", n);
                    xml.WriteAttributeString("width", ColumnWidth(table, c).ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("customWidth", "1");
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }

            xml.WriteStartElement("sheetData");
            if (columnCount > 0)
            {
                xml.WriteStartElement("row");
                xml.WriteAttributeString("r", "1");
                for (var c = 0; c < columnCount; c++)
                    InlineString(xml, CellRef(c, 1), table.Columns[c].Name ?? string.Empty, StyleHeader);
                xml.WriteEndElement();
            }

            var rows = Math.Min(table.Rows.Count, MaxRows - 1);
            for (var r = 0; r < rows; r++)
            {
                var rowNumber = r + 2;
                var row = table.Rows[r];
                xml.WriteStartElement("row");
                xml.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
                for (var c = 0; c < columnCount && c < row.Length; c++)
                    Cell(xml, CellRef(c, rowNumber), row[c], table.Columns[c].SqlType);
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); // sheetData

            if (columnCount > 0)
            {
                xml.WriteStartElement("autoFilter");
                xml.WriteAttributeString("ref", FilterRange(table, absolute: false));
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); // worksheet
            xml.WriteEndDocument();
        }

        private static void Cell(XmlWriter xml, string cellRef, object raw, string sqlType)
        {
            var value = CellValue.Normalize(raw);
            switch (value)
            {
                case null:
                    return;
                case bool b:
                    Typed(xml, cellRef, "b", b ? "1" : "0", 0);
                    return;
                case DateTime dt when dt.Year >= 1900:
                    var dateOnly = CellValue.Is(sqlType, "date") || (string.IsNullOrEmpty(sqlType) && dt.TimeOfDay == TimeSpan.Zero);
                    Typed(xml, cellRef, null, dt.ToOADate().ToString("R", CultureInfo.InvariantCulture), dateOnly ? StyleDate : StyleDateTime);
                    return;
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case float _:
                case double _:
                    var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(d) || double.IsInfinity(d)) break;
                    Typed(xml, cellRef, null, d.ToString("R", CultureInfo.InvariantCulture), 0);
                    return;
                case long _:
                case ulong _:
                case decimal _:
                    // Excel keeps 15 significant digits; longer keys/amounts stay exact as text.
                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (SignificantDigits(text) > 15) break;
                    Typed(xml, cellRef, null, text, 0);
                    return;
                case string s when CellValue.IsNumericType(sqlType) && SignificantDigits(s) <= 15
                                   && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                                   && !double.IsNaN(parsed) && !double.IsInfinity(parsed):
                    Typed(xml, cellRef, null, parsed.ToString("R", CultureInfo.InvariantCulture), 0);
                    return;
            }
            InlineString(xml, cellRef, Text(value, sqlType), 0);
        }

        private static string Text(object value, string sqlType)
        {
            switch (value)
            {
                case byte[] bytes:
                    var sb = new StringBuilder("0x");
                    foreach (var b in bytes)
                    {
                        if (sb.Length + 2 > MaxCellText) break;
                        sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                    }
                    return sb.ToString();
                case DateTime dt:
                    return dt.ToString(CellValue.Is(sqlType, "date") ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
                case DateTimeOffset dto:
                    return dto.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF zzz", CultureInfo.InvariantCulture);
                case TimeSpan ts:
                    return ts.ToString(@"hh\:mm\:ss\.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.');
                case Guid g:
                    return g.ToString("D").ToUpperInvariant();
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static void Typed(XmlWriter xml, string cellRef, string type, string value, int style)
        {
            xml.WriteStartElement("c");
            xml.WriteAttributeString("r", cellRef);
            if (style != 0) xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            if (type != null) xml.WriteAttributeString("t", type);
            xml.WriteElementString("v", value);
            xml.WriteEndElement();
        }

        private static void InlineString(XmlWriter xml, string cellRef, string text, int style)
        {
            text = XmlSafe(text ?? string.Empty);
            if (text.Length > MaxCellText) text = text.Substring(0, MaxCellText);
            xml.WriteStartElement("c");
            xml.WriteAttributeString("r", cellRef);
            if (style != 0) xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("t", "inlineStr");
            xml.WriteStartElement("is");
            xml.WriteStartElement("t");
            if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[text.Length - 1])))
                xml.WriteAttributeString("xml", "space", null, "preserve");
            xml.WriteString(text);
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        /// <summary>Drops characters XML 1.0 can't carry (control chars other than tab/CR/LF, lone surrogates).</summary>
        private static string XmlSafe(string s)
        {
            StringBuilder sb = null;
            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                bool ok;
                if (char.IsHighSurrogate(ch)) ok = i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
                else if (char.IsLowSurrogate(ch)) ok = i > 0 && char.IsHighSurrogate(s[i - 1]);
                else ok = ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch != 0xFFFE && ch != 0xFFFF);
                if (ok)
                {
                    sb?.Append(ch);
                }
                else if (sb == null)
                {
                    sb = new StringBuilder(s.Length).Append(s, 0, i);
                }
            }
            return sb?.ToString() ?? s;
        }

        private static int SignificantDigits(string number)
        {
            var count = 0;
            var leading = true;
            foreach (var ch in number)
            {
                if (ch == 'e' || ch == 'E') break;
                if (!char.IsDigit(ch)) continue;
                if (leading && ch == '0') continue;
                leading = false;
                count++;
            }
            return count;
        }

        private static double ColumnWidth(ResultTable table, int c)
        {
            var max = (table.Columns[c].Name ?? string.Empty).Length + 2;
            var sample = Math.Min(table.Rows.Count, 200);
            for (var r = 0; r < sample; r++)
            {
                var row = table.Rows[r];
                if (c >= row.Length) continue;
                var v = CellValue.Normalize(row[c]);
                if (v == null) continue;
                var len = v is DateTime ? 19 : Text(v, table.Columns[c].SqlType).Length;
                if (len > max) max = len;
            }
            return Math.Max(8, Math.Min(max + 1, 60));
        }

        private static string FilterRange(ResultTable table, bool absolute)
        {
            var lastRow = Math.Min(table.Rows.Count, MaxRows - 1) + 1;
            var lastCol = ColumnName(table.Columns.Count - 1);
            return absolute
                ? "$A$1:$" + lastCol + "$" + lastRow.ToString(CultureInfo.InvariantCulture)
                : "A1:" + lastCol + lastRow.ToString(CultureInfo.InvariantCulture);
        }

        public static string CellRef(int column, int row) => ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);

        /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
        public static string ColumnName(int index)
        {
            var name = string.Empty;
            for (var n = index + 1; n > 0; n = (n - 1) / 26)
                name = (char)('A' + (n - 1) % 26) + name;
            return name;
        }

        /// <summary>Sheet names: max 31 chars, none of : \ / ? * [ ].</summary>
        private static string SheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Result";
            var sb = new StringBuilder();
            foreach (var ch in name)
                sb.Append(":\\/?*[]".IndexOf(ch) >= 0 ? '_' : ch);
            var s = sb.ToString().Trim('\'');
            if (s.Length > 31) s = s.Substring(0, 31);
            return s.Length == 0 ? "Result" : s;
        }

        private static string SecurityElementEscape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static void Part(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                w.Write(content);
        }
    }
}
