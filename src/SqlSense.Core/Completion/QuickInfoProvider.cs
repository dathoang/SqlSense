using System;
using System.Collections.Generic;
using System.Linq;
using SqlSense.Core.Analysis;
using SqlSense.Core.Language;
using SqlSense.Core.Lexing;
using SqlSense.Core.Schema;

namespace SqlSense.Core.Completion
{
    /// <summary>What the editor shows when the mouse rests on a name.</summary>
    public sealed class QuickInfo
    {
        public QuickInfo(int start, int length, string kind, string name)
        {
            Start = start;
            Length = length;
            Kind = kind;
            Name = name;
        }

        /// <summary>Document span of the word the info is about.</summary>
        public int Start { get; }
        public int Length { get; }

        /// <summary>"table", "column", "database role" …</summary>
        public string Kind { get; }
        public string Name { get; }

        /// <summary>Further lines: columns, parameters, members …</summary>
        public List<string> Lines { get; } = new List<string>();

        public string Title => Kind + " " + Name;

        public override string ToString() => Lines.Count == 0 ? Title : Title + "\n" + string.Join("\n", Lines);
    }

    /// <summary>Describes the object, column, variable or principal under a document position.</summary>
    public static class QuickInfoProvider
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
        private const int MaxLines = 30;

        public static QuickInfo Get(string text, int position, DatabaseSchema schema)
        {
            text = text ?? string.Empty;
            if (position < 0 || position >= text.Length) return null;
            var batch = ScriptText.GetBatchAt(text, position);
            var rel = position - batch.Start;
            var tok = batch.Tokens.FirstOrDefault(t => t.Start <= rel && rel < t.End);
            if (tok == null || !(tok.IsName || tok.Kind == TokenKind.Variable || tok.Kind == TokenKind.Keyword)) return null;

            var sig = ScriptText.Significant(batch.Tokens);
            var k = sig.IndexOf(tok);
            if (k < 0) return null;
            var start = batch.Start + tok.Start;

            if (tok.Kind == TokenKind.Variable) return Variable(tok, sig, start);

            // The dotted name around the word: a.b.c with the hovered part at index h.
            var first = k;
            while (first >= 2 && sig[first - 1].Text == "." && sig[first - 1].Start == sig[first - 2].End && sig[first - 2].IsName) first -= 2;
            var last = k;
            while (last + 2 < sig.Count && sig[last + 1].Text == "." && sig[last + 1].Start == sig[last].End && sig[last + 2].IsName) last += 2;
            var parts = new List<string>();
            for (var i = first; i <= last; i += 2) parts.Add(sig[i].Unquoted);
            var h = (k - first) / 2;
            var n = parts.Count;
            var isCall = last + 1 < sig.Count && sig[last + 1].Text == "(";
            var name = parts[h];

            if (tok.Kind == TokenKind.Keyword)
            {
                // Built-in functions the lexer treats as keywords (LEFT, CONVERT, COALESCE …).
                return n == 1 && isCall ? BuiltIn(name, start, tok.Text.Length) : null;
            }

            // Context of the name's start: tells principals and schemas apart from tables and columns.
            var ctx = ContextAnalyzer.Analyze(text, batch.Start + sig[first].Start, schema);
            if (n == 1)
            {
                if (ctx.Kind == ContextKind.Principal)
                {
                    var p = schema?.Principals.FirstOrDefault(x => Ci.Equals(x.Name, name) && ctx.PrincipalKinds.Contains(x.Kind))
                            ?? schema?.FindPrincipal(name);
                    return p != null ? Principal(p, start, tok.Text.Length) : null;
                }
                if (ctx.Kind == ContextKind.Schema) return SchemaInfo(schema, name, start, tok.Text.Length);
                if (ctx.Kind == ContextKind.Database) return DatabaseInfo(schema, name, start, tok.Text.Length);
                if (ctx.Kind == ContextKind.Index) return IndexInfo(schema, sig, k, name, start, tok.Text.Length);
            }

            // Tables in scope for the statement holding the word.
            var statements = StatementAnalysis.SplitStatements(sig);
            var stmt = statements.LastOrDefault(s => s.Start <= k);
            if (statements.Count == 0) stmt = (0, sig.Count - 1);
            var ctes = StatementAnalysis.CollectCtes(sig, stmt.Start, stmt.End);
            var locals = StatementAnalysis.CollectLocalTables(sig, k);
            var refs = StatementAnalysis.CollectTableRefs(sig, stmt.Start, stmt.End);
            ContextAnalyzer.Resolve(refs, ctes, locals, schema);

            var len = tok.Text.Length;
            if (h < n - 1) return Qualifier(parts, h, refs, schema, start, len);

            if (n == 1)
            {
                if (isCall)
                {
                    var fn = schema?.Find(null, name);
                    if (fn != null && (fn.Kind == DbObjectKind.ScalarFunction || fn.Kind == DbObjectKind.TableFunction)) return Object(fn, schema, start, len);
                    var builtIn = BuiltIn(name, start, len);
                    if (builtIn != null) return builtIn;
                }
                var tableFirst = ctx.Kind == ContextKind.TableSource || ctx.Kind == ContextKind.Procedure || ctx.Kind == ContextKind.ObjectName;
                var own = refs.FirstOrDefault(r => r.TokenIndex == k);
                if (own != null) return TableRefInfo(own, schema, start, len);
                if (!tableFirst)
                {
                    var alias = refs.FirstOrDefault(r => Ci.Equals(r.Alias, name));
                    if (alias != null) return TableRefInfo(alias, schema, start, len, name);
                    var column = Column(refs, name, start, len);
                    if (column != null) return column;
                }
                var cte = ctes.FirstOrDefault(c => Ci.Equals(c.Name, name));
                if (cte != null) return Local("CTE", cte, start, len);
                var local = locals.LastOrDefault(l => Ci.Equals(l.Name, name));
                if (local != null) return Local(name.StartsWith("#", StringComparison.Ordinal) ? "temp table" : "table variable", local, start, len);
                var obj = schema?.Find(null, name);
                if (obj != null) return Object(obj, schema, start, len);
                if (schema != null && schema.IsSchema(name)) return SchemaInfo(schema, name, start, len);
                var principal = schema?.FindPrincipal(name);
                if (principal != null && ctx.Kind != ContextKind.Expression) return Principal(principal, start, len);
                return null;
            }

            // Last part of a dotted name.
            if (n == 2)
            {
                var r = refs.FirstOrDefault(x => x.Matches(parts[0])) ?? refs.FirstOrDefault(x => Ci.Equals(x.Name, parts[0]));
                if (r != null && r.TokenIndex != first)
                {
                    var column = Column(new[] { r }, name, start, len);
                    if (column != null) return column;
                }
            }
            return Dotted(parts, h, schema, start, len);
        }

        private static QuickInfo Qualifier(List<string> parts, int h, List<TableRef> refs, DatabaseSchema schema, int start, int len)
        {
            var name = parts[h];
            if (h == 0 && parts.Count == 2)
            {
                var r = refs.FirstOrDefault(x => x.Matches(name)) ?? refs.FirstOrDefault(x => Ci.Equals(x.Name, name) && x.Schema == null);
                if (r != null) return TableRefInfo(r, schema, start, len, Ci.Equals(r.Alias, name) ? name : null);
            }
            return Dotted(parts, h, schema, start, len);
        }

        /// <summary>
        /// One part of [server.][database.][schema.]object[.column]: tries the hovered part as an object, a
        /// column, a schema, a database and a linked server, counting the parts before it.
        /// </summary>
        private static QuickInfo Dotted(List<string> parts, int h, DatabaseSchema schema, int start, int len)
        {
            if (schema == null) return null;
            var n = parts.Count;
            var after = n - 1 - h;
            var name = parts[h];
            string Part(int i) => i >= 0 && parts[i].Length > 0 ? parts[i] : null;
            // The snapshot for a database at index i (its server at i - 1); the current one when i < 0.
            DatabaseSchema DbAt(int i) => i < 0 ? schema : schema.For(Part(i - 1), Part(i) ?? schema.DatabaseName);

            if (after <= 1)
            {
                var obj = DbAt(h - 2)?.Find(Part(h - 1), name);
                if (obj != null && h <= 3) return Object(obj, DbAt(h - 2), start, len);
            }
            if (after == 0 && h >= 1)
            {
                var table = DbAt(h - 3)?.Find(Part(h - 2), parts[h - 1]);
                var col = table?.Columns.FirstOrDefault(c => Ci.Equals(c.Name, name));
                if (col != null) return ColumnInfo(table.QualifiedName, col, start, len);
            }
            if (after == 1 || after == 2)
            {
                var db = DbAt(h - 1);
                if (db != null && db.IsSchema(name)) return SchemaInfo(db, name, start, len);
            }
            if (after == 2 || after == 3)
            {
                var catalog = h >= 1 ? schema.For(parts[h - 1], null) : schema;
                if (catalog != null && h >= 1)
                {
                    if (!catalog.IsDatabase(name)) return null;
                    var info = new QuickInfo(start, len, "database", name);
                    info.Lines.Add("on linked server " + parts[h - 1]);
                    return info;
                }
                var local = DatabaseInfo(schema, name, start, len);
                if (local != null) return local;
            }
            if ((after == 3 || after == 4) && h == 0 && schema.IsLinkedServer(name))
            {
                var info = new QuickInfo(start, len, "linked server", name);
                var catalog = schema.For(name, null);
                if (catalog != null) info.Lines.Add(catalog.OtherDatabases.Count + " databases");
                return info;
            }
            return null;
        }

        // ---------------- descriptions ----------------

        private static QuickInfo Variable(SqlToken tok, List<SqlToken> sig, int start)
        {
            if (tok.Text.StartsWith("@@", StringComparison.Ordinal)) return BuiltIn(tok.Text, start, tok.Text.Length);
            var v = StatementAnalysis.CollectVariables(sig, sig.Count).FirstOrDefault(x => Ci.Equals(x.Name, tok.Text));
            var info = new QuickInfo(start, tok.Text.Length, "variable", tok.Text);
            if (v?.DataType != null) info.Lines.Add(v.DataType);
            return info;
        }

        private static QuickInfo BuiltIn(string name, int start, int len)
        {
            var f = SqlVocabulary.Functions.FirstOrDefault(x => Ci.Equals(x.Name, name));
            if (f == null) return null;
            var info = new QuickInfo(start, len, "function", f.Signature);
            if (!string.IsNullOrEmpty(f.Description)) info.Lines.Add(f.Description);
            return info;
        }

        private static QuickInfo Column(IEnumerable<TableRef> refs, string name, int start, int len)
        {
            foreach (var r in refs)
            {
                var dbCol = r.Object?.Columns.FirstOrDefault(c => Ci.Equals(c.Name, name));
                if (dbCol != null) return ColumnInfo(r.Object.QualifiedName, dbCol, start, len);
                var localCol = r.Object == null ? r.ColumnNames.FirstOrDefault(c => Ci.Equals(c, name)) : null;
                if (localCol != null)
                {
                    var info = new QuickInfo(start, len, "column", (r.Name ?? r.Alias) + "." + localCol);
                    info.Lines.Add(r.Source == TableRefSource.Cte ? "of CTE " + r.Name
                        : r.Source == TableRefSource.Derived ? "of derived table " + r.Alias
                        : "of " + r.Name);
                    return info;
                }
            }
            return null;
        }

        private static QuickInfo ColumnInfo(string table, DbColumn c, int start, int len)
        {
            var info = new QuickInfo(start, len, "column", table + "." + c.Name);
            info.Lines.Add(ColumnText(c));
            return info;
        }

        private static string ColumnText(DbColumn c) =>
            c.DataType + (c.Nullable ? " null" : " not null") + (c.IsIdentity ? " identity" : "") + (c.IsComputed ? " computed" : "");

        private static QuickInfo TableRefInfo(TableRef r, DatabaseSchema schema, int start, int len, string alias = null)
        {
            QuickInfo info;
            if (r.Object != null) info = Object(r.Object, schema, start, len);
            else if (r.Source == TableRefSource.Derived) info = new QuickInfo(start, len, "derived table", r.Alias ?? "");
            else
            {
                var kind = r.Source == TableRefSource.Cte ? "CTE"
                    : r.Source == TableRefSource.LocalTable ? (r.Name.StartsWith("@", StringComparison.Ordinal) ? "table variable" : "temp table")
                    : "table";
                info = new QuickInfo(start, len, kind, r.Name);
            }
            if (r.Object == null && r.LocalColumns != null && r.LocalColumns.Count > 0)
                AddLimited(info.Lines, r.LocalColumns.Select(c => "  " + c));
            if (alias != null) info.Lines.Insert(0, "alias " + alias);
            return info;
        }

        private static QuickInfo Local(string kind, LocalTable t, int start, int len)
        {
            var info = new QuickInfo(start, len, kind, t.Name);
            AddLimited(info.Lines, t.Columns.Select(c => "  " + c));
            return info;
        }

        public static QuickInfo Object(DbObject o, DatabaseSchema schema, int start, int len)
        {
            var info = new QuickInfo(start, len, KindText(o.Kind), o.QualifiedName);
            switch (o.Kind)
            {
                case DbObjectKind.Procedure:
                case DbObjectKind.ScalarFunction:
                case DbObjectKind.TableFunction:
                    if (o.Parameters.Count == 0) info.Lines.Add("no parameters");
                    AddLimited(info.Lines, o.Parameters.OrderBy(p => p.Ordinal).Select(p =>
                        "  " + p.Name + " " + p.DataType + (p.IsOutput ? " OUTPUT" : "") + (p.HasDefault ? " = default" : "")));
                    if (o.Kind == DbObjectKind.TableFunction && o.Columns.Count > 0)
                    {
                        info.Lines.Add("returns table");
                        AddLimited(info.Lines, o.Columns.OrderBy(c => c.Ordinal).Select(c => "  " + c.Name + " " + c.DataType));
                    }
                    break;
                case DbObjectKind.Trigger:
                    if (o.Parent != null) info.Lines.Add("on " + o.Parent);
                    break;
                default:
                    AddLimited(info.Lines, o.Columns.OrderBy(c => c.Ordinal).Select(c => "  " + c.Name + " " + ColumnText(c)));
                    if (schema != null && o.Kind == DbObjectKind.Table)
                    {
                        var fks = schema.ForeignKeys.Where(f => Ci.Equals(f.FromSchema, o.Schema) && Ci.Equals(f.FromTable, o.Name)).Take(10).ToList();
                        if (fks.Count > 0) info.Lines.Add("foreign keys");
                        foreach (var f in fks)
                            info.Lines.Add($"  ({string.Join(", ", f.FromColumns)}) → {f.ToSchema}.{f.ToTable} ({string.Join(", ", f.ToColumns)})");
                    }
                    if (o.Indexes.Count > 0) info.Lines.Add("indexes");
                    AddLimited(info.Lines, o.Indexes.Select(ix => "  " + ix.Name + ": " + ix.Describe()));
                    break;
            }
            return info;
        }

        public static QuickInfo Principal(DbPrincipal p, int start, int len)
        {
            var info = new QuickInfo(start, len, p.KindText, p.Name);
            if (!string.IsNullOrEmpty(p.TypeDescription) && !p.IsRole) info.Lines.Add("type: " + p.TypeDescription);
            if (p.IsFixedRole) info.Lines.Add("built-in role");
            if (p.IsDisabled) info.Lines.Add("disabled");
            if (!string.IsNullOrEmpty(p.Login)) info.Lines.Add("login: " + p.Login);
            if (!string.IsNullOrEmpty(p.Default))
                info.Lines.Add((p.Kind == DbPrincipalKind.Login ? "default database: " : "default schema: ") + p.Default);
            if (p.IsRole) info.Lines.Add(p.Members.Count == 0 ? "no members" : "members: " + Limited(p.Members));
            if (p.MemberOf.Count > 0) info.Lines.Add("member of: " + Limited(p.MemberOf));
            return info;
        }

        private static QuickInfo SchemaInfo(DatabaseSchema schema, string name, int start, int len)
        {
            if (schema == null || !schema.IsSchema(name)) return null;
            var info = new QuickInfo(start, len, "schema", schema.Schemas.First(s => Ci.Equals(s, name)));
            var counts = schema.Objects.Where(o => Ci.Equals(o.Schema, name) && o.Kind != DbObjectKind.Trigger)
                .GroupBy(o => o.Kind).OrderBy(g => g.Key)
                .Select(g => g.Count() + " " + KindText(g.Key) + (g.Count() == 1 ? "" : "s"));
            var text = string.Join(", ", counts);
            info.Lines.Add(text.Length == 0 ? "no objects" : text);
            return info;
        }

        private static QuickInfo DatabaseInfo(DatabaseSchema schema, string name, int start, int len)
        {
            if (schema == null || !schema.IsDatabase(name) && schema.FindDatabase(name) == null) return null;
            var info = new QuickInfo(start, len, "database", name);
            var current = Ci.Equals(name, schema.DatabaseName);
            if (current) info.Lines.Add("current database");
            var d = schema.FindDatabase(name);
            if (d != null)
            {
                if (!Ci.Equals(d.State, "ONLINE")) info.Lines.Add("state: " + d.State);
                info.Lines.Add($"recovery: {d.RecoveryModel}, compatibility level {d.CompatibilityLevel}");
            }
            if (current)
                foreach (var f in schema.Files.Take(MaxLines))
                    info.Lines.Add($"  {f.Name}: {f.Describe()}");
            foreach (var type in new[] { 'D', 'I', 'L' })
            {
                var last = schema.Backups.FirstOrDefault(b => b.Type == type && Ci.Equals(b.Database, name));
                if (last != null) info.Lines.Add($"last {last.TypeText} backup: {last.Finished:yyyy-MM-dd HH:mm} — {last.Device}");
            }
            return info;
        }

        private static QuickInfo IndexInfo(DatabaseSchema schema, List<SqlToken> sig, int k, string name, int start, int len)
        {
            if (schema == null) return null;
            // Prefer the table named after ON in this statement.
            var on = k + 1 < sig.Count && sig[k + 1].Is("ON") ? k + 2 : -1;
            DbObject table = null;
            if (on > 0 && on < sig.Count && sig[on].IsName)
                table = on + 2 < sig.Count && sig[on + 1].Text == "." && sig[on + 2].IsName
                    ? schema.Find(sig[on].Unquoted, sig[on + 2].Unquoted)
                    : schema.Find(null, sig[on].Unquoted);
            foreach (var o in table != null ? new[] { table } : schema.Objects.ToArray())
            {
                var ix = o.Indexes.FirstOrDefault(x => Ci.Equals(x.Name, name));
                if (ix == null) continue;
                var info = new QuickInfo(start, len, "index", ix.Name);
                info.Lines.Add(ix.Describe());
                info.Lines.Add("on " + o.QualifiedName);
                return info;
            }
            return null;
        }

        private static string KindText(DbObjectKind kind)
        {
            switch (kind)
            {
                case DbObjectKind.ScalarFunction: return "scalar function";
                case DbObjectKind.TableFunction: return "table-valued function";
                case DbObjectKind.TableType: return "table type";
                default: return kind.ToString().ToLowerInvariant();
            }
        }

        private static void AddLimited(List<string> lines, IEnumerable<string> items)
        {
            var list = items.ToList();
            lines.AddRange(list.Take(MaxLines));
            if (list.Count > MaxLines) lines.Add($"  … {list.Count - MaxLines} more");
        }

        private static string Limited(List<string> names) =>
            string.Join(", ", names.Take(MaxLines)) + (names.Count > MaxLines ? $", … {names.Count - MaxLines} more" : "");
    }
}
