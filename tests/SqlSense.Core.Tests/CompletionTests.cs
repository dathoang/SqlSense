using System.Linq;
using SqlSense.Core.Analysis;
using SqlSense.Core.Completion;
using SqlSense.Core.Schema;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class CompletionTests
    {
        private readonly CompletionEngine _engine = new CompletionEngine { NewLine = "\n" };

        /// <summary>"|" marks the caret.</summary>
        private CompletionResult Complete(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(caret, 1);
            return _engine.GetCompletions(text, caret, TestSchema.Create());
        }

        [Theory]
        [InlineData("SELECT * FROM |", ContextKind.TableSource)]
        [InlineData("SELECT * FROM dbo.Customers c JOIN |", ContextKind.TableSource)]
        [InlineData("SELECT * FROM dbo.Customers c WHERE |", ContextKind.Expression)]
        [InlineData("SELECT | FROM dbo.Customers", ContextKind.Expression)]
        [InlineData("EXEC |", ContextKind.Procedure)]
        [InlineData("INSERT INTO |", ContextKind.TableSource)]
        [InlineData("INSERT INTO dbo.Customers (|", ContextKind.InsertColumns)]
        [InlineData("DECLARE @x |", ContextKind.DataType)]
        [InlineData("SELECT CAST(1 AS |", ContextKind.DataType)]
        [InlineData("USE |", ContextKind.Database)]
        [InlineData("SELECT 'abc|'", ContextKind.None)]
        [InlineData("-- FROM |", ContextKind.None)]
        [InlineData("SELECT 1 /* FROM | */", ContextKind.None)]
        [InlineData("SELECT * FROM dbo.Customers AS |", ContextKind.AliasName)]
        [InlineData("SELECT @|", ContextKind.Variable)]
        [InlineData("|", ContextKind.Keyword)]
        [InlineData("SELECT 1;\n|", ContextKind.Keyword)]
        [InlineData("UPDATE |", ContextKind.TableSource)]
        [InlineData("UPDATE dbo.Customers SET |", ContextKind.Expression)]
        [InlineData("DELETE FROM |", ContextKind.TableSource)]
        public void Detects_context(string sql, ContextKind expected)
        {
            Assert.Equal(expected, Complete(sql).Context.Kind);
        }

        [Fact]
        public void From_lists_tables_with_alias_and_schema()
        {
            var r = Complete("SELECT * FROM Cust|");
            var first = r.Items.First();
            Assert.Equal("dbo.Customers", first.DisplayText);
            Assert.Equal("dbo.Customers AS c", first.InsertText);
        }

        [Theory]
        [InlineData("USE K99|", "KANGAROO_APP99")]
        [InlineData("use k99|", "KANGAROO_APP99")]
        [InlineData("SELECT * FROM c0|", "dbo.ct00")]
        [InlineData("select * from c0|", "dbo.ct00")]
        public void Word_segment_search_finds_databases_and_tables(string sql, string expected)
        {
            var s = TestSchema.Create();
            s.OtherDatabases.Add("KANGAROO_APP99");
            s.Add(new DbObject("dbo", "ct00", DbObjectKind.Table));
            var caret = sql.IndexOf('|');
            var r = _engine.GetCompletions(sql.Remove(caret, 1), caret, s);
            Assert.Equal(expected, r.Items.First().DisplayText);
        }

        [Fact]
        public void Initials_match_camel_case_names()
        {
            var r = Complete("SELECT * FROM soh|");
            Assert.Equal("sales.SalesOrderHeader", r.Items.First().DisplayText);
            Assert.Equal("sales.SalesOrderHeader AS soh", r.Items.First().InsertText);
        }

        [Theory]
        [InlineData("isms", "is_ms_shipped")]
        [InlineData("ismssh", "is_ms_shipped")]
        [InlineData("msship", "is_ms_shipped")]
        [InlineData("soh", "SalesOrderHeader")]
        [InlineData("salord", "SalesOrderHeader")]
        [InlineData("custid", "CustomerID")]
        [InlineData("xmldat", "GetXMLData")]
        [InlineData("objid", "object_id")]
        [InlineData("K99", "KANGAROO_APP99")]
        [InlineData("c0", "ct00")]
        [InlineData("c0", "dbo.ct00")]
        public void Word_segment_prefixes_match(string typed, string candidate)
        {
            Assert.InRange(CompletionMatcher.Score(typed, candidate), 600, 700);
        }

        [Theory]
        [InlineData("isms", "is_published")]
        [InlineData("sih", "SalesOrderHeader")]
        [InlineData("ohs", "SalesOrderHeader")]
        public void Word_segment_match_needs_order_and_word_starts(string typed, string candidate)
        {
            Assert.Equal(0, CompletionMatcher.Score(typed, candidate));
        }

        [Fact]
        public void Word_segment_match_prefers_leading_word_and_fewer_skips()
        {
            Assert.True(CompletionMatcher.Score("isms", "is_ms_shipped") > CompletionMatcher.Score("isms", "is_auto_ms_shipped"));
            Assert.True(CompletionMatcher.Score("isms", "is_ms_shipped") > CompletionMatcher.Score("isms", "uses_is_ms_shipped"));
        }

        [Fact]
        public void Alias_dot_lists_that_tables_columns()
        {
            var r = Complete("SELECT c.| FROM dbo.Customers AS c");
            Assert.Equal(new[] { "CustomerID", "Email", "Name" }, r.Items.Select(i => i.DisplayText).OrderBy(x => x));
        }

        [Fact]
        public void Columns_of_all_tables_in_scope_are_qualified_when_several()
        {
            var r = Complete("SELECT | FROM dbo.Customers c JOIN sales.SalesOrderHeader soh ON soh.CustomerID = c.CustomerID");
            var cols = r.Items.Where(i => i.Kind == CompletionItemKind.Column).Select(i => i.DisplayText).ToList();
            Assert.Contains("c.Name", cols);
            Assert.Contains("soh.Total", cols);
        }

        [Fact]
        public void Join_on_suggests_foreign_key_condition_first()
        {
            var r = Complete("SELECT * FROM dbo.Customers AS c INNER JOIN sales.SalesOrderHeader AS soh ON |");
            Assert.Equal(CompletionItemKind.JoinCondition, r.Items[0].Kind);
            Assert.Equal("soh.CustomerID = c.CustomerID", r.Items[0].InsertText);
            Assert.Single(r.Items, i => i.InsertText == "soh.CustomerID = c.CustomerID");
        }

        [Fact]
        public void Join_on_suggests_same_named_columns_without_schema()
        {
            var text = "WITH a AS (SELECT CustomerID, RegionCode, Name FROM x), b AS (SELECT CustomerID, RegionCode, Name, Total FROM y)\n" +
                       "SELECT * FROM a JOIN b ON ";
            var r = _engine.GetCompletions(text, text.Length, null);
            var conds = r.Items.Where(i => i.Kind == CompletionItemKind.JoinCondition).Select(i => i.InsertText).ToList();
            Assert.Equal(new[]
            {
                "b.CustomerID = a.CustomerID AND b.RegionCode = a.RegionCode",
                "b.CustomerID = a.CustomerID",
                "b.RegionCode = a.RegionCode",
                "b.Name = a.Name",
            }, conds);
            Assert.Equal(conds, r.Items.Take(conds.Count).Select(i => i.InsertText));
        }

        [Fact]
        public void Same_name_join_skips_identity_on_both_sides()
        {
            var r = Complete("SELECT * FROM dbo.Customers AS c JOIN dbo.Customers AS c2 ON |");
            var conds = r.Items.Where(i => i.Kind == CompletionItemKind.JoinCondition).Select(i => i.InsertText).ToList();
            Assert.DoesNotContain("c2.CustomerID = c.CustomerID", conds);
            Assert.Contains("c2.Name = c.Name", conds);
        }

        [Fact]
        public void Same_name_join_can_be_turned_off()
        {
            var engine = new CompletionEngine { NewLine = "\n", Options = new CompletionOptions { SuggestSameNameJoins = false } };
            var text = "WITH a AS (SELECT CustomerID FROM x), b AS (SELECT CustomerID FROM y) SELECT * FROM a JOIN b ON ";
            var r = engine.GetCompletions(text, text.Length, null);
            Assert.DoesNotContain(r.Items, i => i.Kind == CompletionItemKind.JoinCondition);
        }

        [Fact]
        public void Schema_dot_lists_objects_in_schema()
        {
            var r = Complete("SELECT * FROM sales.|");
            Assert.Single(r.Items);
            Assert.StartsWith("SalesOrderHeader", r.Items[0].InsertText);
        }

        [Fact]
        public void Exec_inserts_parameters()
        {
            var r = Complete("EXEC usp|");
            Assert.Equal("dbo.usp_GetOrders @CustomerID = 0, @From = SYSDATETIME()", r.Items[0].InsertText);
        }

        [Fact]
        public void Insert_into_generates_column_list_without_identity()
        {
            var r = Complete("INSERT INTO Custom|");
            Assert.Equal("dbo.Customers (Name, Email)\nVALUES (N'', '')", r.Items[0].InsertText);
        }

        [Fact]
        public void Insert_column_list_offers_all_columns()
        {
            var r = Complete("INSERT INTO dbo.Customers (|");
            Assert.Equal("Name, Email", r.Items[0].InsertText);
        }

        [Fact]
        public void Variables_declared_in_batch_are_offered()
        {
            var r = Complete("DECLARE @customerId int = 1;\nSELECT * FROM dbo.Customers WHERE CustomerID = @c|");
            Assert.Equal("@customerId", r.Items[0].DisplayText);
        }

        [Fact]
        public void Variables_do_not_leak_across_go()
        {
            var r = Complete("DECLARE @old int;\nGO\nSELECT @|");
            Assert.DoesNotContain(r.Items, i => i.DisplayText == "@old");
        }

        [Fact]
        public void Cte_columns_are_known()
        {
            var r = Complete("WITH recent AS (SELECT CustomerID, MAX(OrderDate) AS LastOrder FROM sales.SalesOrderHeader GROUP BY CustomerID)\nSELECT r.| FROM recent r");
            Assert.Equal(new[] { "CustomerID", "LastOrder" }, r.Items.Select(i => i.DisplayText).OrderBy(x => x));
        }

        [Fact]
        public void Temp_table_columns_are_known()
        {
            var r = Complete("CREATE TABLE #work (Id int, Amount money);\nSELECT w.| FROM #work w");
            Assert.Equal(new[] { "Amount", "Id" }, r.Items.Select(i => i.DisplayText).OrderBy(x => x));
        }

        [Fact]
        public void Keyword_context_offers_snippets()
        {
            var r = Complete("ssf|");
            Assert.Equal("ssf", r.Items[0].DisplayText);
            Assert.Equal("SELECT * FROM ", r.Items[0].InsertText);
        }

        [Fact]
        public void Update_alias_resolves_to_from_table()
        {
            var r = Complete("UPDATE c SET c.| FROM dbo.Customers c");
            Assert.Contains(r.Items, i => i.DisplayText == "Email");
        }

        [Fact]
        public void Bracketed_and_reserved_names_are_quoted()
        {
            Assert.Equal("[Order Details]", CompletionEngine.Quote("Order Details"));
            Assert.Equal("[Select]", CompletionEngine.Quote("Select"));
            Assert.Equal("Customers", CompletionEngine.Quote("Customers"));
        }

        [Fact]
        public void Star_expands_to_columns()
        {
            var sql = "SELECT * FROM dbo.Customers";
            var edit = StarExpander.TryExpand(sql, "SELECT *".Length, TestSchema.Create(), "\n");
            Assert.NotNull(edit);
            Assert.Equal("CustomerID,\n       Name,\n       Email", edit.NewText);
        }

        [Fact]
        public void Qualified_star_expands_one_table()
        {
            var sql = "SELECT c.* FROM dbo.Customers c JOIN sales.SalesOrderHeader o ON o.CustomerID = c.CustomerID";
            var edit = StarExpander.TryExpand(sql, "SELECT c.*".Length, TestSchema.Create(), "\n");
            Assert.Equal("c.CustomerID,\n       c.Name,\n       c.Email", edit.NewText);
        }

        [Fact]
        public void Count_star_is_not_expanded()
        {
            var sql = "SELECT COUNT(*) FROM dbo.Customers";
            Assert.Null(StarExpander.TryExpand(sql, "SELECT COUNT(*".Length, TestSchema.Create()));
        }

        [Fact]
        public void Alias_generator_avoids_collisions_and_keywords()
        {
            Assert.Equal("soh", AliasGenerator.Generate("SalesOrderHeader", new string[0]));
            Assert.Equal("oi", AliasGenerator.Generate("order_items", new string[0]));
            Assert.Equal("c2", AliasGenerator.Generate("Customers", new[] { "c" }));
            Assert.Equal("as1", AliasGenerator.Generate("ArchiveStore", new string[0]));
        }
    }
}
