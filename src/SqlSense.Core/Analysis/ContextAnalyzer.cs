using System;
using System.Collections.Generic;
using System.Linq;
using SqlSense.Core.Lexing;
using SqlSense.Core.Schema;

namespace SqlSense.Core.Analysis
{
    public enum ContextKind
    {
        /// <summary>Inside a comment or string: no completion.</summary>
        None,
        /// <summary>Start of a statement or after a complete clause: keywords and snippets.</summary>
        Keyword,
        /// <summary>A table-like source is expected (FROM, JOIN, UPDATE, INSERT INTO …).</summary>
        TableSource,
        /// <summary>After EXEC.</summary>
        Procedure,
        /// <summary>Columns, functions, variables, keywords.</summary>
        Expression,
        /// <summary>Column list of INSERT INTO t ( … ).</summary>
        InsertColumns,
        DataType,
        Database,
        Variable,
        /// <summary>Typing an alias right after AS: nothing useful to suggest.</summary>
        AliasName,
        /// <summary>
        /// An existing module after ALTER/DROP PROCEDURE|FUNCTION|VIEW|TRIGGER (kind in ClauseKeyword), or any
        /// securable after GRANT … ON / OBJECT:: (ClauseKeyword "OBJECT").
        /// </summary>
        ObjectName,
        /// <summary>A user, role or login (which ones in <see cref="CompletionContext.PrincipalKinds"/>).</summary>
        Principal,
        /// <summary>An existing schema (ALTER SCHEMA, SCHEMA::, DEFAULT_SCHEMA = …).</summary>
        Schema,
        /// <summary>A fixed list of statement options (which one in <see cref="CompletionContext.OptionList"/>).</summary>
        Options,
        /// <summary>A file of the current database (DBCC SHRINKFILE, MODIFY FILE (NAME = …)).</summary>
        DatabaseFile,
        /// <summary>An existing index after ALTER/DROP INDEX.</summary>
        Index,
    }

    public sealed class CompletionContext
    {
        public ContextKind Kind { get; set; }

        /// <summary>What the context would be without the dotted qualifier (e.g. TableSource for "FROM dbo.|").</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>Document offset where the word being completed starts.</summary>
        public int PrefixStart { get; set; }

        /// <summary>Dotted parts before the word, unquoted (e.g. ["dbo"] for "dbo.Cu|", ["c"] for "c.Na|").</summary>
        public List<string> Qualifier { get; set; } = new List<string>();

        public List<TableRef> TablesInScope { get; set; } = new List<TableRef>();
        public List<VariableInfo> Variables { get; set; } = new List<VariableInfo>();
        public List<LocalTable> LocalTables { get; set; } = new List<LocalTable>();
        public List<LocalTable> Ctes { get; set; } = new List<LocalTable>();

        /// <summary>The keyword that set this context (FROM, JOIN, WHERE, EXEC …), upper-case.</summary>
        public string ClauseKeyword { get; set; }

        /// <summary>True when the caret directly follows ON after a JOIN; join conditions can be offered.</summary>
        public bool IsJoinCondition { get; set; }

        /// <summary>True for FROM/JOIN/APPLY, where committing a table may add an alias.</summary>
        public bool AllowsAlias { get; set; }

        /// <summary>True for INSERT [INTO] target, where committing a table may add a column list.</summary>
        public bool IsInsertTarget { get; set; }

        public TableRef InsertTarget { get; set; }

        /// <summary>
        /// ObjectName after ALTER or CREATE OR ALTER with nothing else in the batch: picking an object can
        /// script its definition. "ALTER" / "CREATE OR ALTER", else null.
        /// </summary>
        public string ScriptVerb { get; set; }

        /// <summary>Document offset of the statement's first keyword when <see cref="ScriptVerb"/> is set.</summary>
        public int StatementStart { get; set; } = -1;

        /// <summary>Line indentation at the caret, used for multi-line insertions.</summary>
        public string LineIndent { get; set; } = string.Empty;

        /// <summary>For <see cref="ContextKind.Principal"/>: the kinds of principal that fit.</summary>
        public List<DbPrincipalKind> PrincipalKinds { get; set; } = new List<DbPrincipalKind>();

        /// <summary>For <see cref="ContextKind.Options"/>: the list's key ("BACKUP WITH", "DBCC", "DATABASE SET" …).</summary>
        public string OptionList { get; set; }

        /// <summary>BACKUP/RESTORE DATABASE|LOG x: the database named by the statement, if any.</summary>
        public string TargetDatabase { get; set; }

        /// <summary>BACKUP/RESTORE LOG rather than DATABASE.</summary>
        public bool TargetIsLog { get; set; }

        /// <summary>Table names go in as a string literal (DBCC CHECKTABLE ('dbo.T')).</summary>
        public bool NameAsString { get; set; }

        /// <summary>ALTER rather than DROP INDEX: ALL is an option.</summary>
        public bool AllowsAllIndexes { get; set; }

        public bool HasQualifier => Qualifier.Count > 0;
    }

    /// <summary>Decides what kind of completion fits the caret position.</summary>
    public static partial class ContextAnalyzer
    {
        private static readonly HashSet<string> ContextKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "JOIN", "APPLY", "WHERE", "ON", "BY", "HAVING", "SET", "UPDATE", "INTO", "INSERT",
            "DELETE", "EXEC", "EXECUTE", "VALUES", "WHEN", "THEN", "ELSE", "RETURN", "IF", "WHILE", "PRINT",
            "DECLARE", "USE", "TABLE", "MERGE", "USING", "OUTPUT", "TOP", "CASE", "AND", "OR", "NOT", "IN",
            "EXISTS", "BEGIN", "END", "AS", "TRUNCATE", "OVER", "PARTITION",
        };

        private static readonly HashSet<string> ExpressionKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "WHERE", "ON", "BY", "HAVING", "SET", "VALUES", "WHEN", "THEN", "ELSE", "RETURN", "IF",
            "WHILE", "PRINT", "OUTPUT", "TOP", "CASE", "AND", "OR", "NOT", "IN", "EXISTS", "OVER", "PARTITION",
        };

        public static CompletionContext Analyze(string text, int caret, DatabaseSchema schema = null)
        {
            text = text ?? string.Empty;
            caret = Math.Max(0, Math.Min(caret, text.Length));
            var ctx = new CompletionContext { PrefixStart = caret };
            ctx.LineIndent = ScriptText.LineIndent(text, caret);

            var batch = ScriptText.GetBatchAt(text, caret);
            var rel = caret - batch.Start;
            var tokens = batch.Tokens;

            if (IsInCommentOrString(tokens, batch.Text, rel))
            {
                ctx.Kind = ContextKind.None;
                return ctx;
            }

            // Word being typed (may start with @, #, or an unclosed [).
            var wordStart = rel;
            while (wordStart > 0 && IsWordChar(batch.Text[wordStart - 1])) wordStart--;
            if (wordStart > 0 && batch.Text[wordStart - 1] == '[') wordStart--;
            ctx.Prefix = batch.Text.Substring(wordStart, rel - wordStart);
            ctx.PrefixStart = batch.Start + wordStart;

            var sig = new List<SqlToken>();
            foreach (var t in tokens)
                if (!t.IsTrivia && t.End <= wordStart) sig.Add(t);
            var allSig = ScriptText.Significant(tokens);

            // Qualifier: name.name.<word>
            var lastIdx = sig.Count - 1;
            if (lastIdx >= 0 && sig[lastIdx].Text == "." && sig[lastIdx].End == wordStart)
            {
                var parts = new List<string>();
                var k = lastIdx;
                while (k >= 0 && sig[k].Text == ".")
                {
                    if (k - 1 >= 0 && sig[k - 1].Text == ".") { parts.Insert(0, string.Empty); k--; continue; }
                    if (k - 1 < 0 || !sig[k - 1].IsName) break;
                    parts.Insert(0, sig[k - 1].Unquoted);
                    k -= 2;
                }
                ctx.Qualifier = parts;
                lastIdx = k;
            }

            // Statement containing the caret.
            var statements = StatementAnalysis.SplitStatements(allSig);
            var caretSigIndex = sig.Count; // index in allSig of first token at/after the word
            var stmt = statements.LastOrDefault(s => s.Start <= Math.Max(0, Math.Min(lastIdx, allSig.Count - 1)));
            if (statements.Count == 0) stmt = (0, -1);
            // Extend the statement to include tokens after the caret (FROM after a SELECT list being typed).
            var stmtEnd = Math.Max(stmt.End, Math.Min(caretSigIndex, allSig.Count - 1));

            if (allSig.Count > 0 && stmt.Start <= stmtEnd)
            {
                var ctes = StatementAnalysis.CollectCtes(allSig, stmt.Start, stmtEnd);
                ctx.Ctes = ctes;
                ctx.LocalTables = StatementAnalysis.CollectLocalTables(allSig, caretSigIndex);
                ctx.TablesInScope = StatementAnalysis.CollectTableRefs(allSig, stmt.Start, stmtEnd);
                // The (possibly qualified) name being typed is not yet a table in scope.
                var typingFrom = lastIdx + 1;
                ctx.TablesInScope.RemoveAll(r => r.TokenIndex >= typingFrom && r.TokenIndex <= caretSigIndex);
                Resolve(ctx.TablesInScope, ctes, ctx.LocalTables, schema);
            }
            ctx.Variables = StatementAnalysis.CollectVariables(allSig, caretSigIndex);

            if (ctx.Prefix.StartsWith("@", StringComparison.Ordinal) && !ctx.HasQualifier)
            {
                ctx.Kind = ContextKind.Variable;
                return ctx;
            }

            ctx.Kind = Classify(sig, lastIdx, stmt.Start, ctx);
            if (ctx.Kind == ContextKind.InsertColumns && ctx.ClauseKeyword == "INDEX" && ctx.InsertTarget != null)
                Resolve(new List<TableRef> { ctx.InsertTarget }, ctx.Ctes, ctx.LocalTables, schema);
            if (ctx.ScriptVerb != null)
            {
                // Only into an empty batch: re-picking the name of an ALTER that already has a body must not duplicate it.
                var wordEnd = rel;
                while (wordEnd < batch.Text.Length && (IsWordChar(batch.Text[wordEnd]) || batch.Text[wordEnd] == ']')) wordEnd++;
                if (allSig.Any(t => t.Start >= wordEnd)) ctx.ScriptVerb = null;
                else ctx.StatementStart += batch.Start;
            }
            return ctx;
        }

        /// <summary>
        /// ALTER|DROP|CREATE OR ALTER PROCEDURE|FUNCTION|VIEW|TRIGGER|TABLE [IF EXISTS] | → names of existing
        /// objects; CREATE PROCEDURE … | → a new name (nothing to suggest). Null when the pattern doesn't apply.
        /// </summary>
        private static ContextKind? ClassifyObjectName(List<SqlToken> sig, int p, CompletionContext ctx)
        {
            var i = p;
            var ifExists = i >= 2 && sig[i].Is("EXISTS") && sig[i - 1].Is("IF");
            if (ifExists) i -= 2;
            if (i < 1) return null;
            var kind = ModuleKeyword(sig[i]);
            if (kind == null) return null;

            var v = i - 1;
            string verb;
            if (sig[v].Is("DROP")) verb = "DROP";
            else if (sig[v].Is("ALTER"))
            {
                verb = "ALTER";
                if (v >= 2 && sig[v - 1].Is("OR") && sig[v - 2].Is("CREATE"))
                {
                    verb = "CREATE OR ALTER";
                    v -= 2;
                }
            }
            else if (sig[v].Is("CREATE")) return ifExists || kind == "TABLE" ? (ContextKind?)null : ContextKind.AliasName;
            else return null;
            if (ifExists && verb != "DROP") return null;

            ctx.ClauseKeyword = kind;
            if (kind == "TABLE") return verb == "CREATE OR ALTER" ? (ContextKind?)null : ContextKind.TableSource;
            if (verb != "DROP")
            {
                ctx.ScriptVerb = verb;
                ctx.StatementStart = sig[v].Start;
            }
            return ContextKind.ObjectName;
        }

        private static readonly DbPrincipalKind[] DatabasePrincipals =
            { DbPrincipalKind.User, DbPrincipalKind.DatabaseRole, DbPrincipalKind.ApplicationRole };

        private static readonly DbPrincipalKind[] ServerPrincipals = { DbPrincipalKind.Login, DbPrincipalKind.ServerRole };

        /// <summary>
        /// Security statements: ALTER|DROP ROLE|USER|LOGIN|SCHEMA|DATABASE, ALTER ROLE … ADD|DROP MEMBER,
        /// CREATE USER … FOR LOGIN, GRANT|DENY|REVOKE … ON … TO|FROM, class::name, AUTHORIZATION,
        /// DEFAULT_SCHEMA = … . Null when none applies.
        /// </summary>
        private static ContextKind? ClassifySecurity(List<SqlToken> sig, int p, int stmtStart, CompletionContext ctx)
        {
            var i = p;
            var ifExists = i >= 2 && sig[i].Is("EXISTS") && sig[i - 1].Is("IF");
            if (ifExists) i -= 2;
            if (i < stmtStart) return null;
            var t = sig[i];
            var first = sig[stmtStart];
            var isPermission = first.Is("GRANT") || first.Is("DENY") || first.Is("REVOKE");

            // class::name
            if (t.Text == "::" && i - 1 >= stmtStart && !ifExists)
            {
                var cls = sig[i - 1];
                ctx.ClauseKeyword = cls.Upper;
                if (cls.Is("OBJECT")) return ContextKind.ObjectName;
                if (ctx.HasQualifier) return null;
                if (cls.Is("SCHEMA")) return ContextKind.Schema;
                if (cls.Is("DATABASE")) return ContextKind.Database;
                if (cls.Is("USER")) return Principals(ctx, DbPrincipalKind.User);
                if (cls.Is("LOGIN")) return Principals(ctx, DbPrincipalKind.Login);
                if (cls.Is("ROLE"))
                {
                    if (i - 2 >= stmtStart && sig[i - 2].Is("SERVER")) return Principals(ctx, DbPrincipalKind.ServerRole);
                    if (i - 2 >= stmtStart && sig[i - 2].Is("APPLICATION")) return Principals(ctx, DbPrincipalKind.ApplicationRole);
                    return Principals(ctx, DbPrincipalKind.DatabaseRole);
                }
                return null;
            }

            // GRANT … ON |
            if (isPermission && t.Is("ON") && !ifExists)
            {
                ctx.ClauseKeyword = "OBJECT";
                return ContextKind.ObjectName;
            }
            if (ctx.HasQualifier) return null;

            // ALTER|DROP ROLE|USER|LOGIN|SCHEMA|DATABASE |, CREATE … | (a new name), CREATE USER u FOR|FROM LOGIN |
            if (t.Is("ROLE") || t.Is("USER") || t.Is("LOGIN") || t.Is("SCHEMA") || t.Is("DATABASE"))
            {
                var v = i - 1;
                var kind = DbPrincipalKind.User;
                if (t.Is("ROLE"))
                {
                    kind = DbPrincipalKind.DatabaseRole;
                    if (v >= stmtStart && sig[v].Is("SERVER")) { kind = DbPrincipalKind.ServerRole; v--; }
                    else if (v >= stmtStart && sig[v].Is("APPLICATION")) { kind = DbPrincipalKind.ApplicationRole; v--; }
                }
                else if (t.Is("LOGIN")) kind = DbPrincipalKind.Login;
                if (v < stmtStart) return null;
                var verb = sig[v];
                if (verb.Is("CREATE"))
                {
                    if (ifExists) return null;
                    ctx.ClauseKeyword = t.Upper;
                    return ContextKind.AliasName;
                }
                var existing = verb.Is("DROP")
                               || !ifExists && (verb.Is("ALTER") || t.Is("LOGIN") && (verb.Is("FOR") || verb.Is("FROM")));
                if (!existing) return null;
                ctx.ClauseKeyword = t.Upper;
                if (t.Is("SCHEMA")) return ContextKind.Schema;
                if (t.Is("DATABASE")) return ContextKind.Database;
                return Principals(ctx, kind);
            }
            if (ifExists) return null;

            // ALTER ROLE r ADD|DROP MEMBER |, ALTER SERVER ROLE r ADD|DROP MEMBER |
            if (t.Is("MEMBER") && i - 1 >= stmtStart && (sig[i - 1].Is("ADD") || sig[i - 1].Is("DROP")) && first.Is("ALTER"))
            {
                ctx.ClauseKeyword = "MEMBER";
                if (stmtStart + 2 < sig.Count && sig[stmtStart + 1].Is("SERVER") && sig[stmtStart + 2].Is("ROLE"))
                    return Principals(ctx, ServerPrincipals);
                if (stmtStart + 1 < sig.Count && sig[stmtStart + 1].Is("ROLE"))
                    return Principals(ctx, DbPrincipalKind.User, DbPrincipalKind.DatabaseRole);
                return null;
            }

            // WITH DEFAULT_SCHEMA = |, LOGIN = |, DEFAULT_DATABASE = |
            if (t.Text == "=" && i - 1 >= stmtStart && (first.Is("ALTER") || first.Is("CREATE")))
            {
                var option = sig[i - 1];
                if (option.Is("DEFAULT_SCHEMA")) return ContextKind.Schema;
                if (option.Is("DEFAULT_DATABASE")) return ContextKind.Database;
                if (option.Is("LOGIN")) return Principals(ctx, DbPrincipalKind.Login);
                return null;
            }

            // CREATE SCHEMA|ROLE … AUTHORIZATION |
            if (t.Is("AUTHORIZATION") && first.Is("CREATE"))
            {
                ctx.ClauseKeyword = "AUTHORIZATION";
                return stmtStart + 1 < sig.Count && sig[stmtStart + 1].Is("SERVER")
                    ? Principals(ctx, ServerPrincipals)
                    : Principals(ctx, DatabasePrincipals);
            }

            // GRANT … TO |, REVOKE … FROM |, … TO a, |, ALTER AUTHORIZATION ON x TO |
            var isAuthorization = first.Is("ALTER") && stmtStart + 1 < sig.Count && sig[stmtStart + 1].Is("AUTHORIZATION");
            if (!isPermission && !isAuthorization) return null;
            var grantee = -1;
            if (t.Is("TO") || isPermission && (t.Is("FROM") || t.Is("AS"))) grantee = i;
            else if (t.Text == ",")
            {
                for (var k = i - 1; k > stmtStart; k--)
                {
                    if (sig[k].Is("ON") || sig[k].Text == ")" || sig[k].Text == "(") break;
                    if (sig[k].Is("TO") || sig[k].Is("FROM")) { grantee = k; break; }
                }
            }
            if (grantee < 0) return null;
            ctx.ClauseKeyword = sig[grantee].Upper;
            // Server permissions (VIEW SERVER STATE, ALTER ANY LOGIN, ON LOGIN::…, ON ENDPOINT::…) go to logins.
            for (var k = stmtStart + 1; k < grantee; k++)
                if (sig[k].Is("SERVER") || sig[k].Is("LOGIN") || sig[k].Is("ENDPOINT"))
                    return Principals(ctx, ServerPrincipals);
            return Principals(ctx, DatabasePrincipals);
        }

        private static ContextKind Principals(CompletionContext ctx, params DbPrincipalKind[] kinds)
        {
            ctx.PrincipalKinds = kinds.ToList();
            return ContextKind.Principal;
        }

        private static string ModuleKeyword(SqlToken t)
        {
            if (t.Is("PROCEDURE") || t.Is("PROC")) return "PROCEDURE";
            if (t.Is("FUNCTION")) return "FUNCTION";
            if (t.Is("VIEW")) return "VIEW";
            if (t.Is("TRIGGER")) return "TRIGGER";
            if (t.Is("TABLE")) return "TABLE";
            return null;
        }

        private static ContextKind Classify(List<SqlToken> sig, int p, int stmtStart, CompletionContext ctx)
        {
            if (p < 0 || p < stmtStart && !ctx.HasQualifier) return ContextKind.Keyword;
            var prev = sig[p];
            if (prev.Text == ";") return ContextKind.Keyword;
            var maintenance = ClassifyMaintenance(sig, p, stmtStart, ctx);
            if (maintenance != null) return maintenance.Value;
            var security = ClassifySecurity(sig, p, stmtStart, ctx);
            if (security != null) return security.Value;
            var objectName = ClassifyObjectName(sig, p, ctx);
            if (objectName != null) return objectName.Value;

            // Walk back to the governing keyword at this nesting depth.
            var depth = 0;
            var opener = -1;
            var kwIdx = -1;
            for (var i = p; i >= 0 && i >= stmtStart; i--)
            {
                var t = sig[i];
                if (t.Text == ")") { depth++; continue; }
                if (t.Text == "(")
                {
                    if (depth == 0) { opener = i; break; }
                    depth--;
                    continue;
                }
                if (depth == 0 && t.IsWord && ContextKeywords.Contains(t.Text))
                {
                    // BETWEEN … AND: the AND belongs to the expression; keep walking.
                    kwIdx = i;
                    break;
                }
            }

            if (kwIdx < 0 && opener >= 0)
            {
                return ClassifyParen(sig, opener, p, ctx);
            }
            if (kwIdx < 0) return ctx.HasQualifier ? ContextKind.Expression : ContextKind.Keyword;

            var kw = sig[kwIdx].Upper;
            ctx.ClauseKeyword = kw;
            var directlyAfter = kwIdx == p;

            switch (kw)
            {
                case "FROM":
                case "JOIN":
                case "APPLY":
                case "USING":
                case "MERGE":
                    if (directlyAfter || prev.Text == ",")
                    {
                        // DELETE FROM t takes no alias; MERGE aliases come after INTO/USING handling.
                        ctx.AllowsAlias = kw != "MERGE" && !(kw == "FROM" && kwIdx > 0 && sig[kwIdx - 1].Is("DELETE"));
                        return ContextKind.TableSource;
                    }
                    return prev.Is("AS") ? ContextKind.AliasName : ContextKind.Keyword;
                case "TABLE":
                    if (directlyAfter && kwIdx > 0 && (sig[kwIdx - 1].Is("TRUNCATE") || sig[kwIdx - 1].Is("DROP") || sig[kwIdx - 1].Is("ALTER")))
                        return ContextKind.TableSource;
                    return ContextKind.Keyword;
                case "TRUNCATE":
                    return ContextKind.Keyword;
                case "UPDATE":
                    return directlyAfter ? ContextKind.TableSource : ContextKind.Keyword;
                case "DELETE":
                    return directlyAfter ? ContextKind.TableSource : ContextKind.Keyword;
                case "INSERT":
                    if (directlyAfter)
                    {
                        ctx.IsInsertTarget = true;
                        return ContextKind.TableSource;
                    }
                    return ContextKind.Keyword;
                case "INTO":
                    if (!directlyAfter) return ContextKind.Keyword;
                    ctx.IsInsertTarget = kwIdx > 0 && sig[kwIdx - 1].Is("INSERT");
                    ctx.AllowsAlias = kwIdx > 0 && sig[kwIdx - 1].Is("MERGE");
                    return ContextKind.TableSource;
                case "EXEC":
                case "EXECUTE":
                    return directlyAfter ? ContextKind.Procedure : ContextKind.Expression;
                case "USE":
                    return directlyAfter ? ContextKind.Database : ContextKind.Keyword;
                case "DECLARE":
                    if (prev.Kind == TokenKind.Variable) return ContextKind.DataType;
                    if (prev.Is("AS") && p > 0 && sig[p - 1].Kind == TokenKind.Variable) return ContextKind.DataType;
                    // "DECLARE |" is a new variable name: nothing to suggest.
                    return directlyAfter ? ContextKind.AliasName : ContextKind.Expression;
                case "AS":
                    if (directlyAfter)
                    {
                        // CAST(x AS |) → data type; "FROM t AS |" → alias; "CREATE … AS |" → keyword.
                        if (opener >= 0 || IsInsideCast(sig, kwIdx)) return ContextKind.DataType;
                        if (stmtStart < sig.Count && (sig[stmtStart].Is("CREATE") || sig[stmtStart].Is("ALTER"))
                            && StatementAnalysis.FindWordAtDepth(sig, stmtStart, kwIdx, "SELECT") < 0)
                            return ContextKind.Keyword;
                        if (kwIdx > 0 && (sig[kwIdx - 1].IsName || sig[kwIdx - 1].Text == ")")) return ContextKind.AliasName;
                        return ContextKind.Keyword;
                    }
                    return ContextKind.Expression;
                case "BEGIN":
                case "END":
                    return ContextKind.Keyword;
                case "ON":
                    // CREATE/ALTER TRIGGER t ON | → the table or view.
                    if (directlyAfter && stmtStart < kwIdx && (sig[stmtStart].Is("CREATE") || sig[stmtStart].Is("ALTER"))
                        && StatementAnalysis.FindWordAtDepth(sig, stmtStart, kwIdx, "TRIGGER") >= 0)
                        return ContextKind.TableSource;
                    ctx.IsJoinCondition = directlyAfter && IsJoinOn(sig, kwIdx);
                    return ContextKind.Expression;
                default:
                    if (ExpressionKeywords.Contains(kw)) return ContextKind.Expression;
                    return ContextKind.Keyword;
            }
        }

        private static ContextKind ClassifyParen(List<SqlToken> sig, int opener, int p, CompletionContext ctx)
        {
            // INSERT INTO t ( | → column list
            var k = opener - 1;
            var nameEnd = k;
            while (k >= 0 && (sig[k].IsName || sig[k].Text == ".")) k--;
            if (nameEnd > k && k >= 0 && sig[k].Is("INTO") && k > 0 && sig[k - 1].Is("INSERT")
                || nameEnd > k && k >= 0 && sig[k].Is("INSERT"))
            {
                var refs = ctx.TablesInScope;
                ctx.InsertTarget = refs.FirstOrDefault(r => r.TokenIndex == k + 1) ?? refs.FirstOrDefault();
                ctx.ClauseKeyword = "INSERT";
                return ContextKind.InsertColumns;
            }
            // CREATE TABLE x ( | , DECLARE @t TABLE ( | → defining columns; nothing to suggest yet.
            if (nameEnd > k && k >= 0 && sig[k].Is("TABLE")) return ContextKind.AliasName;
            if (opener > 0 && sig[opener - 1].Is("TABLE")) return ContextKind.AliasName;
            ctx.ClauseKeyword = "(";
            return ContextKind.Expression;
        }

        private static bool IsInsideCast(List<SqlToken> sig, int asIdx)
        {
            var depth = 0;
            for (var i = asIdx - 1; i >= 0; i--)
            {
                if (sig[i].Text == ")") depth++;
                else if (sig[i].Text == "(")
                {
                    if (depth == 0) return i > 0 && (sig[i - 1].Is("CAST") || sig[i - 1].Is("TRY_CAST"));
                    depth--;
                }
            }
            return false;
        }

        private static bool IsJoinOn(List<SqlToken> sig, int onIdx)
        {
            var depth = 0;
            for (var i = onIdx - 1; i >= 0; i--)
            {
                var t = sig[i];
                if (t.Text == ")") depth++;
                else if (t.Text == "(") { if (depth == 0) return false; depth--; }
                else if (depth == 0 && t.Is("JOIN")) return true;
                else if (depth == 0 && (t.Is("WHERE") || t.Is("SELECT") || t.Is("ON"))) return false;
            }
            return false;
        }

        /// <summary>Attaches schema objects, CTEs, and temp tables to the references.</summary>
        public static void Resolve(List<TableRef> refs, List<LocalTable> ctes, List<LocalTable> locals, DatabaseSchema schema)
        {
            foreach (var r in refs)
            {
                if (r.Source == TableRefSource.Derived) continue;
                var cte = string.IsNullOrEmpty(r.Schema) ? ctes.FirstOrDefault(c => Eq(c.Name, r.Name)) : null;
                if (cte != null)
                {
                    r.Source = TableRefSource.Cte;
                    r.LocalColumns = cte.Columns;
                    continue;
                }
                var local = locals.LastOrDefault(l => Eq(l.Name, r.Name) && (r.Schema == null || r.Name.StartsWith("#", StringComparison.Ordinal) == false));
                if (local != null && (r.Name.StartsWith("#", StringComparison.Ordinal) || r.Name.StartsWith("@", StringComparison.Ordinal) || schema?.Find(r.Schema, r.Name) == null))
                {
                    r.Source = TableRefSource.LocalTable;
                    r.LocalColumns = local.Columns;
                    continue;
                }
                // Another database or a linked server: its snapshot when loaded (Resolver never blocks).
                r.Object = schema?.For(r.Server, r.Database)?.Find(r.Schema, r.Name);
            }

            // "UPDATE a SET … FROM dbo.T a": the UPDATE target is an alias of a FROM source.
            refs.RemoveAll(r => r.Object == null && r.Source == TableRefSource.Database && r.Alias == null
                                && refs.Any(o => o != r && Eq(o.Alias, r.Name)));
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';

        private static bool IsInCommentOrString(List<SqlToken> tokens, string text, int rel)
        {
            var idx = ScriptText.TokenIndexBefore(tokens, rel);
            if (idx >= 0)
            {
                var t = tokens[idx];
                if (t.Kind == TokenKind.Comment)
                {
                    if (t.Type == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.SingleLineComment)
                        return rel <= t.End && !t.Text.EndsWith("\n", StringComparison.Ordinal) || rel < t.End;
                    return rel < t.End || !t.Text.EndsWith("*/", StringComparison.Ordinal);
                }
                if (t.Kind == TokenKind.StringLiteral)
                {
                    if (rel < t.End) return true;
                    if (t.Text.Length < 2 || !t.Text.EndsWith("'", StringComparison.Ordinal)) return true;
                }
            }
            // Unterminated string on the current line: odd number of quotes before the caret.
            var lineStart = rel;
            while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;
            var quotes = 0;
            for (var i = lineStart; i < rel; i++)
            {
                if (text[i] == '-' && i + 1 < rel && text[i + 1] == '-' && quotes % 2 == 0) return true;
                if (text[i] == '\'') quotes++;
            }
            return quotes % 2 == 1;
        }
    }
}
