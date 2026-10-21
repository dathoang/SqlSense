using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlSense.Core.Analysis;
using SqlSense.Core.Language;
using SqlSense.Core.Lexing;
using SqlSense.Core.Schema;

namespace SqlSense.Core.Completion
{
    /// <summary>Generates short aliases from object names: SalesOrderHeader → soh, order_items → oi.</summary>
    public static class AliasGenerator
    {
        public static string Generate(string objectName, IEnumerable<string> taken)
        {
            var name = (objectName ?? "t").Trim('[', ']', '#', '@');
            var sb = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (!char.IsLetter(c)) continue;
                var isStart = i == 0 || !char.IsLetterOrDigit(name[i - 1])
                              || char.IsUpper(c) && char.IsLower(name[i - 1]);
                if (isStart) sb.Append(char.ToLowerInvariant(c));
            }
            var alias = sb.Length == 0 ? "t" : sb.ToString();
            if (IsKeyword(alias)) alias += "1";

            var used = new HashSet<string>(taken.Where(t => t != null), StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(alias)) return alias;
            for (var n = 2; ; n++)
                if (!used.Contains(alias + n)) return alias + n;
        }

        private static bool IsKeyword(string word) =>
            SqlVocabulary.Keywords.Contains(word.ToUpperInvariant());
    }

    /// <summary>Join conditions between the newest joined table and earlier ones: from foreign keys or same-named columns.</summary>
    public static class JoinConditions
    {
        public static List<string> Suggest(List<TableRef> refs, DatabaseSchema schema)
        {
            var result = new List<string>();
            if (schema == null || refs.Count < 2) return result;
            var joined = refs[refs.Count - 1];
            if (joined.Object == null) return result;
            foreach (var other in refs.Take(refs.Count - 1).Reverse())
            {
                if (other.Object == null) continue;
                foreach (var fk in schema.ForeignKeys)
                {
                    if (Is(fk.FromSchema, fk.FromTable, joined.Object) && Is(fk.ToSchema, fk.ToTable, other.Object))
                        result.Add(Condition(joined, fk.FromColumns, other, fk.ToColumns));
                    else if (Is(fk.FromSchema, fk.FromTable, other.Object) && Is(fk.ToSchema, fk.ToTable, joined.Object))
                        result.Add(Condition(joined, fk.ToColumns, other, fk.FromColumns));
                }
            }
            return result.Distinct().ToList();
        }

        /// <summary>
        /// Conditions on columns with the same name in the joined table and an earlier one (nearest first). Works without
        /// a schema (CTEs, temp tables, derived tables). Key-like names (…ID, …Code, …Key, …No) come first, and several
        /// of them on one pair also give a combined AND condition. Columns whose types differ, or that are the identity
        /// of both tables (two unrelated surrogate keys), are skipped.
        /// </summary>
        public static List<string> SameName(List<TableRef> refs)
        {
            var result = new List<string>();
            if (refs.Count < 2) return result;
            var joined = refs[refs.Count - 1];
            var joinedCols = joined.ColumnNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (joinedCols.Count == 0) return result;
            foreach (var other in refs.Take(refs.Count - 1).Reverse())
            {
                var common = joinedCols
                    .Where(c => other.ColumnNames.Any(o => string.Equals(o, c, StringComparison.OrdinalIgnoreCase)))
                    .Where(c => Joinable(Column(joined, c), Column(other, c)))
                    .ToList();
                var keys = common.Where(IsKeyLike).ToList();
                if (keys.Count > 1) result.Add(Condition(joined, keys, other, keys));
                foreach (var c in keys.Concat(common.Where(c => !IsKeyLike(c))))
                    result.Add(Condition(joined, new[] { c }, other, new[] { c }));
            }
            return result.Distinct().ToList();
        }

        private static readonly string[] KeySuffixes = { "id", "code", "key", "no", "num", "number" };

        private static bool IsKeyLike(string column) =>
            KeySuffixes.Any(s => column.EndsWith(s, StringComparison.OrdinalIgnoreCase));

        private static DbColumn Column(TableRef r, string name) =>
            r.Object?.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        private static bool Joinable(DbColumn a, DbColumn b)
        {
            if (a == null || b == null) return true;
            if (a.IsIdentity && b.IsIdentity) return false;
            return string.Equals(BaseType(a.DataType), BaseType(b.DataType), StringComparison.OrdinalIgnoreCase);
        }

        private static string BaseType(string dataType)
        {
            var t = (dataType ?? string.Empty).Trim();
            var paren = t.IndexOf('(');
            return paren >= 0 ? t.Substring(0, paren).Trim() : t;
        }

        private static bool Is(string schema, string table, DbObject o) =>
            string.Equals(schema, o.Schema, StringComparison.OrdinalIgnoreCase) && string.Equals(table, o.Name, StringComparison.OrdinalIgnoreCase);

        private static string Condition(TableRef left, IReadOnlyList<string> leftCols, TableRef right, IReadOnlyList<string> rightCols)
        {
            var parts = new List<string>();
            for (var i = 0; i < leftCols.Count && i < rightCols.Count; i++)
                parts.Add($"{CompletionEngine.Quote(left.Qualifier)}.{CompletionEngine.Quote(leftCols[i])} = {CompletionEngine.Quote(right.Qualifier)}.{CompletionEngine.Quote(rightCols[i])}");
            return string.Join(" AND ", parts);
        }
    }

    public sealed class TextEdit
    {
        public TextEdit(int start, int length, string newText, int caretOffset = -1)
        {
            Start = start;
            Length = length;
            NewText = newText;
            CaretOffset = caretOffset;
        }

        public int Start { get; }
        public int Length { get; }
        public string NewText { get; }

        /// <summary>Caret position inside NewText after applying; -1 = end.</summary>
        public int CaretOffset { get; }
    }

    /// <summary>Expands "*" (or "alias.*") in a select list into explicit columns.</summary>
    public static class StarExpander
    {
        public static TextEdit TryExpand(string text, int caret, DatabaseSchema schema, string newLine = "\r\n")
        {
            if (caret <= 0 || caret > text.Length) return null;
            var batch = ScriptText.GetBatchAt(text, caret);
            var rel = caret - batch.Start;
            var all = ScriptText.Significant(batch.Tokens);
            var idx = all.FindLastIndex(t => t.End <= rel);
            if (idx < 0 || all[idx].Text != "*" || all[idx].End != rel) return null;

            string qualifier = null;
            var start = idx;
            if (idx >= 2 && all[idx - 1].Text == "." && all[idx - 2].IsName)
            {
                qualifier = all[idx - 2].Unquoted;
                start = idx - 2;
            }
            else if (idx >= 1 && !(all[idx - 1].Is("SELECT") || all[idx - 1].Text == "," || all[idx - 1].Is("DISTINCT")
                                   || all[idx - 1].Text == ")" && IsTopClause(all, idx - 1) || all[idx - 1].Kind == TokenKind.NumberLiteral && idx >= 2 && all[idx - 2].Is("TOP")))
            {
                return null; // multiplication or COUNT(*)
            }
            if (idx >= 1 && all[idx - 1].Text == "(") return null;

            var statements = StatementAnalysis.SplitStatements(all);
            var stmt = statements.LastOrDefault(s => s.Start <= idx);
            var refs = StatementAnalysis.CollectTableRefs(all, stmt.Start, stmt.End);
            var ctes = StatementAnalysis.CollectCtes(all, stmt.Start, stmt.End);
            var locals = StatementAnalysis.CollectLocalTables(all, idx);
            ContextAnalyzer.Resolve(refs, ctes, locals, schema);

            // Only sources of the SELECT that owns this star (same nesting level, after it).
            var targets = qualifier != null ? refs.Where(r => r.Matches(qualifier)).ToList() : refs.Where(r => r.TokenIndex > idx).ToList();
            if (targets.Count == 0) targets = refs;
            if (targets.Count == 0) return null;

            var qualify = qualifier != null || targets.Count > 1;
            var columns = new List<string>();
            foreach (var r in targets)
                foreach (var c in r.ColumnNames)
                    columns.Add(qualify ? CompletionEngine.Quote(r.Qualifier) + "." + CompletionEngine.Quote(c) : CompletionEngine.Quote(c));
            if (columns.Count == 0) return null;

            var startOffset = batch.Start + all[start].Start;
            var column = ScriptText.ColumnOf(text, startOffset);
            var indent = new string(' ', column);
            var replacement = string.Join("," + newLine + indent, columns);
            return new TextEdit(startOffset, caret - startOffset, replacement);
        }

        private static bool IsTopClause(List<SqlToken> all, int closeParen)
        {
            var depth = 0;
            for (var i = closeParen; i >= 0; i--)
            {
                if (all[i].Text == ")") depth++;
                else if (all[i].Text == "(" && --depth == 0) return i > 0 && all[i - 1].Is("TOP");
            }
            return false;
        }
    }
}
