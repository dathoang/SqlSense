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

        /// <summary>The name refers to another database or linked server whose metadata is still loading.</summary>
        public bool SchemaPending { get; set; }
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

        /// <summary>Clock for generated backup file names.</summary>
        public Func<DateTime> Now { get; set; } = () => DateTime.Now;

        /// <summary>
        /// When false, every candidate is returned in priority order and the caller filters as the
        /// user types (the editor's completion list does this).
        /// </summary>
        public bool FilterByPrefix { get; set; } = true;

        /// <summary>Recently picked items; with <see cref="CompletionOptions.RankRecentlyUsed"/> they rank higher.</summary>
        public CompletionHistory History { get; set; }

        private bool _schemaPending;

        public CompletionResult GetCompletions(string text, int caret, DatabaseSchema schema)
        {
            _schemaPending = false;
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
                    case ContextKind.TableSource when ctx.NameAsString:
                        AddTableNamesAsStrings(schema, items);
                        break;
                    case ContextKind.TableSource:
                        AddTableSources(ctx, schema, items);
                        if (ctx.ClauseKeyword == "INSERT") items.Add(Keyword("INTO", -1));
                        break;
                    case ContextKind.Procedure:
                        AddObjects(ctx, schema, items, o => o.Kind == DbObjectKind.Procedure, null);
                        AddSchemas(schema, items);
                        AddDatabasesAndServers(schema, items);
                        break;
                    case ContextKind.ObjectName:
                        AddObjects(ctx, schema, items, ModuleFilter(ctx.ClauseKeyword), null);
                        AddSchemas(schema, items);
                        break;
                    case ContextKind.Principal:
                        AddPrincipals(ctx, schema, items);
                        break;
                    case ContextKind.Schema:
                        AddSchemas(schema, items);
                        break;
                    case ContextKind.Options:
                        AddOptions(ctx, schema, items);
                        break;
                    case ContextKind.DatabaseFile:
                        if (schema != null)
                            foreach (var f in schema.Files)
                                items.Add(new CompletionItem(f.Name, "N'" + f.Name.Replace("'", "''") + "'", CompletionItemKind.File, f.Describe()));
                        break;
                    case ContextKind.TableMember:
                        AddTableMembers(ctx, schema, items);
                        break;
                    case ContextKind.Index:
                        AddIndexes(ctx, schema, items);
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
                            // RESTORE also targets databases that are not online (RESTORING after NORECOVERY).
                            var names = new[] { schema.DatabaseName }.Concat(schema.OtherDatabases);
                            if (ctx.ClauseKeyword == "RESTORE") names = names.Concat(schema.Databases.Select(d => d.Name));
                            foreach (var db in names.Distinct(Ci))
                                items.Add(new CompletionItem(db, Quote(db), CompletionItemKind.Database, DescribeDatabase(schema, db)));
                        }
                        break;
                    case ContextKind.Variable:
                        AddVariables(ctx, items, 0);
                        foreach (var f in SqlVocabulary.Functions.Where(f => f.Name.StartsWith("@@", StringComparison.Ordinal)))
                            items.Add(new CompletionItem(f.Name, f.Name, CompletionItemKind.Function, f.Signature + " — " + f.Description) { Priority = 5 });
                        break;
                }
            }

            if (History != null && Options.RankRecentlyUsed)
                foreach (var item in items)
                    item.Priority -= History.Boost(item);

            return new CompletionResult(ctx, Rank(items, FilterByPrefix ? ctx.Prefix : string.Empty)) { SchemaPending = _schemaPending };
        }

        /// <summary>Filters by the typed prefix and orders by match quality, then priority, then name.</summary>
        public static List<CompletionItem> Rank(IEnumerable<CompletionItem> items, string prefix)
        {
            return items
                .Select(i => new { Item = i, Score = CompletionMatcher.Score(prefix, i.DisplayText) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => CompletionMatcher.Bucket(x.Score)) // exact first; prefix matches compete on priority
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
            // After JOIN, tables with a foreign key to one already in the query come first.
            var related = ctx.ClauseKeyword == "JOIN" ? JoinConditions.RelatedObjects(ctx.TablesInScope, schema) : null;
            AddObjects(ctx, schema, items, o => kinds.Contains(o.Kind), null, related);
            AddSchemas(schema, items);
            AddDatabasesAndServers(schema, items);
        }

        private void AddObjects(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items,
            Func<DbObject, bool> filter, string onlySchema, Dictionary<string, string> related = null)
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
                if (related != null && related.TryGetValue(o.Schema + "." + o.Name, out var note))
                {
                    item.Priority -= 6;
                    item.Description = note + " — " + item.Description;
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
                case "OBJECT": return o => o.Kind != DbObjectKind.Trigger;
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
                case DbObjectKind.Table:
                case DbObjectKind.TableType:
                    kind = CompletionItemKind.Table;
                    description = (o.Kind == DbObjectKind.Table ? "table" : "table type") + DescribeColumns(o);
                    break;
                case DbObjectKind.Synonym:
                    kind = CompletionItemKind.Synonym;
                    description = "synonym";
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
            if (ctx.IsJoinCondition) AddJoinConditions(ctx, schema, items, null);

            var seen = new HashSet<string>(Ci);
            AddSelectItems(ctx, items, seen);
            var refs = ctx.TablesInScope;
            var multi = refs.Count(r => r.ColumnNames.Any()) > 1;
            foreach (var r in refs)
            {
                foreach (var col in r.ColumnNames)
                {
                    var display = multi ? r.Qualifier + "." + col : col;
                    if (!seen.Add(display)) continue;
                    var dbCol = r.Object?.Columns.FirstOrDefault(c => Ci.Equals(c.Name, col));
                    var desc = (dbCol != null ? dbCol.Describe() : col) + " — " + (r.Object?.QualifiedName ?? r.Name ?? r.Alias);
                    var item = new CompletionItem(display, multi ? Quote(r.Qualifier) + "." + Quote(col) : Quote(col), CompletionItemKind.Column, desc);
                    item.Priority = ColumnPriority(ctx, schema, r, col, 0, item);
                    items.Add(item);
                }
            }
            foreach (var r in refs.Where(r => !string.IsNullOrEmpty(r.Qualifier)))
            {
                if (seen.Add(r.Qualifier))
                    items.Add(new CompletionItem(r.Qualifier, Quote(r.Qualifier), CompletionItemKind.Alias,
                        "alias of " + (r.Object?.QualifiedName ?? r.Name ?? "derived table")) { Priority = 1 });
            }
            AddVariables(ctx, items, 2);
            RankComparedVariables(ctx, schema, items);
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

        /// <summary>
        /// ON |: foreign-key conditions first, then same-named columns in generator order (priority keeps that order).
        /// ON c.|: the conditions involving c, written from c's side and inserted after the "c." already typed.
        /// </summary>
        private void AddJoinConditions(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items, TableRef from)
        {
            var conditions = new List<(JoinCondition Condition, string Description)>();
            if (Options.SuggestJoinConditions)
                conditions.AddRange(JoinConditions.ForeignKeys(ctx.TablesInScope, schema).Select(c => (c, "join on foreign key")));
            if (Options.SuggestSameNameJoins)
                conditions.AddRange(JoinConditions.SameNameColumns(ctx.TablesInScope).Select(c => (c, "join on same column name")));

            var seen = new HashSet<string>(Ci);
            var priority = -1000;
            foreach (var (condition, description) in conditions)
            {
                string display, insert;
                if (from == null) display = insert = condition.Text;
                else
                {
                    var otherFirst = ReferenceEquals(condition.Other, from);
                    if (!otherFirst && !ReferenceEquals(condition.Joined, from)) continue;
                    display = condition.Render(otherFirst);
                    var lead = Quote(from.Qualifier) + ".";
                    if (!display.StartsWith(lead, StringComparison.OrdinalIgnoreCase)) continue;
                    insert = display.Substring(lead.Length);
                }
                if (seen.Add(display))
                    items.Add(new CompletionItem(display, insert, CompletionItemKind.JoinCondition, description) { Priority = priority++ });
            }
        }

        /// <summary>
        /// Context ranking for a column of <paramref name="table"/>: after "a.col =" its partners (same name, or the
        /// other end of a foreign key) come first; right after ON, key columns (…ID, primary and foreign key columns)
        /// of the newly joined table, then of the other tables, come before the rest.
        /// </summary>
        private static int ColumnPriority(CompletionContext ctx, DatabaseSchema schema, TableRef table, string column, int basePriority, CompletionItem item)
        {
            if (ctx.ComparedColumn.HasValue)
            {
                var (q, compared) = ctx.ComparedColumn.Value;
                var left = ComparedTable(ctx);
                if (left == null) return basePriority;
                if (!ReferenceEquals(left, table) && JoinConditions.ArePartners(table, column, left, compared, schema))
                {
                    item.Description = "matches " + (q != null ? q + "." : "") + compared + " — " + item.Description;
                    return basePriority - 500;
                }
                // Then columns of a compatible type (numbers with numbers, text with text, dates with dates).
                var leftType = left.Object?.Columns.FirstOrDefault(c => Ci.Equals(c.Name, compared))?.DataType;
                var type = table.Object?.Columns.FirstOrDefault(c => Ci.Equals(c.Name, column))?.DataType;
                if (!(ReferenceEquals(left, table) && Ci.Equals(column, compared)) && SqlTypes.Compatible(leftType, type))
                    return basePriority - 100;
                return basePriority;
            }
            if (ctx.IsJoinCondition && IsKeyColumn(table, column, schema))
                return basePriority - (ReferenceEquals(table, ctx.TablesInScope.LastOrDefault()) ? 200 : 100);
            return basePriority;
        }

        /// <summary>The table of the column on the other side of the comparison ("c.CustomerID = |"), or null.</summary>
        private static TableRef ComparedTable(CompletionContext ctx)
        {
            if (!ctx.ComparedColumn.HasValue) return null;
            var (q, compared) = ctx.ComparedColumn.Value;
            return q != null
                ? ctx.TablesInScope.FirstOrDefault(t => t.Matches(q)) ?? ctx.TablesInScope.FirstOrDefault(t => Ci.Equals(t.Name, q))
                : ctx.TablesInScope.FirstOrDefault(t => t.ColumnNames.Contains(compared, Ci));
        }

        /// <summary>
        /// "c.CustomerID = |": a variable named like the column (@CustomerID) first of all, then variables of a
        /// compatible type ahead of the other columns.
        /// </summary>
        private static void RankComparedVariables(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            if (!ctx.ComparedColumn.HasValue) return;
            var compared = ctx.ComparedColumn.Value.Column;
            var leftType = ComparedTable(ctx)?.Object?.Columns.FirstOrDefault(c => Ci.Equals(c.Name, compared))?.DataType;
            foreach (var v in ctx.Variables)
            {
                var item = items.FirstOrDefault(i => i.Kind == CompletionItemKind.Variable && i.DisplayText == v.Name);
                if (item == null) continue;
                var bare = v.Name.TrimStart('@');
                if (Ci.Equals(bare, compared) || bare.EndsWith(compared, StringComparison.OrdinalIgnoreCase) && bare.Length - compared.Length <= 4)
                {
                    item.Priority = -600;
                    item.Description = "matches " + compared + " — " + item.Description;
                }
                else if (SqlTypes.Compatible(leftType, v.DataType)) item.Priority = -50;
            }
        }

        /// <summary>
        /// The SELECT list reused in the clause being typed: WHERE / GROUP BY / HAVING get an alias as its expression
        /// ("amount" inserts "price * quantity", which is what those clauses need), ORDER BY gets the alias itself;
        /// GROUP BY also offers every non-aggregate item at once and skips the ones already listed.
        /// </summary>
        private void AddSelectItems(CompletionContext ctx, List<CompletionItem> items, HashSet<string> seen)
        {
            if (ctx.SelectClause == null || ctx.SelectItems.Count == 0) return;
            string Wrap(SelectItem s) => s.HasOperator && ctx.AfterArithmetic ? "(" + s.Expression + ")" : s.Expression;
            var multi = ctx.TablesInScope.Count(r => r.ColumnNames.Any()) > 1;

            switch (ctx.SelectClause)
            {
                case "ORDER BY":
                    foreach (var s in ctx.SelectItems.Where(s => s.HasAlias))
                        if (seen.Add(s.Name))
                            items.Add(new CompletionItem(s.Name, Quote(s.Name), CompletionItemKind.Alias, "select-list alias of " + s.Expression) { Priority = -1 });
                    break;

                case "GROUP BY":
                    var groupable = ctx.SelectItems.Where(s => !s.IsAggregate && !ctx.ListedInClause.Contains(s.Expression)).ToList();
                    if (groupable.Count > 1)
                    {
                        var all = string.Join(", ", groupable.Select(s => s.Expression));
                        items.Add(new CompletionItem("(select columns)", all, CompletionItemKind.Snippet,
                            "every non-aggregate item of the select list: " + all) { Priority = -3 });
                    }
                    foreach (var s in groupable)
                    {
                        var display = s.HasAlias ? s.Name : s.Expression;
                        if (!seen.Add(display)) continue;
                        items.Add(new CompletionItem(display, s.Expression, s.HasAlias ? CompletionItemKind.Alias : CompletionItemKind.Column,
                            s.HasAlias ? "select-list expression: " + s.Expression : "select-list column") { Priority = -2 });
                    }
                    break;

                default: // WHERE, HAVING
                    foreach (var s in ctx.SelectItems)
                    {
                        if (!s.HasAlias || s.IsAggregate && ctx.SelectClause != "HAVING") continue;
                        // "c.Name AS Name": the column itself is already in the list.
                        if (s.IsSimpleColumn && (Ci.Equals(s.Expression, s.Name) || !multi && s.Expression.EndsWith("." + s.Name, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        if (!seen.Add(s.Name)) continue;
                        items.Add(new CompletionItem(s.Name, Wrap(s), CompletionItemKind.Alias, "select-list expression: " + s.Expression) { Priority = -1 });
                    }
                    break;
            }
        }

        private static bool IsKeyColumn(TableRef table, string column, DatabaseSchema schema)
        {
            if (JoinConditions.IsKeyLike(column)) return true;
            var o = table.Object;
            if (o == null) return false;
            if (o.Indexes.Any(ix => (ix.IsPrimaryKey || ix.IsUnique) && ix.KeyColumns.Contains(column, Ci))) return true;
            return schema != null && schema.ForeignKeys.Any(fk =>
                Ci.Equals(fk.FromSchema, o.Schema) && Ci.Equals(fk.FromTable, o.Name) && fk.FromColumns.Contains(column, Ci)
                || Ci.Equals(fk.ToSchema, o.Schema) && Ci.Equals(fk.ToTable, o.Name) && fk.ToColumns.Contains(column, Ci));
        }

        private void AddInsertColumns(CompletionContext ctx, List<CompletionItem> items)
        {
            var target = ctx.InsertTarget;
            if (target == null) return;
            if (ctx.ClauseKeyword == "INDEX")
            {
                // CREATE INDEX … ON t (…) / INCLUDE (…): every column, in table order.
                if (target.Object != null)
                    foreach (var c in target.Object.Columns)
                        items.Add(new CompletionItem(c.Name, Quote(c.Name), CompletionItemKind.Column, c.Describe()) { Priority = c.Ordinal });
                else
                {
                    var ordinal = 0;
                    foreach (var c in target.ColumnNames)
                        items.Add(new CompletionItem(c, Quote(c), CompletionItemKind.Column, target.Name) { Priority = ordinal++ });
                }
                return;
            }
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
                    if (ctx.IsJoinCondition) AddJoinConditions(ctx, schema, items, r);
                    foreach (var col in r.ColumnNames)
                    {
                        var dbCol = r.Object?.Columns.FirstOrDefault(c => Ci.Equals(c.Name, col));
                        var item = new CompletionItem(col, Quote(col), CompletionItemKind.Column, dbCol?.Describe() ?? col);
                        item.Priority = ColumnPriority(ctx, schema, r, col, dbCol?.Ordinal ?? 0, item);
                        items.Add(item);
                    }
                    return;
                }
                if (schema != null && schema.IsSchema(q[0]))
                {
                    AddObjects(ctx, schema, items, MemberFilter(ctx), q[0]);
                    return;
                }
                // database.<schema>, linked server.<database>
                if (AddSchemasOf(schema, null, q[0], items) || AddDatabasesOf(schema, q[0], items)) return;
                // Unknown alias of a local table name that wasn't resolved: fall back to temp tables.
                var local = ctx.LocalTables.LastOrDefault(l => Ci.Equals(l.Name, q[0]));
                if (local != null)
                    foreach (var c in local.Columns) items.Add(new CompletionItem(c, Quote(c), CompletionItemKind.Column, local.Name));
                return;
            }

            if (schema == null) return;
            if (q.Count == 2)
            {
                // schema.object.<column>, database.schema.<object> (database..<object> = its default schema), server.database.<schema>
                if (!string.IsNullOrEmpty(q[0]) && !string.IsNullOrEmpty(q[1]) && AddColumnsOf(schema, null, null, q[0], q[1], items)) return;
                if (AddObjectsOf(ctx, schema, null, q[0], q[1], items)) return;
                AddSchemasOf(schema, q[0], q[1], items);
                return;
            }
            if (q.Count == 3)
            {
                // database.schema.object.<column>, server.database.schema.<object>
                if (AddColumnsOf(schema, null, q[0], q[1], q[2], items)) return;
                AddObjectsOf(ctx, schema, q[0], q[1], q[2], items);
                return;
            }
            if (q.Count == 4) AddColumnsOf(schema, q[0], q[1], q[2], q[3], items);
        }

        /// <summary>Objects that fit after "schema." in this context.</summary>
        private static Func<DbObject, bool> MemberFilter(CompletionContext ctx)
        {
            switch (ctx.Kind)
            {
                case ContextKind.TableSource:
                    return o => o.Kind != DbObjectKind.Procedure && o.Kind != DbObjectKind.ScalarFunction
                                && o.Kind != DbObjectKind.TableType && o.Kind != DbObjectKind.Trigger;
                case ContextKind.Procedure:
                    return o => o.Kind == DbObjectKind.Procedure;
                case ContextKind.ObjectName:
                    return ModuleFilter(ctx.ClauseKeyword);
                default:
                    return o => o.Kind == DbObjectKind.ScalarFunction || o.Kind == DbObjectKind.TableFunction;
            }
        }

        /// <summary>
        /// Snapshot of [server.]database, or null. A known database that is still loading sets
        /// <see cref="CompletionResult.SchemaPending"/> so the host can reopen the list.
        /// </summary>
        private DatabaseSchema Lookup(DatabaseSchema schema, string server, string database, out bool known)
        {
            known = false;
            if (schema == null) return null;
            var target = schema.For(server, database, out var pending);
            if (pending) _schemaPending = true;
            known = target != null || pending;
            return target;
        }

        private bool AddSchemasOf(DatabaseSchema schema, string server, string database, List<CompletionItem> items)
        {
            if (string.IsNullOrEmpty(database)) return false;
            var target = Lookup(schema, server, database, out var known);
            if (target != null) AddSchemas(target, items);
            return known;
        }

        private bool AddDatabasesOf(DatabaseSchema schema, string server, List<CompletionItem> items)
        {
            if (schema == null || !schema.IsLinkedServer(server)) return false;
            var catalog = Lookup(schema, server, null, out _);
            if (catalog != null)
                foreach (var db in catalog.OtherDatabases)
                    items.Add(new CompletionItem(db, Quote(db), CompletionItemKind.Database, "database on " + server));
            return true;
        }

        private bool AddObjectsOf(CompletionContext ctx, DatabaseSchema schema, string server, string database, string schemaName, List<CompletionItem> items)
        {
            if (string.IsNullOrEmpty(database) || server == null && schema.IsSchema(database) && !schema.IsDatabase(database)) return false;
            var target = Lookup(schema, server, database, out var known);
            if (target == null) return known;
            if (string.IsNullOrEmpty(schemaName)) schemaName = target.DefaultSchema;
            if (!target.IsSchema(schemaName)) return false;
            AddObjects(ctx, target, items, MemberFilter(ctx), schemaName);
            return true;
        }

        private bool AddColumnsOf(DatabaseSchema schema, string server, string database, string schemaName, string name, List<CompletionItem> items)
        {
            var target = database == null ? schema : Lookup(schema, server, database, out _);
            var obj = target?.Find(string.IsNullOrEmpty(schemaName) ? null : schemaName, name);
            if (obj == null) return false;
            foreach (var c in obj.Columns) items.Add(new CompletionItem(c.Name, Quote(c.Name), CompletionItemKind.Column, c.Describe()) { Priority = c.Ordinal });
            return true;
        }

        /// <summary>FROM | / EXEC |: databases and linked servers, for three- and four-part names.</summary>
        private static void AddDatabasesAndServers(DatabaseSchema schema, List<CompletionItem> items)
        {
            if (schema == null) return;
            foreach (var db in new[] { schema.DatabaseName }.Concat(schema.OtherDatabases).Distinct(Ci))
                items.Add(new CompletionItem(db, Quote(db), CompletionItemKind.Database, DescribeDatabase(schema, db)) { Priority = 25 });
            foreach (var server in schema.LinkedServers)
                items.Add(new CompletionItem(server, Quote(server), CompletionItemKind.Server, "linked server") { Priority = 26 });
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

        // ---------------- maintenance statements ----------------

        private void AddOptions(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            if (ctx.OptionList == "BACKUP TO") AddBackupFile(ctx, schema, items);
            if (ctx.OptionList == "RESTORE FROM") AddRecentBackups(ctx, schema, items);
            if (!StatementOptions.Lists.TryGetValue(ctx.OptionList ?? "", out var list)) return;
            var priority = 0;
            foreach (var o in list)
            {
                var text = KwOutsideQuotes(o.Text);
                var caret = text.IndexOf('|');
                text = text.Replace("|", string.Empty);
                items.Add(new CompletionItem(text, text, CompletionItemKind.Keyword, o.Description) { Priority = priority++, CaretOffset = caret });
            }
        }

        /// <summary>BACKUP … TO: a dated file name for the database, in the server's default backup folder.</summary>
        private void AddBackupFile(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            var db = ctx.TargetDatabase ?? schema?.DatabaseName;
            if (string.IsNullOrEmpty(db)) return;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var safe = new string(db.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            var file = $"{safe}_{(ctx.TargetIsLog ? "log_" : "")}{Now():yyyyMMdd_HHmm}.{(ctx.TargetIsLog ? "trn" : "bak")}";
            var text = Kw("DISK") + " = N'" + file.Replace("'", "''") + "'";
            items.Add(new CompletionItem(text, text, CompletionItemKind.Snippet,
                (ctx.TargetIsLog ? "log" : "full") + " backup file in the server's default backup folder") { Priority = -1 });
        }

        /// <summary>RESTORE … FROM: the newest backup files of the database from msdb's history.</summary>
        private void AddRecentBackups(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            if (schema == null) return;
            var types = ctx.TargetDatabase == null ? "DIL" : ctx.TargetIsLog ? "L" : "DI";
            var priority = -100;
            var seen = new HashSet<string>(Ci);
            foreach (var b in schema.Backups)
            {
                if (ctx.TargetDatabase != null && !Ci.Equals(b.Database, ctx.TargetDatabase)) continue;
                if (types.IndexOf(b.Type) < 0 || string.IsNullOrEmpty(b.Device) || b.Device.StartsWith("{", StringComparison.Ordinal)) continue;
                if (!seen.Add(b.Device)) continue;
                var url = b.Device.StartsWith("http", StringComparison.OrdinalIgnoreCase) || b.Device.StartsWith("s3:", StringComparison.OrdinalIgnoreCase);
                var text = Kw(url ? "URL" : "DISK") + " = N'" + b.Device.Replace("'", "''") + "'";
                items.Add(new CompletionItem(text, text, CompletionItemKind.Snippet,
                    $"{b.TypeText} backup of {b.Database}, {b.Finished:yyyy-MM-dd HH:mm}") { Priority = priority++ });
                if (seen.Count >= 10) break;
            }
        }

        private static void AddTableNamesAsStrings(DatabaseSchema schema, List<CompletionItem> items)
        {
            if (schema == null) return;
            foreach (var o in schema.Objects.Where(o => o.Kind == DbObjectKind.Table || o.Kind == DbObjectKind.View))
            {
                var kind = o.Kind == DbObjectKind.View ? CompletionItemKind.View : CompletionItemKind.Table;
                items.Add(new CompletionItem(o.QualifiedName, "'" + (Quote(o.Schema) + "." + Quote(o.Name)).Replace("'", "''") + "'", kind,
                    o.Kind.ToString().ToLowerInvariant() + DescribeColumns(o)) { Priority = Ci.Equals(o.Schema, schema.DefaultSchema) ? 10 : 11 });
            }
        }

        /// <summary>ALTER|DROP INDEX: each index inserts "name ON table"; ALTER also offers ALL.</summary>
        private void AddIndexes(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            if (ctx.AllowsAllIndexes)
                items.Add(new CompletionItem(Kw("ALL"), Kw("ALL") + " " + Kw("ON") + " ", CompletionItemKind.Keyword, "every index of a table") { Priority = -1 });
            if (schema == null) return;
            foreach (var o in schema.Objects)
                foreach (var ix in o.Indexes)
                {
                    var table = Options.AlwaysQualifySchema || !Ci.Equals(o.Schema, schema.DefaultSchema) ? Quote(o.Schema) + "." + Quote(o.Name) : Quote(o.Name);
                    items.Add(new CompletionItem(ix.Name, Quote(ix.Name) + " " + Kw("ON") + " " + table, CompletionItemKind.Index,
                        ix.Describe() + " on " + o.QualifiedName) { Priority = ix.IsPrimaryKey ? 11 : 10 });
                }
        }

        /// <summary>
        /// ALTER TABLE t DROP CONSTRAINT | (foreign keys, primary key and unique constraints), CHECK|NOCHECK CONSTRAINT |
        /// (ALL and foreign keys), ENABLE|DISABLE TRIGGER | (ALL and the table's triggers). Check and default
        /// constraints aren't in the metadata.
        /// </summary>
        private void AddTableMembers(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            var obj = ctx.InsertTarget?.Object;
            if (ctx.ClauseKeyword != "CONSTRAINT")
                items.Add(new CompletionItem(Kw("ALL"), Kw("ALL"), CompletionItemKind.Keyword,
                    ctx.ClauseKeyword == "TRIGGER" ? "every trigger of the table" : "every constraint of the table") { Priority = -1 });
            if (obj == null || schema == null) return;
            if (ctx.ClauseKeyword == "TRIGGER")
            {
                foreach (var tr in schema.Objects.Where(o => o.Kind == DbObjectKind.Trigger && Ci.Equals(o.Parent, obj.QualifiedName)))
                    items.Add(new CompletionItem(tr.Name, Quote(tr.Name), CompletionItemKind.Trigger, "trigger on " + obj.QualifiedName));
                return;
            }
            foreach (var fk in schema.ForeignKeys.Where(f => Ci.Equals(f.FromSchema, obj.Schema) && Ci.Equals(f.FromTable, obj.Name)))
                items.Add(new CompletionItem(fk.Name, Quote(fk.Name), CompletionItemKind.Index,
                    $"foreign key ({string.Join(", ", fk.FromColumns)}) → {fk.ToSchema}.{fk.ToTable}"));
            if (ctx.ClauseKeyword == "CONSTRAINT")
                foreach (var ix in obj.Indexes.Where(i => i.IsPrimaryKey || i.IsUnique))
                    items.Add(new CompletionItem(ix.Name, Quote(ix.Name), CompletionItemKind.Index, ix.Describe()));
        }

        private static string DescribeDatabase(DatabaseSchema schema, string name)
        {
            var d = schema.FindDatabase(name);
            if (d == null) return Ci.Equals(name, schema.DatabaseName) ? "current database" : "database";
            var state = Ci.Equals(d.State, "ONLINE") ? "" : d.State.ToLowerInvariant() + ", ";
            return $"database, {state}{d.RecoveryModel.ToLowerInvariant()} recovery, level {d.CompatibilityLevel}";
        }

        /// <summary>Applies keyword case to everything outside '…' literals.</summary>
        private string KwOutsideQuotes(string text)
        {
            var sb = new StringBuilder(text.Length);
            var segment = new StringBuilder();
            var inString = false;
            foreach (var c in text)
            {
                if (c == '\'')
                {
                    if (!inString) { sb.Append(Kw(segment.ToString())); segment.Clear(); }
                    inString = !inString;
                    sb.Append(c);
                }
                else if (inString) sb.Append(c);
                else segment.Append(c);
            }
            sb.Append(Kw(segment.ToString()));
            return sb.ToString();
        }

        private static void AddPrincipals(CompletionContext ctx, DatabaseSchema schema, List<CompletionItem> items)
        {
            if (schema == null) return;
            foreach (var p in schema.Principals)
            {
                if (!ctx.PrincipalKinds.Contains(p.Kind)) continue;
                var kind = p.IsRole ? CompletionItemKind.Role : p.Kind == DbPrincipalKind.Login ? CompletionItemKind.Login : CompletionItemKind.User;
                items.Add(new CompletionItem(p.Name, Quote(p.Name), kind, DescribePrincipal(p))
                {
                    // Own principals before built-in roles; users and logins before roles.
                    Priority = (p.IsFixedRole ? 20 : 10) + (p.IsRole ? 1 : 0),
                });
            }
        }

        public static string DescribePrincipal(DbPrincipal p)
        {
            var sb = new StringBuilder(p.KindText);
            if (!string.IsNullOrEmpty(p.TypeDescription) && !p.IsRole) sb.Append(" (").Append(p.TypeDescription).Append(')');
            if (p.IsDisabled) sb.Append(", disabled");
            if (p.IsRole && p.Members.Count > 0)
                sb.Append(" — members: ").Append(string.Join(", ", p.Members.Take(8))).Append(p.Members.Count > 8 ? ", …" : "");
            else if (!p.IsRole && p.MemberOf.Count > 0)
                sb.Append(" — member of: ").Append(string.Join(", ", p.MemberOf.Take(8))).Append(p.MemberOf.Count > 8 ? ", …" : "");
            return sb.ToString();
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
