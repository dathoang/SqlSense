using System.Collections.Generic;
using SqlSense.Core.Lexing;

namespace SqlSense.Core.Analysis
{
    public static partial class ContextAnalyzer
    {
        private static readonly HashSet<string> DbccDatabaseCommands = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "CHECKDB", "CHECKALLOC", "CHECKCATALOG", "CHECKFILEGROUP", "SHRINKDATABASE", "UPDATEUSAGE", "OPENTRAN", "CLONEDATABASE",
        };

        private static readonly HashSet<string> DbccTableCommands = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "CHECKTABLE", "CHECKIDENT", "CHECKCONSTRAINTS", "SHOW_STATISTICS", "CLEANTABLE",
        };

        /// <summary>
        /// Maintenance statements: BACKUP/RESTORE (DATABASE|LOG name, TO/FROM device, WITH options), DBCC commands
        /// and their arguments, ALTER DATABASE … SET|MODIFY FILE, ALTER/DROP/CREATE INDEX, UPDATE STATISTICS.
        /// Null when none applies.
        /// </summary>
        private static ContextKind? ClassifyMaintenance(List<SqlToken> sig, int p, int stmtStart, CompletionContext ctx)
        {
            if (p < stmtStart) return null;
            var t = sig[p];
            var first = sig[stmtStart];

            // ALTER INDEX x ON dbo.|, UPDATE STATISTICS dbo.| — table names may be qualified.
            var tableTarget = ClassifyIndexTable(sig, p, stmtStart, ctx);
            if (tableTarget != null) return tableTarget;
            if (ctx.HasQualifier) return null;

            if (first.Is("BACKUP") || first.Is("RESTORE")) return ClassifyBackup(sig, p, stmtStart, ctx);

            if (first.Is("DBCC"))
            {
                if (p == stmtStart) return Options(ctx, "DBCC");
                if (p == stmtStart + 2 && t.Text == "(")
                {
                    var command = sig[stmtStart + 1];
                    ctx.ClauseKeyword = command.Upper;
                    if (DbccDatabaseCommands.Contains(command.Text)) return ContextKind.Database;
                    if (DbccTableCommands.Contains(command.Text))
                    {
                        ctx.NameAsString = true;
                        return ContextKind.TableSource;
                    }
                    if (command.Is("SHRINKFILE")) return ContextKind.DatabaseFile;
                }
                return p > stmtStart && t.Is("WITH") || t.Text == "," && IsAfterWith(sig, p, stmtStart) ? Options(ctx, "DBCC WITH") : (ContextKind?)null;
            }

            if (first.Is("ALTER") && stmtStart + 2 < sig.Count && sig[stmtStart + 1].Is("DATABASE") && p >= stmtStart + 2)
            {
                if (p == stmtStart + 2) return Options(ctx, "ALTER DATABASE");
                if (p == stmtStart + 3 && t.Is("SET")) return Options(ctx, "DATABASE SET");
                if (t.Is("RECOVERY") && p == stmtStart + 4) return Options(ctx, "RECOVERY");
                // MODIFY FILE (NAME = |
                if (t.Text == "=" && p >= 3 && sig[p - 1].Is("NAME") && sig[p - 2].Text == "(" && sig[p - 3].Is("FILE")) return ContextKind.DatabaseFile;
                return null;
            }

            // ALTER INDEX |, DROP INDEX [IF EXISTS] |
            var i = p;
            if (i >= 2 && sig[i].Is("EXISTS") && sig[i - 1].Is("IF")) i -= 2;
            if (i == stmtStart + 1 && sig[i].Is("INDEX") && (first.Is("ALTER") || first.Is("DROP")))
            {
                ctx.ClauseKeyword = "INDEX";
                ctx.AllowsAllIndexes = first.Is("ALTER") && i == p;
                return ContextKind.Index;
            }

            // ALTER INDEX x ON t |
            if (first.Is("ALTER") && stmtStart + 3 < sig.Count && sig[stmtStart + 1].Is("INDEX") && sig[stmtStart + 3].Is("ON")
                && p > stmtStart + 3 && EndsName(sig, stmtStart + 4, p))
                return Options(ctx, "ALTER INDEX");
            // UPDATE STATISTICS t |
            if (first.Is("UPDATE") && stmtStart + 1 < sig.Count && sig[stmtStart + 1].Is("STATISTICS") && p > stmtStart + 1
                && EndsName(sig, stmtStart + 2, p))
                return Options(ctx, "UPDATE STATISTICS");

            return ClassifyIndexColumns(sig, p, stmtStart, ctx);
        }

        /// <summary>ALTER|DROP INDEX x ON |, CREATE … INDEX x ON |, UPDATE STATISTICS | → tables and views.</summary>
        private static ContextKind? ClassifyIndexTable(List<SqlToken> sig, int p, int stmtStart, CompletionContext ctx)
        {
            var t = sig[p];
            var first = sig[stmtStart];
            if (t.Is("STATISTICS") && p == stmtStart + 1 && first.Is("UPDATE")
                || t.Is("ON") && (first.Is("ALTER") || first.Is("DROP")) && p == stmtStart + 3 && sig[stmtStart + 1].Is("INDEX")
                || t.Is("ON") && first.Is("CREATE") && p >= 2 && sig[p - 2].Is("INDEX"))
            {
                ctx.ClauseKeyword = "INDEX";
                return ContextKind.TableSource;
            }
            return null;
        }

        /// <summary>CREATE INDEX x ON t (|, …, INCLUDE (| → columns of t.</summary>
        private static ContextKind? ClassifyIndexColumns(List<SqlToken> sig, int p, int stmtStart, CompletionContext ctx)
        {
            if (!sig[stmtStart].Is("CREATE") || !(sig[p].Text == "(" || sig[p].Text == ",")) return null;
            var opener = p;
            var depth = 0;
            for (; opener > stmtStart; opener--)
            {
                if (sig[opener].Text == ")") depth++;
                else if (sig[opener].Text == "(" && depth-- == 0) break;
            }
            if (opener <= stmtStart || sig[opener].Text != "(") return null;

            int on;
            if (sig[opener - 1].Is("INCLUDE")) on = StatementAnalysis.FindWordAtDepth(sig, stmtStart, opener, "ON");
            else
            {
                on = opener - 1;
                while (on > stmtStart && (sig[on].IsName || sig[on].Text == ".")) on--;
                if (on == opener - 1) return null;
            }
            if (on < 2 || !sig[on].Is("ON") || !sig[on - 2].Is("INDEX")) return null;

            var target = TableAfter(sig, on + 1);
            if (target == null) return null;
            ctx.InsertTarget = target;
            ctx.ClauseKeyword = "INDEX";
            return ContextKind.InsertColumns;
        }

        private static TableRef TableAfter(List<SqlToken> sig, int j)
        {
            if (j >= sig.Count || !sig[j].IsName) return null;
            var parts = new List<string> { sig[j].Unquoted };
            var k = j + 1;
            while (k + 1 < sig.Count && sig[k].Text == "." && sig[k + 1].IsName)
            {
                parts.Add(sig[k + 1].Unquoted);
                k += 2;
            }
            var r = new TableRef { TokenIndex = j, Source = TableRefSource.Database, Name = parts[parts.Count - 1] };
            if (parts.Count >= 2) r.Schema = parts[parts.Count - 2];
            if (parts.Count >= 3) r.Database = parts[parts.Count - 3];
            return r;
        }

        private static ContextKind? ClassifyBackup(List<SqlToken> sig, int p, int stmtStart, CompletionContext ctx)
        {
            var t = sig[p];
            var first = sig[stmtStart];
            var verb = first.Upper;
            if (p == stmtStart) return Options(ctx, verb);

            var kindToken = stmtStart + 1 < sig.Count ? sig[stmtStart + 1] : null;
            ctx.TargetIsLog = kindToken != null && kindToken.Is("LOG");
            if (kindToken != null && (kindToken.Is("DATABASE") || kindToken.Is("LOG")) && stmtStart + 2 <= p && sig[stmtStart + 2].IsName)
                ctx.TargetDatabase = sig[stmtStart + 2].Unquoted;

            // BACKUP|RESTORE DATABASE|LOG |
            if (p == stmtStart + 1 && (t.Is("DATABASE") || t.Is("LOG")))
            {
                ctx.ClauseKeyword = verb;
                return ContextKind.Database;
            }

            var with = StatementAnalysis.FindWordAtDepth(sig, stmtStart, p, "WITH");
            if (with >= 0 && p >= with)
            {
                // RESTORE … WITH MOVE N'x' TO | → a path, nothing to suggest.
                if (t.Is("TO")) return ContextKind.AliasName;
                if (t.Is("WITH") || t.Text == ",") return Options(ctx, verb + " WITH");
                return null;
            }

            // BACKUP … TO |, RESTORE … FROM |, and after a comma (striped backups).
            var device = verb == "BACKUP" ? "TO" : "FROM";
            if (t.Is(device) || t.Text == "," && StatementAnalysis.FindWordAtDepth(sig, stmtStart, p, device) >= 0)
                return Options(ctx, verb + " " + device);
            return null;
        }

        private static bool IsAfterWith(List<SqlToken> sig, int p, int stmtStart) =>
            StatementAnalysis.FindWordAtDepth(sig, stmtStart, p, "WITH") >= 0;

        /// <summary>True when sig[from..p] is just a (dotted) name.</summary>
        private static bool EndsName(List<SqlToken> sig, int from, int p)
        {
            if (from > p || !sig[p].IsName) return false;
            for (var k = from; k <= p; k++)
                if (!(sig[k].IsName || sig[k].Text == ".")) return false;
            return true;
        }

        private static ContextKind Options(CompletionContext ctx, string list)
        {
            ctx.OptionList = list;
            ctx.ClauseKeyword = list;
            return ContextKind.Options;
        }
    }
}
