using System.Collections.Generic;
using SqlSense.Core.Lexing;

namespace SqlSense.Core.Analysis
{
    public static partial class ContextAnalyzer
    {
        /// <summary>Words that start a table-level constraint (or inline index) instead of a column.</summary>
        private static readonly HashSet<string> TableConstraintStarts = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "INDEX", "PERIOD",
        };

        /// <summary>
        /// Column and constraint definitions: CREATE TABLE t ( … ), DECLARE @t TABLE ( … ), CREATE TYPE … AS TABLE ( … ),
        /// RETURNS @t TABLE ( … ) and ALTER TABLE t ADD|ALTER COLUMN|DROP|CHECK|NOCHECK|ENABLE|DISABLE … .
        /// Data type after a column name, column options after its type, constraint keywords, referenced tables and
        /// columns. Null when none applies.
        /// </summary>
        private static ContextKind? ClassifyTableDefinition(List<SqlToken> sig, int p, int stmtStart, CompletionContext ctx)
        {
            if (p < stmtStart) return null;
            var kind = sig[stmtStart].Is("ALTER") && stmtStart + 2 < sig.Count && sig[stmtStart + 1].Is("TABLE")
                ? ClassifyAlterTable(sig, p, stmtStart, ctx)
                : ClassifyTableBody(sig, p, stmtStart, ctx);
            // A qualifier only makes sense for the referenced table (REFERENCES dbo.|).
            if (ctx.HasQualifier && kind != ContextKind.TableSource) return null;
            return kind;
        }

        private static ContextKind? ClassifyTableBody(List<SqlToken> sig, int p, int stmtStart, CompletionContext ctx)
        {
            // Unclosed parentheses around the caret, innermost first.
            var openers = new List<int>();
            var depth = 0;
            for (var i = p; i >= stmtStart; i--)
            {
                if (sig[i].Text == ")") depth++;
                else if (sig[i].Text == "(")
                {
                    if (depth == 0) openers.Add(i);
                    else depth--;
                }
            }

            foreach (var opener in openers)
            {
                var name = TableDefinitionName(sig, opener, stmtStart);
                if (name == null) continue;
                var start = ElementStart(sig, opener + 1, p);
                var nested = openers[0] > opener ? openers[0] : -1;
                // Columns defined so far, for PRIMARY KEY (…), UNIQUE (…), FOREIGN KEY (…), INDEX ix (…).
                var own = new TableRef { Name = name, Source = TableRefSource.Derived, LocalColumns = DefinedColumns(sig, opener + 1, p) };
                return ClassifyElement(sig, start, p, nested, own, alter: false, ctx);
            }
            return null;
        }

        /// <summary>The table's name when sig[opener] opens a table definition, else null.</summary>
        private static string TableDefinitionName(List<SqlToken> sig, int opener, int stmtStart)
        {
            var k = opener - 1;
            if (k < stmtStart) return null;
            // DECLARE @t TABLE (, RETURNS @t TABLE (, CREATE TYPE x AS TABLE (
            if (sig[k].Is("TABLE")) return k - 1 >= stmtStart && sig[k - 1].Kind == TokenKind.Variable ? sig[k - 1].Text : "table";
            var nameEnd = k;
            while (k >= stmtStart && (sig[k].IsName || sig[k].Text == ".")) k--;
            if (k == nameEnd || k < stmtStart + 1 || !sig[k].Is("TABLE") || !sig[k - 1].Is("CREATE")) return null;
            return sig[nameEnd].Unquoted;
        }

        /// <summary>First token of the element (column or constraint) the caret is in: after the last comma at depth 0.</summary>
        private static int ElementStart(List<SqlToken> sig, int from, int p)
        {
            var start = from;
            var depth = 0;
            for (var i = from; i <= p; i++)
            {
                if (sig[i].Text == "(") depth++;
                else if (sig[i].Text == ")") depth--;
                else if (depth == 0 && sig[i].Text == ",") start = i + 1;
            }
            return start;
        }

        private static List<string> DefinedColumns(List<SqlToken> sig, int from, int p)
        {
            var columns = new List<string>();
            var depth = 0;
            var atStart = true;
            for (var i = from; i <= p; i++)
            {
                var t = sig[i];
                if (atStart && depth == 0 && t.IsName && !TableConstraintStarts.Contains(t.Text)) columns.Add(t.Unquoted);
                atStart = false;
                if (t.Text == "(") depth++;
                else if (t.Text == ")") depth--;
                else if (depth == 0 && t.Text == ",") atStart = true;
            }
            return columns;
        }

        /// <summary>
        /// One column or constraint definition, sig[start..p] typed so far. <paramref name="nested"/> is an unclosed
        /// parenthesis inside it (or -1); <paramref name="own"/> is the table being defined or altered.
        /// </summary>
        private static ContextKind? ClassifyElement(List<SqlToken> sig, int start, int p, int nested, TableRef own, bool alter, CompletionContext ctx)
        {
            if (nested >= start) return ClassifyElementParen(sig, start, p, nested, own, ctx);
            if (p < start) return Options(ctx, "TABLE ELEMENT");

            var first = sig[start];
            var t = sig[p];
            var isConstraint = TableConstraintStarts.Contains(first.Text) && first.IsWord;
            var references = FindWord(sig, start, p, "REFERENCES");

            if (t.Is("REFERENCES"))
            {
                ctx.ClauseKeyword = "REFERENCES";
                return ContextKind.TableSource;
            }
            if (t.Is("CONSTRAINT") || t.Is("COLLATE") || t.Is("IDENTITY") || t.Is("CHECK") || t.Is("WITH") || t.Is("INDEX"))
                return ContextKind.AliasName;
            if (p - 1 >= start && sig[p - 1].Is("CONSTRAINT"))
                return Options(ctx, p - 1 == start ? (alter ? "ALTER TABLE CONSTRAINT" : "TABLE CONSTRAINT") : "COLUMN CONSTRAINT");
            if (t.Is("PRIMARY")) return Options(ctx, "PRIMARY");
            if (t.Is("FOREIGN")) return Options(ctx, "FOREIGN");
            // Column-level FOREIGN KEY | → REFERENCES; table-level FOREIGN KEY | is followed by its column list.
            if (t.Is("KEY") && sig[p - 1].Is("FOREIGN")) return isConstraint ? ContextKind.AliasName : Options(ctx, "FOREIGN KEY END");
            if (t.Is("NOT")) return Options(ctx, "NOT");
            if (t.Is("DEFAULT")) return ContextKind.Expression;
            if (t.Is("FOR") && FindWord(sig, start, p, "DEFAULT") >= 0) return OwnColumns(own, ctx);
            if (references >= 0)
            {
                if (t.Is("ON")) return Options(ctx, "REFERENCES ON");
                if ((t.Is("DELETE") || t.Is("UPDATE")) && sig[p - 1].Is("ON")) return Options(ctx, "REFERENTIAL ACTION");
                if (t.Is("SET") && p - 2 >= start && sig[p - 2].Is("ON")) return Options(ctx, "SET ACTION");
                if (t.Is("NO")) return Options(ctx, "NO");
                if (t.Text == ")" || t.IsName) return Options(ctx, "REFERENCES");
            }

            if (isConstraint)
            {
                // PRIMARY KEY |, UNIQUE | → CLUSTERED / NONCLUSTERED; FOREIGN KEY (…) | → REFERENCES.
                if (t.Is("UNIQUE") || t.Is("KEY") && sig[p - 1].Is("PRIMARY")) return Options(ctx, "KEY TYPE");
                if (t.Text == ")" && references < 0 && FindWord(sig, start, p, "FOREIGN") >= 0) return Options(ctx, "FOREIGN KEY END");
                // ALTER TABLE … ADD CONSTRAINT DF DEFAULT 0 | → FOR column.
                if (alter && FindWord(sig, start, p, "DEFAULT") >= 0 && FindWord(sig, start, p, "FOR") < 0) return Options(ctx, "DEFAULT END");
                return ContextKind.AliasName;
            }

            // A column: name | → data type; name AS | → computed column expression; name type … | → column options.
            if (p == start) return ContextKind.DataType;
            if (sig[start + 1].Is("AS"))
            {
                if (p > start + 1) return null;
                ctx.ClauseKeyword = "AS";
                return ContextKind.Expression;
            }
            return Options(ctx, "COLUMN");
        }

        /// <summary>Inside a parenthesis of a column or constraint definition.</summary>
        private static ContextKind? ClassifyElementParen(List<SqlToken> sig, int start, int p, int nested, TableRef own, CompletionContext ctx)
        {
            var t = sig[p];
            if (!(p == nested || t.Text == ",")) return ContextKind.AliasName;
            var before = sig[nested - 1];
            // PRIMARY KEY [NON]CLUSTERED (|, UNIQUE (|, FOREIGN KEY (|, INDEX ix (|
            if (before.Is("KEY") || before.Is("UNIQUE") || before.Is("CLUSTERED") || before.Is("NONCLUSTERED")
                || before.IsName && nested - 2 >= start && sig[nested - 2].Is("INDEX"))
                return OwnColumns(own, ctx);
            // REFERENCES t (|
            var k = nested - 1;
            while (k > start && (sig[k].IsName || sig[k].Text == ".")) k--;
            if (k < nested - 1 && sig[k].Is("REFERENCES"))
            {
                var target = TableAfter(sig, k + 1);
                if (target == null) return ContextKind.AliasName;
                ctx.InsertTarget = target;
                ctx.ClauseKeyword = "INDEX";
                return ContextKind.InsertColumns;
            }
            // CHECK (…), DEFAULT (…): an expression; type arguments, IDENTITY (1, 1), WITH (…): nothing to suggest.
            if (before.Is("CHECK") || before.Is("DEFAULT")) return null;
            return ContextKind.AliasName;
        }

        private static ContextKind OwnColumns(TableRef own, CompletionContext ctx)
        {
            ctx.InsertTarget = own;
            ctx.ClauseKeyword = "INDEX";
            return ContextKind.InsertColumns;
        }

        private static int FindWord(List<SqlToken> sig, int from, int to, string word)
        {
            for (var i = from; i <= to; i++)
                if (sig[i].Is(word)) return i;
            return -1;
        }

        private static ContextKind? ClassifyAlterTable(List<SqlToken> sig, int p, int stmtStart, CompletionContext ctx)
        {
            // ALTER TABLE | (and a dotted name being typed) is ClassifyObjectName's.
            var target = TableAfter(sig, stmtStart + 2);
            if (target == null) return null;
            var nameEnd = stmtStart + 2;
            while (nameEnd + 2 < sig.Count && sig[nameEnd + 1].Text == "." && sig[nameEnd + 2].IsName) nameEnd += 2;
            if (p < nameEnd) return null;
            if (p == nameEnd) return Options(ctx, "ALTER TABLE");

            var a = nameEnd + 1;
            // WITH CHECK|NOCHECK ADD|CHECK CONSTRAINT …
            if (sig[a].Is("WITH"))
            {
                if (p == a) return Options(ctx, "ALTER TABLE WITH");
                if (p == a + 1) return Options(ctx, "ALTER TABLE WITH CHECK");
                a += 2;
                if (p < a) return ContextKind.AliasName;
            }
            var action = sig[a];
            var t = sig[p];

            if (action.Is("ADD"))
            {
                if (p == a) return Options(ctx, "TABLE ELEMENT");
                var start = ElementStart(sig, a + 1, p);
                var nested = -1;
                var depth = 0;
                for (var i = p; i > a; i--)
                {
                    if (sig[i].Text == ")") depth++;
                    else if (sig[i].Text == "(" && depth-- == 0) { nested = i; break; }
                }
                return ClassifyElement(sig, start, p, nested, target, alter: true, ctx);
            }

            if (action.Is("ALTER"))
            {
                if (p == a) return Options(ctx, "ALTER TABLE ALTER");
                if (!sig[a + 1].Is("COLUMN")) return ContextKind.AliasName;
                if (p == a + 1) return OwnColumns(target, ctx);
                if (p == a + 2) return ContextKind.DataType;
                if (t.Is("NOT")) return Options(ctx, "NOT");
                if (t.Is("COLLATE") || t.Text == "(" || t.Text == ",") return ContextKind.AliasName;
                // Inside the type's arguments: nothing.
                return InsideParen(sig, a, p) ? ContextKind.AliasName : Options(ctx, "ALTER COLUMN");
            }

            if (action.Is("DROP"))
            {
                if (p == a) return Options(ctx, "ALTER TABLE DROP");
                var what = sig[a + 1];
                var ifExists = p >= a + 3 && sig[a + 2].Is("IF") && sig[a + 3].Is("EXISTS");
                var isNameSlot = p == a + 1 || ifExists && p == a + 3 || t.Text == ",";
                if (!isNameSlot) return ContextKind.AliasName;
                if (what.Is("COLUMN")) return OwnColumns(target, ctx);
                if (what.Is("CONSTRAINT")) return TableMembers(target, "CONSTRAINT", ctx);
                return ContextKind.AliasName;
            }

            if (action.Is("CHECK") || action.Is("NOCHECK"))
            {
                if (p == a) return Options(ctx, "CONSTRAINT");
                return t.Is("CONSTRAINT") || t.Text == "," ? TableMembers(target, "CHECK CONSTRAINT", ctx) : ContextKind.AliasName;
            }

            if (action.Is("ENABLE") || action.Is("DISABLE"))
            {
                if (p == a) return Options(ctx, "TRIGGER");
                return t.Is("TRIGGER") || t.Text == "," ? TableMembers(target, "TRIGGER", ctx) : ContextKind.AliasName;
            }
            return ContextKind.AliasName;
        }

        private static bool InsideParen(List<SqlToken> sig, int from, int p)
        {
            var depth = 0;
            for (var i = from; i <= p; i++)
            {
                if (sig[i].Text == "(") depth++;
                else if (sig[i].Text == ")") depth--;
            }
            return depth > 0;
        }

        private static ContextKind TableMembers(TableRef target, string which, CompletionContext ctx)
        {
            ctx.InsertTarget = target;
            ctx.ClauseKeyword = which;
            return ContextKind.TableMember;
        }
    }
}
