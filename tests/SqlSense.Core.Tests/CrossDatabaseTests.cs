using System.Linq;
using SqlSense.Core.Completion;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class CrossDatabaseTests
    {
        private readonly CompletionEngine _engine = new CompletionEngine { NewLine = "\n" };

        /// <summary>"|" marks the caret.</summary>
        private CompletionResult Complete(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(caret, 1);
            return _engine.GetCompletions(text, caret, TestSchema.WithOtherDatabases());
        }

        private string Names(string sql) => string.Join(",", Complete(sql).Items.Select(i => i.DisplayText));

        [Fact]
        public void From_also_lists_databases_and_linked_servers_after_objects()
        {
            var items = Complete("SELECT * FROM |").Items;
            Assert.NotEqual(CompletionItemKind.Database, items[0].Kind);
            Assert.Contains(items, i => i.DisplayText == "Archive" && i.Kind == CompletionItemKind.Database);
            Assert.Contains(items, i => i.DisplayText == "REMOTE" && i.Kind == CompletionItemKind.Server);
            Assert.Contains(Complete("EXEC |").Items, i => i.DisplayText == "Archive");
        }

        [Theory]
        [InlineData("SELECT * FROM Archive.|", "dbo,hist")]
        [InlineData("SELECT * FROM Archive.hist.|", "OldOrders")]
        [InlineData("SELECT * FROM Archive..|", "")]
        [InlineData("EXEC Archive.dbo.|", "usp_Purge")]
        [InlineData("SELECT * FROM Shop.dbo.|", "ActiveCustomers,Customers,fn_OrdersOf")]
        [InlineData("SELECT * FROM REMOTE.|", "Sales2")]
        [InlineData("SELECT * FROM REMOTE.Sales2.|", "dbo")]
        [InlineData("SELECT * FROM REMOTE.Sales2.dbo.|", "Invoices")]
        [InlineData("SELECT * FROM REMOTE.Sales2..|", "Invoices")]
        [InlineData("SELECT Archive.hist.OldOrders.| FROM Archive.hist.OldOrders", "OldOrderID")]
        [InlineData("SELECT REMOTE.Sales2.dbo.Invoices.| FROM REMOTE.Sales2.dbo.Invoices", "InvoiceNo")]
        public void Completes_each_part_of_a_long_name(string sql, string expected)
        {
            Assert.Equal(expected, Names(sql));
        }

        [Theory]
        [InlineData("SELECT o.| FROM Archive.hist.OldOrders AS o", "OldOrderID")]
        [InlineData("SELECT i.| FROM REMOTE.Sales2.dbo.Invoices i", "InvoiceNo")]
        public void Aliases_of_other_databases_resolve_columns(string sql, string expected)
        {
            Assert.Equal(expected, Names(sql));
        }

        [Fact]
        public void A_database_still_loading_is_reported_so_the_list_can_reopen()
        {
            var r = Complete("SELECT * FROM Pending.|");
            Assert.Empty(r.Items);
            Assert.True(r.SchemaPending);
            Assert.False(Complete("SELECT * FROM Archive.|").SchemaPending);
            Assert.False(Complete("SELECT * FROM Nowhere.|").SchemaPending);
        }

        [Fact]
        public void Picked_objects_in_another_database_insert_just_their_name()
        {
            Assert.Equal("OldOrders", Complete("SELECT * FROM Archive.hist.|").Items.Single().InsertText);
        }

        private static QuickInfo Hover(string textWithMouse)
        {
            var pos = textWithMouse.IndexOf('|');
            return QuickInfoProvider.Get(textWithMouse.Remove(pos, 1), pos, TestSchema.WithOtherDatabases());
        }

        [Theory]
        [InlineData("SELECT * FROM Archive.hist.|OldOrders", "table hist.OldOrders")]
        [InlineData("SELECT * FROM Archive.|hist.OldOrders", "schema hist")]
        [InlineData("SELECT * FROM |Archive.hist.OldOrders", "database Archive")]
        [InlineData("SELECT * FROM REMOTE.Sales2.dbo.|Invoices", "table dbo.Invoices")]
        [InlineData("SELECT * FROM REMOTE.|Sales2.dbo.Invoices", "database Sales2")]
        [InlineData("SELECT * FROM |REMOTE.Sales2.dbo.Invoices", "linked server REMOTE")]
        [InlineData("SELECT i.|InvoiceNo FROM REMOTE.Sales2.dbo.Invoices i", "column dbo.Invoices.InvoiceNo")]
        [InlineData("SELECT Archive.hist.OldOrders.|OldOrderID FROM Archive.hist.OldOrders", "column hist.OldOrders.OldOrderID")]
        public void Hover_resolves_other_databases_and_linked_servers(string sql, string title)
        {
            Assert.Equal(title, Hover(sql)?.Title);
        }
    }
}
