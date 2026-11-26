using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlSense.Core.Lexing;

namespace SqlSense.Core.Analysis
{
    /// <summary>One item of a SELECT list: "price * quantity AS amount", "amount = price * quantity", "c.Name".</summary>
    public sealed class SelectItem
    {
        public SelectItem(string name, string expression, bool hasAlias, bool isAggregate, bool isSimpleColumn, bool hasOperator)
        {
            Name = name;
            Expression = expression;
            HasAlias = hasAlias;
            IsAggregate = isAggregate;
            IsSimpleColumn = isSimpleColumn;
            HasOperator = hasOperator;
        }

        /// <summary>Output name: the alias, or the column name of a plain column.</summary>
        public string Name { get; }

        /// <summary>The expression as written (line breaks folded into single spaces).</summary>
        public string Expression { get; }

        public bool HasAlias { get; }

        /// <summary>Contains an aggregate (SUM, COUNT …) that is not a window function.</summary>
        public bool IsAggregate { get; }

        /// <summary>A bare, possibly qualified, column: "c.Name".</summary>
        public bool IsSimpleColumn { get; }

        /// <summary>Has a binary operator outside parentheses ("a + b"): needs parentheses inside a larger expression.</summary>
        public bool HasOperator { get; }

        public override string ToString() => HasAlias ? $"{Expression} AS {Name}" : Expression;
    }

    /// <summary>The clause around the caret and the SELECT list that owns it.</summary>
    public static class SelectList
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        private static readonly HashSet<string> Aggregates = new HashSet<string>(Ci)
        {
            "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP", "STRING_AGG",
            "GROUPING", "GROUPING_ID", "CHECKSUM_AGG", "APPROX_COUNT_DISTINCT", "APPROX_PERCENTILE_CONT", "APPROX_PERCENTILE_DISC",
        };

        private static readonly HashSet<string> ClauseWords = new HashSet<string>(Ci)
        {
            "SELECT", "FROM", "WHERE", "HAVING", "BY", "ON", "SET", "JOIN", "VALUES", "INTO", "UNION", "EXCEPT", "INTERSECT",
        };

        private static readonly HashSet<string> ListEnd = new HashSet<string>(Ci)
        {
            "FROM", "INTO", "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "OPTION", "FOR", "WINDOW",
        };

        private static readonly HashSet<string> Operators = new HashSet<string>
        {
            "+", "-", "*", "/", "%", "&", "|", "^", "=", "<>", "!=", "<", ">", "<=", ">=",
        };

        /// <summary>
        /// The clause that holds sig[p] at its nesting level ("WHERE", "GROUP BY", "ORDER BY", "HAVING", or null for
        /// anything else), and the index of that clause's keyword and of the SELECT that owns it.
        /// </summary>
        public static (string Clause, int ClauseIndex, int SelectIndex) FindClause(List<SqlToken> sig, int p, int stmtStart)
        {
            var none = ((string)null, -1, -1);
            var clause = WalkBack(sig, p, stmtStart, ClauseWords);
            if (clause < 0) return none;
            string name;
            var t = sig[clause];
            if (t.Is("WHERE") || t.Is("HAVING")) name = t.Upper;
            else if (t.Is("BY") && clause > 0 && (sig[clause - 1].Is("GROUP") || sig[clause - 1].Is("ORDER"))) name = sig[clause - 1].Upper + " BY";
            else return none;
            var select = WalkBack(sig, clause - 1, stmtStart, new HashSet<string>(Ci) { "SELECT" });
            return select < 0 ? none : (name, clause, select);
        }

        /// <summary>Nearest word of <paramref name="words"/> at the depth of sig[p], not crossing an unmatched "(".</summary>
        private static int WalkBack(List<SqlToken> sig, int p, int stmtStart, HashSet<string> words)
        {
            var depth = 0;
            for (var i = Math.Min(p, sig.Count - 1); i >= 0 && i >= stmtStart; i--)
            {
                var t = sig[i];
                if (t.Text == ")") depth++;
                else if (t.Text == "(")
                {
                    if (depth == 0) return -1;
                    depth--;
                }
                else if (depth == 0 && t.IsWord && words.Contains(t.Text)) return i;
            }
            return -1;
        }

        /// <summary>The items of the SELECT list starting after sig[select]; <paramref name="text"/> is what the token offsets refer to.</summary>
        public static List<SelectItem> Collect(List<SqlToken> sig, int select, string text)
        {
            var items = new List<SelectItem>();
            var i = select + 1;
            var last = sig.Count - 1;
            if (i <= last && (sig[i].Is("DISTINCT") || sig[i].Is("ALL"))) i++;
            if (i <= last && sig[i].Is("TOP"))
            {
                i++;
                if (i <= last && sig[i].Text == "(") i = StatementAnalysis.MatchingParen(sig, i) + 1;
                else i++;
                if (i <= 0) return items;
                if (i <= last && sig[i].Is("PERCENT")) i++;
                if (i + 1 <= last && sig[i].Is("WITH") && sig[i + 1].Is("TIES")) i += 2;
            }
            var itemStart = i;
            var depth = 0;
            for (; i <= last + 1; i++)
            {
                var atEnd = i > last;
                if (!atEnd)
                {
                    var t = sig[i];
                    if (t.Text == "(") { depth++; continue; }
                    if (t.Text == ")")
                    {
                        if (--depth >= 0) continue;
                        atEnd = true;
                    }
                    else if (depth == 0 && (t.Text == ";" || t.IsWord && ListEnd.Contains(t.Text))) atEnd = true;
                }
                if (atEnd || depth == 0 && sig[i].Text == ",")
                {
                    var item = Item(sig, itemStart, i - 1, text);
                    if (item != null) items.Add(item);
                    itemStart = i + 1;
                    if (atEnd) break;
                }
            }
            return items;
        }

        private static SelectItem Item(List<SqlToken> sig, int s, int e, string text)
        {
            if (s > e) return null;
            if (sig[s].Kind == TokenKind.Variable) return null; // @x = …: assignment, not a column
            if (sig[e].Text == "*") return null;

            string alias = null;
            int exprStart = s, exprEnd = e;
            if (e - s >= 2 && sig[e - 1].Is("AS") && (sig[e].IsName || sig[e].Kind == TokenKind.StringLiteral))
            {
                alias = Unquote(sig[e]);
                exprEnd = e - 2;
            }
            else if (e - s >= 2 && sig[s].IsName && sig[s + 1].Text == "=")
            {
                alias = sig[s].Unquoted;
                exprStart = s + 2;
            }
            else if (e - s >= 1 && sig[e].IsName && sig[e - 1].Text != "." && IsOperandEnd(sig[e - 1]))
            {
                alias = sig[e].Unquoted; // "expr alias"
                exprEnd = e - 1;
            }
            if (exprStart > exprEnd) return null;

            var simple = IsColumn(sig, exprStart, exprEnd);
            var hasName = false;
            var aggregate = false;
            var op = false;
            var depth = 0;
            for (var k = exprStart; k <= exprEnd; k++)
            {
                var t = sig[k];
                if (t.IsName) hasName = true;
                if (t.Text == "(") depth++;
                else if (t.Text == ")") depth--;
                else if (depth == 0 && k > exprStart && (Operators.Contains(t.Text) || t.Is("AND") || t.Is("OR"))) op = true;
                if (t.IsWord && Aggregates.Contains(t.Text) && k + 1 <= exprEnd && sig[k + 1].Text == "(")
                {
                    var close = StatementAnalysis.MatchingParen(sig, k + 1);
                    if (close < 0 || close + 1 > exprEnd || !sig[close + 1].Is("OVER")) aggregate = true;
                }
            }
            if (!hasName && !aggregate) return null; // a constant: nothing to reuse
            var name = alias ?? (simple ? sig[exprEnd].Unquoted : null);
            if (name == null) return null;
            var expression = Fold(text.Substring(sig[exprStart].Start, sig[exprEnd].End - sig[exprStart].Start));
            return new SelectItem(name, expression, alias != null, aggregate, simple, op);
        }

        /// <summary>A token that can end an expression before a bare alias: a name, literal, ")" or END.</summary>
        private static bool IsOperandEnd(SqlToken t) =>
            t.IsName || t.Text == ")" || t.Is("END") || t.Kind == TokenKind.NumberLiteral || t.Kind == TokenKind.StringLiteral;

        private static bool IsColumn(List<SqlToken> sig, int s, int e)
        {
            for (var k = s; k <= e; k++)
            {
                var name = (k - s) % 2 == 0;
                if (name ? !sig[k].IsName : sig[k].Text != ".") return false;
            }
            return (e - s) % 2 == 0;
        }

        /// <summary>Folds line breaks and runs of spaces outside string literals into one space.</summary>
        public static string Fold(string s)
        {
            var sb = new StringBuilder(s.Length);
            var inString = false;
            var space = false;
            foreach (var c in s)
            {
                if (c == '\'') inString = !inString;
                if (!inString && char.IsWhiteSpace(c))
                {
                    space = true;
                    continue;
                }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
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

        /// <summary>The expressions already listed between sig[from] and sig[to] (a GROUP BY list), folded.</summary>
        public static HashSet<string> ListedExpressions(List<SqlToken> sig, int from, int to, string text)
        {
            var result = new HashSet<string>(Ci);
            var start = from;
            var depth = 0;
            for (var k = from; k <= to + 1; k++)
            {
                var end = k > to;
                if (!end)
                {
                    if (sig[k].Text == "(") depth++;
                    else if (sig[k].Text == ")") depth--;
                }
                if (end || depth == 0 && sig[k].Text == ",")
                {
                    if (start <= k - 1) result.Add(Fold(text.Substring(sig[start].Start, sig[k - 1].End - sig[start].Start)));
                    start = k + 1;
                }
            }
            return result;
        }
    }
}
