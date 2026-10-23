using System;
using System.Collections.Generic;
using System.Linq;
using SqlSense.Core.Language;
using SqlSense.Core.Lexing;
using SqlSense.Core.Schema;

namespace SqlSense.Core.Analysis
{
    public enum TableRefSource
    {
        Database,
        Cte,
        LocalTable,
        Derived,
    }

    /// <summary>A table-like source referenced by the current statement (FROM, JOIN, UPDATE, INTO …).</summary>
    public sealed class TableRef
    {
        /// <summary>Linked server of a four-part name.</summary>
        public string Server { get; set; }
        public string Database { get; set; }
        public string Schema { get; set; }
        public string Name { get; set; }
        public string Alias { get; set; }
        public TableRefSource Source { get; set; }
        public DbObject Object { get; set; }
        public List<string> LocalColumns { get; set; }
        public int TokenIndex { get; set; }

        /// <summary>The name a column should be qualified with: alias if any, otherwise the table name.</summary>
        public string Qualifier => string.IsNullOrEmpty(Alias) ? Name : Alias;

        public IEnumerable<string> ColumnNames =>
            Object != null ? Object.Columns.Select(c => c.Name) : (IEnumerable<string>)LocalColumns ?? Array.Empty<string>();

        public bool Matches(string identifier) =>
            string.Equals(Alias, identifier, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrEmpty(Alias) && string.Equals(Name, identifier, StringComparison.OrdinalIgnoreCase));

        public override string ToString() => $"{Schema}.{Name} {Alias}".Trim();
    }

    public sealed class LocalTable
    {
        public LocalTable(string name, List<string> columns)
        {
            Name = name;
            Columns = columns;
        }

        public string Name { get; }
        public List<string> Columns { get; }
    }

    public sealed class VariableInfo
    {
        public VariableInfo(string name, string dataType)
        {
            Name = name;
            DataType = dataType;
        }

        public string Name { get; }
        public string DataType { get; set; }
    }

    /// <summary>Statement-level structure recovered from tokens; tolerant of incomplete SQL.</summary>
    public static class StatementAnalysis
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        private static readonly HashSet<string> DropObjectWords = new HashSet<string>(Ci)
        {
            "TABLE", "VIEW", "PROCEDURE", "PROC", "FUNCTION", "INDEX", "SCHEMA", "TRIGGER", "DATABASE", "TYPE",
            "SEQUENCE", "SYNONYM", "COLUMN", "CONSTRAINT", "USER", "ROLE", "LOGIN", "ASSEMBLY", "STATISTICS",
        };

        private static readonly HashSet<string> NotAnAlias = new HashSet<string>(Ci)
        {
            "APPLY", "WINDOW", "OUTPUT", "OPTION", "TABLESAMPLE", "FOR", "GO", "MATCHED",
        };

        // ---------- statement segmentation ----------

        /// <summary>
        /// Splits significant tokens into statements, returning [start, end] index pairs (inclusive).
        /// Statements end at ';' or where a new statement keyword begins at nesting depth zero.
        /// </summary>
        public static List<(int Start, int End)> SplitStatements(List<SqlToken> sig)
        {
            var result = new List<(int, int)>();
            var segStart = 0;
            var depth = 0;
            var caseDepth = 0;
            for (var i = 0; i < sig.Count; i++)
            {
                var t = sig[i];
                if (t.Text == "(") { depth++; continue; }
                if (t.Text == ")") { depth = Math.Max(0, depth - 1); continue; }
                if (depth > 0) continue;
                if (t.Text == ";")
                {
                    result.Add((segStart, i));
                    segStart = i + 1;
                    caseDepth = 0;
                    continue;
                }
                if (!t.IsWord) continue;
                var u = t.Upper;
                if (u == "CASE") { caseDepth++; continue; }
                if (u == "END" && caseDepth > 0) { caseDepth--; continue; }
                if (i > segStart && SqlVocabulary.StatementStarters.Contains(u) && !IsContinuation(sig, segStart, i))
                {
                    result.Add((segStart, i - 1));
                    segStart = i;
                    caseDepth = 0;
                }
            }
            if (segStart < sig.Count) result.Add((segStart, sig.Count - 1));
            return result;
        }

        private static bool IsContinuation(List<SqlToken> sig, int segStart, int i)
        {
            var u = sig[i].Upper;
            var prev = sig[i - 1];
            var prevU = prev.IsWord ? prev.Upper : prev.Text;
            var first = sig[segStart].IsWord ? sig[segStart].Upper : sig[segStart].Text;
            var next = i + 1 < sig.Count ? sig[i + 1] : null;

            switch (u)
            {
                case "SELECT":
                    return prevU == "UNION" || prevU == "ALL" || prevU == "EXCEPT" || prevU == "INTERSECT"
                           || prevU == "AS" || prevU == "FOR" || prevU == "DISTINCT"
                           || first == "INSERT" || first == "WITH" || first == "CREATE" || first == "ALTER"
                           || (first == "GRANT" || first == "DENY" || first == "REVOKE") && (prevU == first || prevU == ",");
                case "SET":
                    return first == "UPDATE" || first == "MERGE" || first == "ALTER" || prevU == "DELETE" || prevU == "UPDATE"
                           || (first == "WITH" && ContainsWord(sig, segStart, i, "UPDATE"));
                case "UPDATE":
                case "INSERT":
                case "DELETE":
                    return prevU == "THEN" || prevU == "FOR" || prevU == "ON" || prevU == "AFTER" || prevU == "OF"
                           || prevU == "," || prevU == "NOT" || (first == "WITH" && prevU == ")")
                           || first == "CREATE" || first == "ALTER" || first == "GRANT" || first == "DENY" || first == "REVOKE";
                case "MERGE":
                    return first == "WITH" && prevU == ")";
                case "WITH":
                    // A CTE starts a statement; table hints and options (WITH (NOLOCK), WITH SCHEMABINDING) don't.
                    if (next != null && next.Text == "(") return true;
                    return first == "CREATE" || first == "ALTER" || first == "SELECT" || first == "INSERT"
                           || first == "UPDATE" || first == "DELETE" || first == "MERGE"
                           || first == "DECLARE" || first == "RAISERROR" || first == "THROW"
                           || first == "BACKUP" || first == "RESTORE" || first == "DBCC";
                case "EXEC":
                case "EXECUTE":
                    return first == "INSERT" || prevU == "WITH" || prevU == "," || first == "GRANT" || first == "DENY" || first == "REVOKE";
                case "FETCH":
                    return prevU == "ROWS" || prevU == "ROW";
                case "IF":
                    return DropObjectWords.Contains(prevU);
                case "ALTER":
                    return prevU == "OR";
                case "OPEN":
                case "CLOSE":
                    return prevU == "=";
                case "DROP":
                    // ALTER [SERVER] ROLE r DROP MEMBER m
                    return first == "ALTER" && next != null && next.Is("MEMBER");
                case "RETURN":
                case "DECLARE":
                case "CREATE":
                    return false;
                default:
                    return false;
            }
        }

        private static bool ContainsWord(List<SqlToken> sig, int from, int to, string upper)
        {
            for (var k = from; k < to; k++)
                if (sig[k].Is(upper)) return true;
            return false;
        }

        // ---------- table references ----------

        /// <summary>Collects table sources referenced in sig[start..end].</summary>
        public static List<TableRef> CollectTableRefs(List<SqlToken> sig, int start, int end)
        {
            var refs = new List<TableRef>();
            for (var i = start; i <= end && i < sig.Count; i++)
            {
                var t = sig[i];
                if (!t.IsWord) continue;
                var u = t.Upper;
                var isFromLike = u == "FROM" || u == "JOIN" || u == "APPLY" || u == "USING";
                var isTarget = (u == "UPDATE" && IsStatementHead(sig, start, i))
                               || u == "INTO" || u == "MERGE"
                               || (u == "INSERT" && i + 1 <= end && !sig[i + 1].Is("INTO"))
                               || (u == "DELETE" && i + 1 <= end && !sig[i + 1].Is("FROM"))
                               || (u == "TABLE" && i > start && (sig[i - 1].Is("TRUNCATE") || sig[i - 1].Is("ALTER") || sig[i - 1].Is("DROP")));
                if (!isFromLike && !isTarget) continue;
                if (u == "UPDATE" && i + 1 <= end && sig[i + 1].Is("SET")) continue;

                var j = i + 1;
                while (j <= end)
                {
                    var allowAlias = !(u == "TABLE" || u == "INSERT" || (u == "INTO" && IsInsertInto(sig, i)));
                    var parsed = ParseTableRef(sig, j, end, allowAlias, out var next);
                    if (parsed == null) break;
                    refs.Add(parsed);
                    j = next;
                    // FROM a, b, c — comma-separated sources only in FROM.
                    if (u == "FROM" && j <= end && sig[j].Text == ",") { j++; continue; }
                    break;
                }
            }
            return refs;
        }

        private static bool IsStatementHead(List<SqlToken> sig, int start, int i)
        {
            if (i == start) return true;
            // Segmentation already split real statements, so a statement-level UPDATE is either
            // first in its segment or follows a CTE.
            return sig[i - 1].Text == ")" && sig[start].Is("WITH");
        }

        private static bool IsInsertInto(List<SqlToken> sig, int intoIndex) =>
            intoIndex > 0 && sig[intoIndex - 1].Is("INSERT");

        private static TableRef ParseTableRef(List<SqlToken> sig, int j, int end, bool allowAlias, out int next)
        {
            next = j;
            if (j > end) return null;
            var t = sig[j];
            TableRef r;
            if (t.Text == "(")
            {
                var close = MatchingParen(sig, j);
                if (close < 0 || close > end) return null;
                r = new TableRef { Source = TableRefSource.Derived, Name = null, TokenIndex = j };
                var sel = FindWordAtDepth(sig, j + 1, close - 1, "SELECT");
                r.LocalColumns = sel >= 0 ? SelectListNames(sig, sel + 1, close - 1) : new List<string>();
                next = close + 1;
            }
            else if (t.IsName || t.Kind == TokenKind.Variable)
            {
                var parts = new List<string> { t.Unquoted };
                var k = j + 1;
                while (k + 1 <= end && sig[k].Text == ".")
                {
                    if (sig[k + 1].Text == ".") { parts.Add(string.Empty); k++; continue; }
                    if (!sig[k + 1].IsName) break;
                    parts.Add(sig[k + 1].Unquoted);
                    k += 2;
                }
                r = new TableRef { TokenIndex = j, Source = TableRefSource.Database };
                r.Name = parts[parts.Count - 1];
                if (parts.Count >= 2) r.Schema = NullIfEmpty(parts[parts.Count - 2]);
                if (parts.Count >= 3) r.Database = NullIfEmpty(parts[parts.Count - 3]);
                if (parts.Count >= 4) r.Server = NullIfEmpty(parts[parts.Count - 4]);
                if (k <= end && sig[k].Text == "(")
                {
                    var close = MatchingParen(sig, k);
                    k = close < 0 ? end + 1 : close + 1;
                }
                next = k;
            }
            else
            {
                return null;
            }

            if (!allowAlias) return r;
            var a = next;
            if (a <= end && sig[a].Is("AS")) a++;
            if (a <= end && sig[a].IsName && !NotAnAlias.Contains(sig[a].Text))
            {
                r.Alias = sig[a].Unquoted;
                next = a + 1;
            }
            return r;
        }

        /// <summary>CTE definitions at the head of a statement: WITH a (cols) AS (...), b AS (...).</summary>
        public static List<LocalTable> CollectCtes(List<SqlToken> sig, int start, int end)
        {
            var ctes = new List<LocalTable>();
            if (start > end || !sig[start].Is("WITH")) return ctes;
            var i = start + 1;
            while (i <= end && sig[i].IsName)
            {
                var name = sig[i].Unquoted;
                i++;
                List<string> cols = null;
                if (i <= end && sig[i].Text == "(")
                {
                    var close = MatchingParen(sig, i);
                    if (close < 0) break;
                    cols = new List<string>();
                    for (var k = i + 1; k < close; k++) if (sig[k].IsName) cols.Add(sig[k].Unquoted);
                    i = close + 1;
                }
                if (i > end || !sig[i].Is("AS")) break;
                i++;
                if (i > end || sig[i].Text != "(") break;
                var bodyClose = MatchingParen(sig, i);
                var bodyEnd = bodyClose < 0 ? end : bodyClose - 1;
                if (cols == null)
                {
                    var sel = FindWordAtDepth(sig, i + 1, bodyEnd, "SELECT");
                    cols = sel >= 0 ? SelectListNames(sig, sel + 1, bodyEnd) : new List<string>();
                }
                ctes.Add(new LocalTable(name, cols));
                if (bodyClose < 0) break;
                i = bodyClose + 1;
                if (i <= end && sig[i].Text == ",") i++;
                else break;
            }
            return ctes;
        }

        /// <summary>Temp tables and table variables defined in the batch before <paramref name="limit"/>.</summary>
        public static List<LocalTable> CollectLocalTables(List<SqlToken> sig, int limit)
        {
            var tables = new List<LocalTable>();
            for (var i = 0; i < sig.Count && i < limit; i++)
            {
                // CREATE TABLE name ( ... )
                if (sig[i].Is("CREATE") && i + 2 < sig.Count && sig[i + 1].Is("TABLE"))
                {
                    var nameIdx = i + 2;
                    var k = nameIdx;
                    while (k + 2 < sig.Count && sig[k + 1].Text == "." && sig[k + 2].IsName) k += 2;
                    if (k + 1 < sig.Count && sig[k + 1].Text == "(")
                        tables.Add(new LocalTable(sig[k].Unquoted, ColumnDefinitions(sig, k + 1)));
                }
                // DECLARE @t TABLE ( ... )
                else if (sig[i].Kind == TokenKind.Variable && i + 2 < sig.Count && sig[i + 1].Is("TABLE") && sig[i + 2].Text == "(")
                {
                    tables.Add(new LocalTable(sig[i].Text, ColumnDefinitions(sig, i + 2)));
                }
                // SELECT ... INTO #t FROM
                else if (sig[i].Is("INTO") && i + 1 < sig.Count && sig[i + 1].Text.StartsWith("#", StringComparison.Ordinal))
                {
                    var sel = i - 1;
                    var depth = 0;
                    while (sel >= 0)
                    {
                        if (sig[sel].Text == ")") depth++;
                        else if (sig[sel].Text == "(") depth--;
                        else if (depth == 0 && sig[sel].Is("SELECT")) break;
                        sel--;
                    }
                    if (sel >= 0) tables.Add(new LocalTable(sig[i + 1].Text, SelectListNames(sig, sel + 1, i - 1)));
                }
            }
            return tables;
        }

        private static List<string> ColumnDefinitions(List<SqlToken> sig, int openParen)
        {
            var cols = new List<string>();
            var close = MatchingParen(sig, openParen);
            if (close < 0) close = sig.Count;
            var itemStart = true;
            var depth = 0;
            for (var k = openParen + 1; k < close; k++)
            {
                var t = sig[k];
                if (t.Text == "(") depth++;
                else if (t.Text == ")") depth--;
                else if (depth == 0 && t.Text == ",") { itemStart = true; continue; }
                if (itemStart && depth == 0)
                {
                    itemStart = false;
                    if (t.IsName && !(t.Is("CONSTRAINT") || t.Is("PRIMARY") || t.Is("INDEX") || t.Is("UNIQUE")
                                      || t.Is("FOREIGN") || t.Is("CHECK") || t.Is("PERIOD")))
                        cols.Add(t.Unquoted);
                }
            }
            return cols;
        }

        /// <summary>Output column names of a select list spanning sig[from..to] (stops at FROM/INTO at depth 0).</summary>
        public static List<string> SelectListNames(List<SqlToken> sig, int from, int to)
        {
            var names = new List<string>();
            var i = from;
            if (i <= to && (sig[i].Is("DISTINCT") || sig[i].Is("ALL"))) i++;
            if (i <= to && sig[i].Is("TOP"))
            {
                i++;
                if (i <= to && sig[i].Text == "(") i = MatchingParen(sig, i) + 1;
                else i++;
                if (i <= to && sig[i].Is("PERCENT")) i++;
                if (i + 1 <= to && sig[i].Is("WITH") && sig[i + 1].Is("TIES")) i += 2;
            }
            var itemStart = i;
            var depth = 0;
            for (; i <= to + 1; i++)
            {
                var atEnd = i > to;
                var t = atEnd ? null : sig[i];
                if (!atEnd)
                {
                    if (t.Text == "(") { depth++; continue; }
                    if (t.Text == ")") { depth--; if (depth < 0) atEnd = true; else continue; }
                    if (!atEnd && depth == 0 && (t.Is("FROM") || t.Is("INTO") || t.Is("WHERE") || t.Is("UNION") || t.Text == ";"))
                        atEnd = true;
                }
                if (atEnd || (depth == 0 && t.Text == ","))
                {
                    var name = ItemName(sig, itemStart, i - 1);
                    if (name != null) names.Add(name);
                    itemStart = i + 1;
                    if (atEnd) break;
                }
            }
            return names;
        }

        private static string ItemName(List<SqlToken> sig, int s, int e)
        {
            if (s > e) return null;
            if (e - s >= 1 && sig[e - 1].Is("AS") && (sig[e].IsName || sig[e].Kind == TokenKind.StringLiteral))
                return Unquote(sig[e]);
            if (e - s >= 2 && sig[s].IsName && sig[s + 1].Text == "=") return sig[s].Unquoted;
            if (sig[e].IsName)
            {
                // "expr alias" or a bare (possibly qualified) column.
                return sig[e].Unquoted;
            }
            return null;
        }

        private static string Unquote(SqlToken t)
        {
            if (t.Kind == TokenKind.StringLiteral && t.Text.Length >= 2)
            {
                var s = t.Text.StartsWith("N", StringComparison.OrdinalIgnoreCase) ? t.Text.Substring(1) : t.Text;
                return s.Trim('\'');
            }
            return t.Unquoted;
        }

        // ---------- variables ----------

        public static List<VariableInfo> CollectVariables(List<SqlToken> sig, int limit)
        {
            var byName = new Dictionary<string, VariableInfo>(Ci);
            var list = new List<VariableInfo>();
            for (var i = 0; i < sig.Count && i < limit; i++)
            {
                var t = sig[i];
                if (t.Kind != TokenKind.Variable || t.Text.StartsWith("@@", StringComparison.Ordinal)) continue;
                if (!byName.TryGetValue(t.Text, out var v))
                {
                    v = new VariableInfo(t.Text, null);
                    byName[t.Text] = v;
                    list.Add(v);
                }
                if (v.DataType == null && i + 1 < sig.Count)
                {
                    var n = i + 1;
                    if (sig[n].Is("AS") && n + 1 < sig.Count) n++;
                    if (sig[n].IsWord && (SqlVocabulary.IsDataType(sig[n].Text) || sig[n].Is("TABLE") || sig[n].Is("CURSOR")))
                        v.DataType = sig[n].Text.ToLowerInvariant();
                }
            }
            return list;
        }

        // ---------- helpers ----------

        public static int MatchingParen(List<SqlToken> sig, int open)
        {
            var depth = 0;
            for (var k = open; k < sig.Count; k++)
            {
                if (sig[k].Text == "(") depth++;
                else if (sig[k].Text == ")" && --depth == 0) return k;
            }
            return -1;
        }

        public static int FindWordAtDepth(List<SqlToken> sig, int from, int to, string upper)
        {
            var depth = 0;
            for (var k = from; k <= to && k < sig.Count; k++)
            {
                if (sig[k].Text == "(") depth++;
                else if (sig[k].Text == ")") depth--;
                else if (depth == 0 && sig[k].Is(upper)) return k;
            }
            return -1;
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
