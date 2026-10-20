using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SqlSense.Core.Analysis;
using SqlSense.Core.Lexing;

namespace SqlSense.Core.Export
{
    /// <summary>Turns a result grid into INSERT … VALUES statements.</summary>
    public static class InsertScriptGenerator
    {
        /// <summary>SQL Server's limit on row constructors in one VALUES clause.</summary>
        public const int MaxRowsPerInsert = 1000;

        public const string DefaultTableName = "[dbo].[TableName]";

        public static string Generate(ResultTable table, string targetTable, int rowsPerInsert = MaxRowsPerInsert)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (string.IsNullOrWhiteSpace(targetTable)) targetTable = DefaultTableName;
            rowsPerInsert = Math.Max(1, Math.Min(rowsPerInsert, MaxRowsPerInsert));

            // rowversion/timestamp columns cannot be inserted explicitly.
            var columns = Enumerable.Range(0, table.Columns.Count)
                .Where(i => !CellValue.Is(table.Columns[i].SqlType, "timestamp", "rowversion"))
                .ToList();
            var sb = new StringBuilder();
            if (columns.Count == 0 || table.Rows.Count == 0)
            {
                sb.Append("-- No rows to script.").AppendLine();
                return sb.ToString();
            }

            var header = "INSERT INTO " + targetTable + " (" + string.Join(", ", columns.Select(i => QuoteName(ColumnName(table, i)))) + ")";
            for (var r = 0; r < table.Rows.Count; r++)
            {
                if (r % rowsPerInsert == 0)
                {
                    if (r > 0) sb.Append(';').AppendLine().AppendLine();
                    sb.Append(header).AppendLine().Append("VALUES").AppendLine();
                }
                else
                {
                    sb.Append(',').AppendLine();
                }
                var row = table.Rows[r];
                sb.Append("    (");
                for (var c = 0; c < columns.Count; c++)
                {
                    if (c > 0) sb.Append(", ");
                    var i = columns[c];
                    sb.Append(Literal(i < row.Length ? row[i] : null, table.Columns[i].SqlType));
                }
                sb.Append(')');
            }
            sb.Append(';').AppendLine();
            return sb.ToString();
        }

        private static string ColumnName(ResultTable table, int index)
        {
            var name = table.Columns[index].Name;
            return string.IsNullOrWhiteSpace(name) || name == "(No column name)" ? "Column" + (index + 1) : name;
        }

        public static string QuoteName(string name) => "[" + name.Replace("]", "]]") + "]";

        /// <summary>A T-SQL literal for a cell value; <paramref name="sqlType"/> refines strings and dates.</summary>
        public static string Literal(object value, string sqlType = null)
        {
            value = CellValue.Normalize(value);
            switch (value)
            {
                case null:
                    return "NULL";
                case string s:
                    return StringLiteral(s, sqlType);
                case char ch:
                    return StringLiteral(ch.ToString(), sqlType);
                case bool b:
                    return b ? "1" : "0";
                case byte[] bytes:
                    return HexLiteral(bytes);
                case Guid g:
                    return "'" + g.ToString("D").ToUpperInvariant() + "'";
                case DateTime dt:
                    return "'" + FormatDateTime(dt, sqlType) + "'";
                case DateTimeOffset dto:
                    return "'" + dto.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture) + "'";
                case TimeSpan ts:
                    return "'" + ts.ToString(@"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture) + "'";
                case float f:
                    return FloatLiteral(f.ToString("R", CultureInfo.InvariantCulture));
                case double d:
                    return FloatLiteral(d.ToString("R", CultureInfo.InvariantCulture));
                case decimal m:
                    return m.ToString(CultureInfo.InvariantCulture);
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                default:
                    // UDTs (geography, geometry, hierarchyid) convert implicitly from their text form.
                    return "N'" + Escape(Convert.ToString(value, CultureInfo.InvariantCulture)) + "'";
            }
        }

        private static string StringLiteral(string s, string sqlType)
        {
            if (CellValue.IsNumericType(sqlType))
            {
                var t = s.Trim();
                if (CellValue.Is(sqlType, "bit"))
                {
                    if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) return "1";
                    if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) return "0";
                }
                if (IsNumber(t)) return t;
            }
            if (CellValue.IsBinaryType(sqlType) && IsHex(s.Trim())) return s.Trim();
            if (CellValue.IsAnsiTextType(sqlType)) return "'" + Escape(s) + "'";
            return "N'" + Escape(s) + "'";
        }

        private static string FormatDateTime(DateTime dt, string sqlType)
        {
            if (CellValue.Is(sqlType, "date")) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (CellValue.Is(sqlType, "smalldatetime")) return dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            if (CellValue.Is(sqlType, "datetime")) return dt.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(sqlType) && dt.TimeOfDay == TimeSpan.Zero) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return dt.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        }

        private static string FloatLiteral(string text) =>
            // NaN/Infinity can't come from SQL Server, but keep the script valid if they do.
            IsNumber(text) ? text : "NULL";

        private static string HexLiteral(byte[] bytes)
        {
            var sb = new StringBuilder(2 + bytes.Length * 2).Append("0x");
            foreach (var b in bytes) sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string Escape(string s) => s.Replace("'", "''");

        private static bool IsNumber(string s) =>
            s.Length > 0 && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) && !double.IsInfinity(d);

        private static bool IsHex(string s)
        {
            if (s.Length < 2 || s[0] != '0' || (s[1] != 'x' && s[1] != 'X')) return false;
            for (var i = 2; i < s.Length; i++)
                if (!Uri.IsHexDigit(s[i])) return false;
            return true;
        }

        /// <summary>
        /// Best guess at the table a result set came from: the first base table in the FROM of the
        /// <paramref name="resultIndex"/>-th SELECT of <paramref name="script"/> (the last SELECT when there
        /// are fewer). Returns null when there is none.
        /// </summary>
        public static string GuessTableName(string script, int resultIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(script)) return null;
            var sig = ScriptText.Significant(SqlLexer.Tokenize(script));
            var selects = new List<TableRef>();
            foreach (var (start, end) in StatementAnalysis.SplitStatements(sig))
            {
                var head = sig[start];
                if (head.Is("WITH")) head = FirstSelectAtDepth0(sig, start, end) ?? head;
                if (!head.Is("SELECT")) continue;
                // SELECT … INTO returns no result set.
                if (StatementAnalysis.FindWordAtDepth(sig, start, end, "INTO") >= 0) continue;
                var from = StatementAnalysis.FindWordAtDepth(sig, start, end, "FROM");
                var refs = from < 0 ? new List<TableRef>() : StatementAnalysis.CollectTableRefs(sig, from, end);
                var ctes = new HashSet<string>(StatementAnalysis.CollectCtes(sig, start, end).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                // Only the driving (first) source names the result; a CTE or derived table there means "unknown".
                var first = refs.FirstOrDefault();
                var isBase = first != null && first.Source != TableRefSource.Derived && !string.IsNullOrEmpty(first.Name)
                             && !(first.Schema == null && ctes.Contains(first.Name));
                selects.Add(isBase ? first : null);
            }
            if (selects.Count == 0) return null;
            var t = selects[Math.Max(0, Math.Min(resultIndex, selects.Count - 1))];
            if (t == null) return null;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(t.Database)) parts.Add(t.Database);
            if (!string.IsNullOrEmpty(t.Schema) || parts.Count > 0) parts.Add(string.IsNullOrEmpty(t.Schema) ? "dbo" : t.Schema);
            else if (!t.Name.StartsWith("#", StringComparison.Ordinal) && !t.Name.StartsWith("@", StringComparison.Ordinal)) parts.Add("dbo");
            parts.Add(t.Name);
            return string.Join(".", parts.Select(p => p.StartsWith("@", StringComparison.Ordinal) ? p : QuoteName(p)));
        }

        private static SqlToken FirstSelectAtDepth0(List<SqlToken> sig, int start, int end)
        {
            var i = StatementAnalysis.FindWordAtDepth(sig, start, end, "SELECT");
            return i >= 0 ? sig[i] : null;
        }
    }
}
