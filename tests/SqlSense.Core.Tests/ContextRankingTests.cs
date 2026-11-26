using System.Linq;
using SqlSense.Core.Completion;
using SqlSense.Core.Schema;
using Xunit;

namespace SqlSense.Core.Tests
{
    /// <summary>Alias style, the "sys." commit fix and context-aware ordering (joins, comparisons).</summary>
    public class ContextRankingTests
    {
        private readonly CompletionEngine _engine = new CompletionEngine { NewLine = "\n" };

        private CompletionResult Complete(string textWithCaret, DatabaseSchema schema = null, CompletionEngine engine = null)
        {
            var caret = textWithCaret.IndexOf('|');
            return (engine ?? _engine).GetCompletions(textWithCaret.Remove(caret, 1), caret, schema ?? TestSchema.Create());
        }

        private static DatabaseSchema WithSystemObjects()
        {
            var s = TestSchema.Create();
            s.Schemas.Add("sys");
            s.Add(new DbObject("sys", "syscolumns", DbObjectKind.View));
            s.Add(new DbObject("sys", "sysobjects", DbObjectKind.View));
            s.Add(new DbObject("sys", "tables", DbObjectKind.View));
            return s;
        }

        // ---------------- AS before aliases ----------------

        [Fact]
        public void Alias_is_added_without_AS_by_default()
        {
            Assert.Equal("dbo.Customers c", Complete("SELECT * FROM Cust|").Items.First().InsertText);
        }

        [Fact]
        public void AS_before_alias_is_an_option()
        {
            var engine = new CompletionEngine { NewLine = "\n", Options = new CompletionOptions { AliasWithAs = true } };
            Assert.Equal("dbo.Customers AS c", Complete("SELECT * FROM Cust|", engine: engine).Items.First().InsertText);
        }

        // ---------------- sys. ----------------

        [Fact]
        public void Exact_schema_name_beats_tables_starting_with_it()
        {
            var first = Complete("SELECT * FROM sys|", WithSystemObjects()).Items.First();
            Assert.Equal(CompletionItemKind.Schema, first.Kind);
            Assert.Equal("sys", first.InsertText);
        }

        [Fact]
        public void Exact_match_ranks_above_prefix_matches()
        {
            var ranked = CompletionEngine.Rank(new[]
            {
                new CompletionItem("sys.syscolumns", "sys.syscolumns s", CompletionItemKind.View) { Priority = 1 },
                new CompletionItem("sys", "sys", CompletionItemKind.Schema) { Priority = 20 },
            }, "sys");
            Assert.Equal("sys", ranked[0].DisplayText);
        }

        [Theory]
        [InlineData("sys", "sys", CompletionItemKind.Schema, "sys", true)]
        [InlineData("mas", "master", CompletionItemKind.Database, "master", true)]
        [InlineData("so", "soh", CompletionItemKind.Alias, "soh", true)]
        [InlineData("sys", "sys.syscolumns", CompletionItemKind.View, "sys.syscolumns s", false)]
        [InlineData("sys", "sys.syscolumns", CompletionItemKind.View, "sys.syscolumns", false)]
        [InlineData("Customers", "dbo.Customers", CompletionItemKind.Table, "dbo.Customers", true)]
        [InlineData("Customers", "dbo.Customers", CompletionItemKind.Table, "dbo.Customers c", false)]
        [InlineData("fn", "dbo.fn_Tax", CompletionItemKind.ScalarFunction, "dbo.fn_Tax()", false)]
        public void Dot_commits_only_names_that_can_be_qualified(string typed, string display, CompletionItemKind kind, string insert, bool commits)
        {
            Assert.Equal(commits, CompletionMatcher.CommitsOnDot(new CompletionItem(display, insert, kind), typed));
        }

        [Fact]
        public void Sys_dot_lists_system_views()
        {
            var r = Complete("SELECT * FROM sys.|", WithSystemObjects());
            Assert.Contains(r.Items, i => i.DisplayText == "tables");
        }

        // ---------------- JOIN ----------------

        [Fact]
        public void Join_lists_related_tables_first()
        {
            var first = Complete("SELECT * FROM dbo.Customers c JOIN |").Items.First();
            Assert.Equal("sales.SalesOrderHeader", first.DisplayText);
            Assert.StartsWith("related to c (FK_Orders_Customers)", first.Description);
        }

        [Fact]
        public void From_does_not_reorder_by_relation()
        {
            var r = Complete("SELECT * FROM |");
            Assert.DoesNotContain(r.Items, i => i.Description.StartsWith("related to"));
        }

        [Fact]
        public void Join_condition_matches_on_its_columns_and_aliases()
        {
            Assert.True(CompletionMatcher.Score("Cust", "soh.CustomerID = c.CustomerID") >= 800);
            Assert.True(CompletionMatcher.Score("soh", "soh.CustomerID = c.CustomerID") >= 800);
            Assert.Equal(0, CompletionMatcher.Score("Total", "soh.CustomerID = c.CustomerID"));
            // Typing a full column name still prefers the column itself.
            Assert.True(CompletionMatcher.Score("CustomerID", "soh.CustomerID = c.CustomerID") < 1000);
        }

        [Fact]
        public void Join_on_with_typed_prefix_keeps_the_condition_first()
        {
            var r = Complete("SELECT * FROM dbo.Customers AS c INNER JOIN sales.SalesOrderHeader AS soh ON cu|");
            Assert.Equal("soh.CustomerID = c.CustomerID", r.Items[0].InsertText);
        }

        [Fact]
        public void Join_on_alias_dot_offers_the_condition_from_that_side()
        {
            var r = Complete("SELECT * FROM dbo.Customers AS c INNER JOIN sales.SalesOrderHeader AS soh ON c.|");
            Assert.Equal(CompletionItemKind.JoinCondition, r.Items[0].Kind);
            Assert.Equal("c.CustomerID = soh.CustomerID", r.Items[0].DisplayText);
            Assert.Equal("CustomerID = soh.CustomerID", r.Items[0].InsertText);

            r = Complete("SELECT * FROM dbo.Customers AS c INNER JOIN sales.SalesOrderHeader AS soh ON soh.|");
            Assert.Equal("CustomerID = c.CustomerID", r.Items[0].InsertText);
        }

        [Fact]
        public void Join_on_lists_key_columns_of_the_joined_table_before_other_columns()
        {
            var r = Complete("SELECT * FROM dbo.Customers AS c INNER JOIN sales.SalesOrderHeader AS soh ON |");
            var cols = r.Items.Where(i => i.Kind == CompletionItemKind.Column).Select(i => i.DisplayText).ToList();
            Assert.True(cols.IndexOf("soh.CustomerID") < cols.IndexOf("c.CustomerID"));
            Assert.True(cols.IndexOf("c.CustomerID") < cols.IndexOf("soh.Total"));
            Assert.True(cols.IndexOf("soh.OrderID") < cols.IndexOf("c.Name"));
        }

        [Fact]
        public void Right_side_of_comparison_lists_the_partner_column_first()
        {
            var r = Complete("SELECT * FROM dbo.Customers AS c INNER JOIN sales.SalesOrderHeader AS soh ON soh.CustomerID = c.|");
            Assert.Equal("CustomerID", r.Items[0].DisplayText);
            Assert.StartsWith("matches soh.CustomerID", r.Items[0].Description);

            r = Complete("SELECT * FROM dbo.Customers AS c JOIN sales.SalesOrderHeader AS soh ON soh.CustomerID = c.CustomerID WHERE c.CustomerID = |");
            Assert.Equal("soh.CustomerID", r.Items[0].DisplayText);
        }

        [Fact]
        public void Comparison_does_not_suggest_the_same_column_back()
        {
            var r = Complete("SELECT * FROM dbo.Customers AS c JOIN sales.SalesOrderHeader AS soh ON soh.CustomerID = c.CustomerID WHERE c.Name = |");
            Assert.Equal(CompletionItemKind.Column, r.Items[0].Kind);
            Assert.DoesNotContain(r.Items, i => i.Description.StartsWith("matches"));
        }
    }
}
