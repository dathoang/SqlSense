using System;
using System.Collections.Generic;
using SqlSense.Core.Lexing;

namespace SqlSense.Core.Analysis
{
    /// <summary>
    /// Pairs the column list of an INSERT with its values: with the caret on a column, the column and
    /// the value at the same position in every VALUES row (or the SELECT list) are returned; with the
    /// caret on a value, that value and its column.
    /// </summary>
    public static class InsertPairs
    {
        /// <summary>Ranges (absolute start, length) to highlight; empty when the caret is not on a paired item.</summary>
        public static List<(int Start, int Length)> Find(string text, int caret)
        {
            var result = new List<(int, int)>();
            if (string.IsNullOrEmpty(text) || text.IndexOf("INSERT", StringComparison.OrdinalIgnoreCase) < 0) return result;

            var batch = ScriptText.GetBatchAt(text, caret);
            var rel = caret - batch.Start;
            var sig = ScriptText.Significant(batch.Tokens);

            foreach (var (start, end) in StatementAnalysis.SplitStatements(sig))
            {
                if (rel < sig[start].Start || rel > sig[end].End) continue;
                var insert = StatementAnalysis.FindWordAtDepth(sig, start, end, "INSERT");
                if (insert < 0) continue;

                var columns = ColumnList(sig, insert + 1, end);
                if (columns == null) continue;
                var rows = SourceRows(sig, columns.Value.Close + 1, end);
                if (rows.Count == 0) continue;

                var cols = SplitItems(sig, columns.Value.Open + 1, columns.Value.Close - 1);
                var col = IndexAt(cols, sig, rel);
                if (col >= 0)
                {
                    Add(result, batch, sig, cols[col]);
                    foreach (var row in rows)
                        if (col < row.Count) Add(result, batch, sig, row[col]);
                    return result;
                }
                foreach (var row in rows)
                {
                    var value = IndexAt(row, sig, rel);
                    if (value < 0) continue;
                    Add(result, batch, sig, row[value]);
                    if (value < cols.Count) Add(result, batch, sig, cols[value]);
                    return result;
                }
            }
            return result;
        }

        /// <summary>The "( … )" column list after INSERT [TOP (n) [PERCENT]] [INTO] target [WITH (hints)].</summary>
        private static (int Open, int Close)? ColumnList(List<SqlToken> sig, int i, int end)
        {
            if (i <= end && sig[i].Is("TOP"))
            {
                i++;
                if (i <= end && sig[i].Text == "(") i = StatementAnalysis.MatchingParen(sig, i) + 1;
                if (i <= 0) return null;
                if (i <= end && sig[i].Is("PERCENT")) i++;
            }
            if (i <= end && sig[i].Is("INTO")) i++;

            // Target: a (possibly qualified) table name, #temp or @table variable.
            var named = false;
            while (i <= end && (sig[i].IsName || sig[i].Kind == TokenKind.Variable || sig[i].Text == "."))
            {
                named = true;
                i++;
            }
            if (!named) return null;

            if (i + 1 <= end && sig[i].Is("WITH") && sig[i + 1].Text == "(")
                i = StatementAnalysis.MatchingParen(sig, i + 1) + 1;
            if (i <= 0 || i > end || sig[i].Text != "(") return null;

            var close = StatementAnalysis.MatchingParen(sig, i);
            if (close < 0 || close > end) return null;
            // "INSERT t (SELECT …)" is a source, not a column list.
            if (close > i + 1 && (sig[i + 1].Is("SELECT") || sig[i + 1].Is("WITH"))) return null;
            return (i, close);
        }

        /// <summary>Value rows: each "( … )" after VALUES, or the select list of INSERT … SELECT.</summary>
        private static List<List<(int First, int Last)>> SourceRows(List<SqlToken> sig, int from, int end)
        {
            var rows = new List<List<(int, int)>>();
            var values = StatementAnalysis.FindWordAtDepth(sig, from, end, "VALUES");
            if (values >= 0)
            {
                var i = values + 1;
                while (i <= end && sig[i].Text == "(")
                {
                    var close = StatementAnalysis.MatchingParen(sig, i);
                    if (close < 0 || close > end) close = end + 1; // row still being typed
                    rows.Add(SplitItems(sig, i + 1, close - 1));
                    i = close + 1;
                    if (i <= end && sig[i].Text == ",") i++;
                    else break;
                }
                return rows;
            }

            var select = StatementAnalysis.FindWordAtDepth(sig, from, end, "SELECT");
            if (select < 0) return rows;
            var s = select + 1;
            if (s <= end && (sig[s].Is("DISTINCT") || sig[s].Is("ALL"))) s++;
            if (s <= end && sig[s].Is("TOP"))
            {
                s++;
                if (s <= end && sig[s].Text == "(") s = StatementAnalysis.MatchingParen(sig, s) + 1;
                else s++;
                if (s <= 0) return rows;
                if (s <= end && sig[s].Is("PERCENT")) s++;
                if (s + 1 <= end && sig[s].Is("WITH") && sig[s + 1].Is("TIES")) s += 2;
            }
            var stop = end;
            var depth = 0;
            for (var k = s; k <= end; k++)
            {
                if (sig[k].Text == "(") depth++;
                else if (sig[k].Text == ")") depth--;
                else if (depth == 0 && IsSelectListEnd(sig[k]))
                {
                    stop = k - 1;
                    break;
                }
            }
            rows.Add(SplitItems(sig, s, stop));
            return rows;
        }

        private static bool IsSelectListEnd(SqlToken t) =>
            t.Text == ";" || t.Is("FROM") || t.Is("INTO") || t.Is("WHERE") || t.Is("GROUP") || t.Is("HAVING")
            || t.Is("ORDER") || t.Is("UNION") || t.Is("EXCEPT") || t.Is("INTERSECT") || t.Is("OPTION");

        /// <summary>Comma-separated items of sig[from..to] at nesting depth zero, as (first, last) token indexes.</summary>
        private static List<(int First, int Last)> SplitItems(List<SqlToken> sig, int from, int to)
        {
            var items = new List<(int, int)>();
            var itemStart = from;
            var depth = 0;
            for (var k = from; k <= to + 1; k++)
            {
                var atEnd = k > to || k >= sig.Count;
                if (!atEnd)
                {
                    if (sig[k].Text == "(") { depth++; continue; }
                    if (sig[k].Text == ")") { depth--; continue; }
                    if (depth != 0 || sig[k].Text != ",") continue;
                }
                if (k - 1 >= itemStart) items.Add((itemStart, k - 1));
                else items.Add((-1, -1)); // empty slot ("a,,b") keeps later positions aligned
                itemStart = k + 1;
                if (atEnd) break;
            }
            // A trailing empty slot is only the end of the list, not an item.
            if (items.Count > 0 && items[items.Count - 1].Item1 < 0) items.RemoveAt(items.Count - 1);
            return items;
        }

        /// <summary>Index of the item containing the caret (touching either edge counts), or -1.</summary>
        private static int IndexAt(List<(int First, int Last)> items, List<SqlToken> sig, int rel)
        {
            for (var n = 0; n < items.Count; n++)
            {
                var (first, last) = items[n];
                if (first >= 0 && rel >= sig[first].Start && rel <= sig[last].End) return n;
            }
            return -1;
        }

        private static void Add(List<(int, int)> result, Batch batch, List<SqlToken> sig, (int First, int Last) item)
        {
            if (item.First < 0) return;
            var start = sig[item.First].Start;
            result.Add((batch.Start + start, sig[item.Last].End - start));
        }
    }
}
