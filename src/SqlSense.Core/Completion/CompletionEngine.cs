using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlSense.Core.Analysis;
using SqlSense.Core.Language;
using SqlSense.Core.Schema;

namespace SqlSense.Core.Completion
{
    public sealed class CompletionResult
    {
        public CompletionResult(CompletionContext context, List<CompletionItem> items)
        {
            Context = context;
            Items = items;
        }

        public CompletionContext Context { get; }
        public List<CompletionItem> Items { get; }
    }

    /// <summary>Builds the suggestion list for a caret position.</summary>
    public sealed class CompletionEngine
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        public CompletionEngine(CompletionOptions options = null, SnippetLibrary snippets = null)
        {
            Options = options ?? new CompletionOptions();
            Snippets = snippets ?? new SnippetLibrary();
        }

        public CompletionOptions Options { get; set; }
        public SnippetLibrary Snippets { get; set; }
        public string NewLine { get; set; } = "\r\n";

        /// <summary>
        /// When false, every candidate is returned in priority order and the caller filters as the
        /// user types (the editor's completion list does this).
        /// </summary>
        public bool FilterByPrefix { get; set; } = true;

        public CompletionResult GetCompletions(string text, int caret, DatabaseSchema schema)
        {
            var ctx = ContextAnalyzer.Analyze(text, caret, schema);
            var items = new List<CompletionItem>();
            if (ctx.Kind == ContextKind.None || ctx.Kind == ContextKind.AliasName)
                return new CompletionResult(ctx, items);

            if (ctx.HasQualifier) AddQualifiedMembers(ctx, schema, items);
            else
            {
                switch (ctx.Kind)
                {
                    case ContextKind.Keyword:
                        AddSnippets(items, ctx);
                        AddKeywords(items, 0);
                        break;
                    case ContextKind.TableSource:
                        AddTableSources(ctx, schema, items);
                        if (ctx.ClauseKeyword == "INSERT") items.Add(Keyword("INTO", -1));
                        break;
                    case ContextKind.Procedure:
                        AddObjects(ctx, schema, items, o => o.Kind == DbObjectKind.Procedure, null);
                        AddSchemas(schema, items);
                        break;
                    case ContextKind.ObjectName:
                        AddObjects(ctx, schema, items, ModuleFilter(ctx.ClauseKeyword), null);
                        AddSchemas(schema, items);
                        break;
                    case ContextKind.Expression:
                        AddExpression(ctx, schema, items);
                        break;
                    case ContextKind.InsertColumns:
                        AddInsertColumns(ctx, items);
                        break;
                    case ContextKind.DataType:
                        foreach (var t in SqlVocabulary.DataTypes.OrderBy(x => x))
                            items.Add(new CompletionItem(t, t, CompletionItemKind.DataType, "data type"));
                        break;
                    case ContextKind.Database:
                        if (schema != null)
                        {
                            foreach (var db in new[] { schema.DatabaseName }.Concat(schema.OtherDatabases).Distinct(Ci))
                                items.Add(new CompletionItem(db, Quote(db), CompletionItemKind.Database, "database"));
                        }
                        break;
                    case ContextKind.Variable:
                        AddVariables(ctx, items, 0);
                        foreach (var f in SqlVocabulary.Functions.Where(f => f.Name.StartsWith("@@", StringComparison.Ordinal)))
                            items.Add(new CompletionItem(f.Name, f.Name, CompletionItemKind.Function, f.Signature + " — " + f.Description) { Priority = 5 });
                        break;
                }
            }

            return new CompletionResult(ctx, Rank(items, FilterByPrefix ? ctx.Prefix : string.Empty));
        }

        /// <summary>Filters by the typed prefix and orders by match quality, then priority, then name.</summary>
        public static List<CompletionItem> Rank(IEnumerable<CompletionItem> items, string prefix)
        {
            return items
                .Select(i => new { Item = i, Score = CompletionMatcher.Score(prefix, i.DisplayText) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score >= 800 ? 800 : x.Score) // prefix matches compete on priority
                .ThenBy(x => x.Item.Priority)
                .ThenBy(x => x.Item.DisplayText, Ci)
                .Select(x => x.Item)
                .ToList();
        }

        // ---------------- contexts ----------------

        private void AddTableSources(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            foreach (var cte in ctx.Ctes)
                items.Add(TableItem(ctx, cte.Name, cte.Name, CompletionItemKind.LocalTable, "CTE (" + string.Join(", ", cte.Columns) + ")", 0, cte.Columns));
            foreach (var local in ctx.LocalTables.GroupBy(l => l.Name, Ci).Select(g => g.Last()))
                items.Add(TableItem(ctx, local.Name, local.Name, CompletionItemKind.LocalTable, "local table (" + string.Join(", ", local.Columns) + ")", 1, local.Columns));

            var kinds = ctx.IsInsertTarget || ctx.ClauseKeyword == "UPDATE" || ctx.ClauseKeyword == "DELETE"
                        || ctx.ClauseKeyword == "TABLE" || ctx.ClauseKeyword == "INTO"
                ? new[] { DbObjectKind.Table, DbObjectKind.View, DbObjectKind.Synonym }
                : new[] { DbObjectKind.Table, DbObjectKind.View, DbObjectKind.TableFunction, DbObjectKind.Synonym };
            AddObjects(ctx, schema, items, o => kinds.Contains(o.Kind), null);
            AddSchemas(schema, items);
        }

        private void AddObjects(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items,
            Func<DbObject, bool> filter, string onlySchema)
        {
            if (schema == null) return;
            foreach (var o in schema.Objects)
            {
                if (!filter(o)) continue;
                if (onlySchema != null && !Ci.Equals(o.Schema, onlySchema)) continue;
                var qualify = onlySchema == null && (Options.AlwaysQualifySchema || !Ci.Equals(o.Schema, schema.DefaultSchema));
                var name = qualify ? Quote(o.Schema) + "." + Quote(o.Name) : Quote(o.Name);
                var display = onlySchema == null ? o.Schema + "." + o.Name : o.Name;
                var priority = Ci.Equals(o.Schema, schema.DefaultSchema) ? 10 : 11;

                CompletionItem item;
                if (ctx.Kind == ContextKind.ObjectName)
                {
                    items.Add(ModuleItem(ctx, o, display, name, priority));
                    continue;
                }
                switch (o.Kind)
                {
                    case DbObjectKind.Procedure:
                        item = new CompletionItem(display, name, CompletionItemKind.Procedure, DescribeRoutine(o)) { Priority = priority };
                        if (Options.ExecGeneratesParameters && ctx.Kind == ContextKind.Procedure && o.Parameters.Count > 0)
                            item.InsertText = name + " " + ExecParameters(o, ctx.LineIndent);
                        break;
                    case DbObjectKind.ScalarFunction:
                        item = new CompletionItem(display, name + "()", CompletionItemKind.ScalarFunction, DescribeRoutine(o)) { Priority = priority + 20 };
                        item.CaretOffset = item.InsertText.Length - 1;
                        break;
                    case DbObjectKind.TableFunction:
                        item = TableItem(ctx, display, name + "()", CompletionItemKind.TableFunction, DescribeRoutine(o), priority, o.Columns.Select(c => c.Name).ToList());
                        if (item.CaretOffset < 0) item.CaretOffset = name.Length + 1;
                        break;
                    default:
                        var kind = o.Kind == DbObjectKind.View ? CompletionItemKind.View
                            : o.Kind == DbObjectKind.Synonym ? CompletionItemKind.Synonym : CompletionItemKind.Table;
                        item = TableItem(ctx, display, name, kind, o.Kind.ToString().ToLowerInvariant() + DescribeColumns(o), priority,
                            o.Columns.Select(c => c.Name).ToList(), o);
                        break;
                }
                items.Add(item);
            }
        }

        private static Func<DbObject, bool> ModuleFilter(string keyword)
        {
            switch (keyword)
            {
                case "PROCEDURE": return o => o.Kind == DbObjectKind.Procedure;
                case "FUNCTION": return o => o.Kind == DbObjectKind.ScalarFunction || o.Kind == DbObjectKind.TableFunction;
                case "VIEW": return o => o.Kind == DbObjectKind.View;
                case "TRIGGER": return o => o.Kind == DbObjectKind.Trigger;
                default: return o => false;
            }
        }

        /// <summary>An object after ALTER/DROP PROCEDURE …: just its name, plus a script request after ALTER.</summary>
        private CompletionItem ModuleItem(CompletionContext ctx, DbObject o, string display, string name, int priority)
        {
            CompletionItemKind kind;
            string description;
            switch (o.Kind)
            {
                case DbObjectKind.View:
                    kind = CompletionItemKind.View;
                    description = "view" + DescribeColumns(o);
                    break;
                case DbObjectKind.Trigger:
                    kind = CompletionItemKind.Trigger;
                    description = "trigger" + (o.Parent != null ? " on " + o.Parent : string.Empty);
                    break;
                case DbObjectKind.ScalarFunction:
                    kind = CompletionItemKind.ScalarFunction;
                    description = DescribeRoutine(o);
                    break;
                case DbObjectKind.TableFunction:
                    kind = CompletionItemKind.TableFunction;
                    description = DescribeRoutine(o);
                    break;
                default:
                    kind = CompletionItemKind.Procedure;
                    description = DescribeRoutine(o);
                    break;
            }
            var item = new CompletionItem(display, name, kind, description) { Priority = priority };
            if (ctx.ScriptVerb != null && Options.AlterScriptsDefinition)
            {
                item.ScriptRequest = new ObjectScriptRequest(o, ctx.ScriptVerb, ctx.StatementStart);
                item.Description += " — inserts its definition";
            }
            return item;
        }

        private CompletionItem TableItem(CompletionContext ctx, string display, string insert, CompletionItemKind kind,
            string description, int priority, List<string> columns, DbObject obj = null)
        {
            var item = new CompletionItem(display, insert, kind, description) { Priority = priority };
            if (ctx.Kind != ContextKind.TableSource || ctx.HasQualifier && ctx.Qualifier.Count > 1) return item;

            if (ctx.IsInsertTarget && Options.InsertGeneratesColumns && obj != null)
            {
                var cols = obj.Columns.Where(c => !c.IsIdentity && !c.IsComputed && !Ci.Equals(c.DataType, "timestamp") && !Ci.Equals(c.DataType, "rowversion")).ToList();
                if (cols.Count > 0)
                {
                    var sb = new StringBuilder(insert);
                    sb.Append(" (").Append(string.Join(", ", cols.Select(c => Quote(c.Name)))).Append(")");
                    sb.Append(NewLine).Append(ctx.LineIndent).Append(Kw("VALUES")).Append(" (");
                    var valuesStart = sb.Length;
                    sb.Append(string.Join(", ", cols.Select(c => DefaultValue(c.DataType))));
                    sb.Append(")");
                    item.InsertText = sb.ToString();
                    item.CaretOffset = valuesStart;
                }
                return item;
            }

            if (ctx.AllowsAlias && Options.AutoAlias)
            {
                var alias = AliasGenerator.Generate(LastPart(display), ctx.TablesInScope.Select(t => t.Alias ?? t.Name));
                var insertText = item.CaretOffset >= 0 ? item.InsertText : insert;
                item.InsertText = insertText + (Options.AliasWithAs ? " " + Kw("AS") + " " : " ") + alias;
            }
            return item;
        }

        private void AddExpression(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            if (ctx.IsJoinCondition)
            {
                // FK conditions first, then same-named columns in generator order (priority keeps that order).
                var conditions = new HashSet<string>(Ci);
                if (Options.SuggestJoinConditions)
                    foreach (var cond in JoinConditions.Suggest(ctx.TablesInScope, schema))
                        if (conditions.Add(cond))
                            items.Add(new CompletionItem(cond, cond, CompletionItemKind.JoinCondition, "join on foreign key") { Priority = -1000 });
                if (Options.SuggestSameNameJoins)
                {
                    var priority = -999;
                    foreach (var cond in JoinConditions.SameName(ctx.TablesInScope))
                        if (conditions.Add(cond))
                            items.Add(new CompletionItem(cond, cond, CompletionItemKind.JoinCondition, "join on same column name") { Priority = priority++ });
                }
            }

            var refs = ctx.TablesInScope;
            var multi = refs.Count(r => r.ColumnNames.Any()) > 1;
            var seen = new HashSet<string>(Ci);
            foreach (var r in refs)
            {
                foreach (var col in r.ColumnNames)
                {
                    var display = multi ? r.Qualifier + "." + col : col;
                    if (!seen.Add(display)) continue;
                    var dbCol = r.Object?.Columns.FirstOrDefault(c => Ci.Equals(c.Name, col));
                    var desc = (dbCol != null ? dbCol.Describe() : col) + " — " + (r.Object?.QualifiedName ?? r.Name ?? r.Alias);
                    items.Add(new CompletionItem(display, multi ? Quote(r.Qualifier) + "." + Quote(col) : Quote(col), CompletionItemKind.Column, desc) { Priority = 0 });
                }
            }
            foreach (var r in refs.Where(r => !string.IsNullOrEmpty(r.Qualifier)))
            {
                if (seen.Add(r.Qualifier))
                    items.Add(new CompletionItem(r.Qualifier, Quote(r.Qualifier), CompletionItemKind.Alias,
                        "alias of " + (r.Object?.QualifiedName ?? r.Name ?? "derived table")) { Priority = 1 });
            }
            AddVariables(ctx, items, 2);
            foreach (var f in SqlVocabulary.Functions)
            {
                var insert = f.NeedsParens ? f.Name + "()" : f.Name;
                items.Add(new CompletionItem(f.Name, insert, CompletionItemKind.Function, f.Signature + " — " + f.Description)
                {
                    Priority = 6,
                    CaretOffset = f.NeedsParens ? insert.Length - 1 : -1,
                });
            }
            if (schema != null)
                AddObjects(ctx, schema, items, o => o.Kind == DbObjectKind.ScalarFunction, null);
            AddKeywords(items, 8);
            if (ctx.ClauseKeyword == "(" || ctx.ClauseKeyword == "EXISTS" || ctx.ClauseKeyword == "IN")
                AddSnippets(items, ctx);
        }

        private void AddInsertColumns(CompletionContext ctx, List<CompletionItem> items)
        {
            var target = ctx.InsertTarget;
            if (target == null) return;
            var cols = target.Object != null
                ? target.Object.Columns.Where(c => !c.IsIdentity && !c.IsComputed).Select(c => c.Name).ToList()
                : target.ColumnNames.ToList();
            if (cols.Count > 1)
                items.Add(new CompletionItem("(all columns)", string.Join(", ", cols.Select(Quote)), CompletionItemKind.Snippet,
                    "insert every writable column") { Priority = -1 });
            foreach (var c in cols)
                items.Add(new CompletionItem(c, Quote(c), CompletionItemKind.Column, target.Name) { Priority = 0 });
        }

        private void AddQualifiedMembers(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            var q = ctx.Qualifier;
            var baseKind = ctx.Kind;

            if (q.Count == 1)
            {
                var r = ctx.TablesInScope.FirstOrDefault(t => t.Matches(q[0]))
                        ?? ctx.TablesInScope.FirstOrDefault(t => Ci.Equals(t.Name, q[0]));
                if (r != null && baseKind != ContextKind.TableSource && baseKind != ContextKind.Procedure && baseKind != ContextKind.ObjectName)
                {
                    foreach (var col in r.ColumnNames)
                    {
                        var dbCol = r.Object?.Columns.FirstOrDefault(c => Ci.Equals(c.Name, col));
                        items.Add(new CompletionItem(col, Quote(col), CompletionItemKind.Column, dbCol?.Describe() ?? col) { Priority = dbCol?.Ordinal ?? 0 });
                    }
                    return;
                }
                if (schema != null && schema.IsSchema(q[0]))
                {
                    Func<DbObject, bool> filter;
                    switch (baseKind)
                    {
                        case ContextKind.TableSource:
                            filter = o => o.Kind != DbObjectKind.Procedure && o.Kind != DbObjectKind.ScalarFunction
                                          && o.Kind != DbObjectKind.TableType && o.Kind != DbObjectKind.Trigger;
                            break;
                        case ContextKind.Procedure:
                            filter = o => o.Kind == DbObjectKind.Procedure;
                            break;
                        case ContextKind.ObjectName:
                            filter = ModuleFilter(ctx.ClauseKeyword);
                            break;
                        default:
                            filter = o => o.Kind == DbObjectKind.ScalarFunction || o.Kind == DbObjectKind.TableFunction;
                            break;
                    }
                    AddObjects(ctx, schema, items, filter, q[0]);
                    return;
                }
                if (schema != null && Ci.Equals(q[0], schema.DatabaseName))
                {
                    AddSchemas(schema, items, withDot: true);
                    return;
                }
                // Unknown alias of a local table name that wasn't resolved: fall back to temp tables.
                var local = ctx.LocalTables.LastOrDefault(l => Ci.Equals(l.Name, q[0]));
                if (local != null)
                    foreach (var c in local.Columns) items.Add(new CompletionItem(c, Quote(c), CompletionItemKind.Column, local.Name));
                return;
            }

            if (schema == null) return;
            if (q.Count == 2)
            {
                // schema.object.<column>
                var obj = schema.Find(q[0], q[1]);
                if (obj != null)
                {
                    foreach (var c in obj.Columns) items.Add(new CompletionItem(c.Name, Quote(c.Name), CompletionItemKind.Column, c.Describe()) { Priority = c.Ordinal });
                    return;
                }
                // db.schema.<object>
                if (Ci.Equals(q[0], schema.DatabaseName) && schema.IsSchema(q[1]))
                    AddObjects(ctx, schema, items,
                        baseKind == ContextKind.Procedure ? o => o.Kind == DbObjectKind.Procedure
                        : baseKind == ContextKind.ObjectName ? ModuleFilter(ctx.ClauseKeyword)
                        : (Func<DbObject, bool>)(o => o.Kind != DbObjectKind.Procedure && o.Kind != DbObjectKind.Trigger), q[1]);
                return;
            }
            if (q.Count == 3 && Ci.Equals(q[0], schema.DatabaseName))
            {
                var obj = schema.Find(q[1], q[2]);
                if (obj != null)
                    foreach (var c in obj.Columns) items.Add(new CompletionItem(c.Name, Quote(c.Name), CompletionItemKind.Column, c.Describe()));
            }
        }

        // ---------------- building blocks ----------------

        private void AddKeywords(List<CompletionItem> items, int priority)
        {
            foreach (var k in SqlVocabulary.Keywords) items.Add(Keyword(k, priority));
        }

        private CompletionItem Keyword(string k, int priority) =>
            new CompletionItem(Kw(k), Kw(k), CompletionItemKind.Keyword, "keyword") { Priority = priority };

        private void AddSnippets(List<CompletionItem> items, CompletionContext ctx)
        {
            foreach (var s in Snippets.All)
            {
                var exp = SnippetLibrary.Expand(s, ctx.LineIndent, NewLine);
                items.Add(new CompletionItem(s.Shortcut, exp.Text, CompletionItemKind.Snippet, s.Description)
                {
                    Priority = 3,
                    CaretOffset = exp.CaretOffset,
                });
            }
        }

        private static void AddVariables(CompletionContext ctx, List<CompletionItem> items, int priority)
        {
            foreach (var v in ctx.Variables)
                items.Add(new CompletionItem(v.Name, v.Name, CompletionItemKind.Variable, v.DataType ?? "variable") { Priority = priority });
        }

        private static void AddSchemas(DatabaseSchema schema, List<CompletionItem> items, bool withDot = false)
        {
            if (schema == null) return;
            foreach (var s in schema.Schemas)
                items.Add(new CompletionItem(s, Quote(s), CompletionItemKind.Schema, "schema") { Priority = 20 });
        }

        private string ExecParameters(DbObject proc, string indent)
        {
            var parts = proc.Parameters.OrderBy(p => p.Ordinal)
                .Select(p => p.IsOutput
                    ? $"{p.Name} = {p.Name} {Kw("OUTPUT")}"
                    : $"{p.Name} = {DefaultValue(p.DataType)}")
                .ToList();
            if (parts.Count <= 2) return string.Join(", ", parts);
            var sep = "," + NewLine + indent + new string(' ', 4);
            return parts[0] + sep + string.Join(sep, parts.Skip(1));
        }

        private string Kw(string keyword) => Options.UppercaseKeywords ? keyword.ToUpperInvariant() : keyword.ToLowerInvariant();

        private static string DescribeRoutine(DbObject o)
        {
            var ps = string.Join(", ", o.Parameters.OrderBy(p => p.Ordinal).Select(p => $"{p.Name} {p.DataType}{(p.IsOutput ? " OUTPUT" : "")}"));
            return $"{o.Kind.ToString().ToLowerInvariant()} {o.QualifiedName}({ps})";
        }

        private static string DescribeColumns(DbObject o)
        {
            if (o.Columns.Count == 0) return string.Empty;
            var cols = o.Columns.Take(12).Select(c => c.Name + " " + c.DataType);
            return " (" + string.Join(", ", cols) + (o.Columns.Count > 12 ? ", …" : "") + ")";
        }

        public static string DefaultValue(string dataType)
        {
            var t = (dataType ?? string.Empty).ToLowerInvariant();
            var paren = t.IndexOf('(');
            if (paren >= 0) t = t.Substring(0, paren);
            switch (t)
            {
                case "bit":
                case "tinyint":
                case "smallint":
                case "int":
                case "bigint":
                case "decimal":
                case "numeric":
                case "float":
                case "real":
                case "money":
                case "smallmoney":
                    return "0";
                case "nchar":
                case "nvarchar":
                case "ntext":
                case "sysname":
                    return "N''";
                case "char":
                case "varchar":
                case "text":
                    return "''";
                case "date":
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "datetimeoffset":
                case "time":
                    return "SYSDATETIME()";
                case "uniqueidentifier":
                    return "NEWID()";
                default:
                    return "NULL";
            }
        }

        private static string LastPart(string display)
        {
            var dot = display.LastIndexOf('.');
            return dot >= 0 ? display.Substring(dot + 1) : display;
        }

        /// <summary>Brackets a name when it is not a plain identifier.</summary>
        public static string Quote(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.StartsWith("#", StringComparison.Ordinal) || name.StartsWith("@", StringComparison.Ordinal)) return name;
            var plain = (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$')
                        && !IsReserved(name);
            return plain ? name : "[" + name.Replace("]", "]]") + "]";
        }

        private static readonly HashSet<string> Reserved = new HashSet<string>(SqlVocabulary.Keywords.Where(k =>
            k != "NOCOUNT" && k != "XACT_ABORT" && k != "APPLY" && k != "ROWS" && k != "TIES" && k != "OFFSET" && k != "NOLOCK"
            && k != "READONLY" && k != "OUTPUT" && k != "PARTITION" && k != "CATCH" && k != "TRY" && k != "CONTINUE" && k != "THROW"
            && k != "USING" && k != "TRAN" && k != "GO"), Ci);

        private static bool IsReserved(string name) => Reserved.Contains(name);
    }
}
