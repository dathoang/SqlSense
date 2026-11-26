using System.Linq;
using SqlSense.Core.Analysis;
using SqlSense.Core.Completion;
using SqlSense.Core.Schema;
using Xunit;

namespace SqlSense.Core.Tests
{
    /// <summary>Select-list reuse in WHERE / GROUP BY / HAVING / ORDER BY, recent picks and type-aware ranking.</summary>
    public class SelectListTests
    {
        private readonly CompletionEngine _engine = new CompletionEngine { NewLine = "\n" };

        private CompletionResult Complete(string textWithCaret, CompletionEngine engine = null, DatabaseSchema schema = null)
        {
            var caret = textWithCaret.IndexOf('|');
            return (engine ?? _engine).GetCompletions(textWithCaret.Remove(caret, 1), caret, schema ?? TestSchema.Create());
        }

        private static DatabaseSchema WithOrderLines()
        {
            var s = TestSchema.Create();
            var lines = s.Add(new DbObject("sales", "OrderLines", DbObjectKind.Table));
            lines.Columns.Add(new DbColumn("OrderID", "int", false, false, false, 1));
            lines.Columns.Add(new DbColumn("Product", "nvarchar(50)", false, false, false, 2));
            lines.Columns.Add(new DbColumn("price", "money", false, false, false, 3));
            lines.Columns.Add(new DbColumn("quantity", "int", false, false, false, 4));
            return s;
        }

        // ---------------- parsing ----------------

        [Fact]
        public void Select_list_items_with_aliases_and_aggregates()
        {
            const string sql = "SELECT DISTINCT TOP (5) o.Product, price * quantity AS amount, total = SUM(price), COUNT(*) n, " +
                               "ROW_NUMBER() OVER (ORDER BY price) AS rn, 1 AS one, @x = 1 FROM sales.OrderLines o";
            var sig = Analysis.ScriptText.Significant(Lexing.SqlLexer.Tokenize(sql));
            var items = SelectList.Collect(sig, 0, sql);
            Assert.Equal(new[] { "Product", "amount", "total", "n", "rn" }, items.Select(i => i.Name));
            Assert.True(items[0].IsSimpleColumn);
            Assert.Equal("o.Product", items[0].Expression);
            Assert.Equal("price * quantity", items[1].Expression);
            Assert.True(items[1].HasOperator);
            Assert.True(items[2].IsAggregate);
            Assert.Equal("SUM(price)", items[2].Expression);
            Assert.True(items[3].IsAggregate);
            Assert.False(items[4].IsAggregate); // window function
        }

        [Fact]
        public void Multi_line_expressions_are_folded()
        {
            const string sql = "SELECT price\n       * quantity AS amount FROM t";
            var sig = Analysis.ScriptText.Significant(Lexing.SqlLexer.Tokenize(sql));
            Assert.Equal("price * quantity", SelectList.Collect(sig, 0, sql).Single().Expression);
        }

        // ---------------- WHERE ----------------

        [Fact]
        public void Where_offers_the_alias_and_inserts_its_expression()
        {
            var r = Complete("SELECT price * quantity AS amount\nFROM sales.OrderLines\nWHERE |", schema: WithOrderLines());
            var first = r.Items.First();
            Assert.Equal("amount", first.DisplayText);
            Assert.Equal("price * quantity", first.InsertText);
            Assert.Contains("price * quantity", first.Description);
        }

        [Theory]
        [InlineData("select price * quantity as amount\nfrom order\nwhere |")]
        [InlineData("select price * quantity as amount\nfrom orders\nwhere |")]
        public void Where_alias_works_without_metadata(string sql)
        {
            var caret = sql.IndexOf('|');
            var r = _engine.GetCompletions(sql.Remove(caret, 1), caret, null);
            Assert.Equal("amount", r.Items.First().DisplayText);
            Assert.Equal("price * quantity", r.Items.First().InsertText);
        }

        [Fact]
        public void Where_alias_filters_by_typed_prefix()
        {
            var r = Complete("SELECT price * quantity AS amount FROM sales.OrderLines WHERE price > 1 AND amo|", schema: WithOrderLines());
            Assert.Equal("price * quantity", r.Items.First().InsertText);
        }

        [Fact]
        public void Alias_inside_arithmetic_gets_parentheses()
        {
            var r = Complete("SELECT price + 1 AS gross FROM sales.OrderLines WHERE 2 * |", schema: WithOrderLines());
            Assert.Equal("(price + 1)", r.Items.First(i => i.DisplayText == "gross").InsertText);
        }

        [Fact]
        public void Where_skips_aggregates_and_plain_column_aliases()
        {
            var r = Complete("SELECT Product AS Product, SUM(price) AS total FROM sales.OrderLines WHERE |", schema: WithOrderLines());
            Assert.DoesNotContain(r.Items, i => i.DisplayText == "total");
            Assert.Single(r.Items, i => i.DisplayText == "Product");
        }

        [Fact]
        public void Subquery_where_uses_its_own_select_list()
        {
            var r = Complete("SELECT a AS outer_a FROM (SELECT price * 2 AS doubled FROM sales.OrderLines WHERE |) x", schema: WithOrderLines());
            Assert.Contains(r.Items, i => i.DisplayText == "doubled");
            Assert.DoesNotContain(r.Items, i => i.DisplayText == "outer_a");
        }

        // ---------------- HAVING / ORDER BY ----------------

        [Fact]
        public void Having_offers_aggregate_aliases_as_expressions()
        {
            var r = Complete("SELECT Product, SUM(price * quantity) AS total FROM sales.OrderLines GROUP BY Product HAVING |", schema: WithOrderLines());
            Assert.Equal("SUM(price * quantity)", r.Items.First(i => i.DisplayText == "total").InsertText);
            Assert.Equal("total", r.Items.First().DisplayText);
        }

        [Fact]
        public void Order_by_offers_aliases_as_they_are()
        {
            var r = Complete("SELECT price * quantity AS amount FROM sales.OrderLines ORDER BY |", schema: WithOrderLines());
            Assert.Equal("amount", r.Items.First().InsertText);
        }

        // ---------------- GROUP BY ----------------

        [Fact]
        public void Group_by_offers_all_non_aggregate_items_first()
        {
            var r = Complete("SELECT o.Product, YEAR(h.OrderDate) AS yr, SUM(o.price) AS total\n" +
                             "FROM sales.OrderLines o JOIN sales.SalesOrderHeader h ON h.OrderID = o.OrderID\nGROUP BY |", schema: WithOrderLines());
            Assert.Equal("(select columns)", r.Items[0].DisplayText);
            Assert.Equal("o.Product, YEAR(h.OrderDate)", r.Items[0].InsertText);
            Assert.Equal("YEAR(h.OrderDate)", r.Items.First(i => i.DisplayText == "yr").InsertText);
            Assert.DoesNotContain(r.Items, i => i.DisplayText == "total");
        }

        [Fact]
        public void Group_by_skips_items_already_listed()
        {
            var r = Complete("SELECT o.Product, YEAR(o.price) AS yr, SUM(o.price) AS total FROM sales.OrderLines o GROUP BY o.Product, |", schema: WithOrderLines());
            Assert.DoesNotContain(r.Items, i => i.DisplayText == "(select columns)");
            Assert.Equal("yr", r.Items.First().DisplayText);
        }

        // ---------------- recently used ----------------

        [Fact]
        public void Recently_picked_items_rank_higher()
        {
            var history = new CompletionHistory();
            var engine = new CompletionEngine { NewLine = "\n", History = history };
            var before = Complete("SELECT * FROM |", engine).Items.First(i => i.Kind == CompletionItemKind.Table || i.Kind == CompletionItemKind.View);
            Assert.Equal("dbo.ActiveCustomers", before.DisplayText);

            history.Record(Complete("SELECT * FROM |", engine).Items.First(i => i.DisplayText == "dbo.Customers"));
            var after = Complete("SELECT * FROM |", engine).Items.First(i => i.Kind == CompletionItemKind.Table || i.Kind == CompletionItemKind.View);
            Assert.Equal("dbo.Customers", after.DisplayText);

            engine.Options = new CompletionOptions { RankRecentlyUsed = false };
            Assert.Equal("dbo.ActiveCustomers", Complete("SELECT * FROM |", engine).Items.First(i => i.Kind == CompletionItemKind.Table || i.Kind == CompletionItemKind.View).DisplayText);
        }

        [Fact]
        public void History_forgets_the_oldest_beyond_capacity()
        {
            var history = new CompletionHistory(capacity: 2);
            var a = new CompletionItem("a", "a", CompletionItemKind.Column);
            history.Record(a);
            history.Record(new CompletionItem("b", "b", CompletionItemKind.Column));
            history.Record(new CompletionItem("c", "c", CompletionItemKind.Column));
            Assert.Equal(2, history.Count);
            Assert.Equal(0, history.Boost(a));
        }

        // ---------------- types ----------------

        [Fact]
        public void Comparison_ranks_same_named_variable_then_compatible_types()
        {
            var r = Complete("DECLARE @CustomerID int, @Name nvarchar(50), @When date;\nSELECT * FROM dbo.Customers c WHERE c.CustomerID = |");
            Assert.Equal("@CustomerID", r.Items[0].DisplayText);

            r = Complete("DECLARE @Since date, @Name nvarchar(50);\n" +
                         "SELECT * FROM sales.SalesOrderHeader soh JOIN dbo.Customers c ON c.CustomerID = soh.CustomerID WHERE soh.OrderDate > |");
            var names = r.Items.Select(i => i.DisplayText).ToList();
            Assert.True(names.IndexOf("@Since") < names.IndexOf("@Name"));
            Assert.True(names.IndexOf("@Since") < names.IndexOf("c.Name"));
        }

        [Fact]
        public void Comparison_ranks_columns_of_a_compatible_type_first()
        {
            var r = Complete("SELECT * FROM sales.SalesOrderHeader soh JOIN dbo.Customers c ON c.CustomerID = soh.CustomerID WHERE c.Name = |");
            var cols = r.Items.Where(i => i.Kind == CompletionItemKind.Column).Select(i => i.DisplayText).ToList();
            Assert.True(cols.IndexOf("c.Email") < cols.IndexOf("soh.Total"));
            Assert.True(cols.IndexOf("c.Email") < cols.IndexOf("c.CustomerID"));
        }

        [Theory]
        [InlineData("int", "money", true)]
        [InlineData("nvarchar(50)", "varchar(10)", true)]
        [InlineData("date", "datetime2", true)]
        [InlineData("int", "nvarchar(5)", false)]
        [InlineData(null, "int", false)]
        public void Type_families(string a, string b, bool compatible)
        {
            Assert.Equal(compatible, SqlTypes.Compatible(a, b));
        }
    }
}
