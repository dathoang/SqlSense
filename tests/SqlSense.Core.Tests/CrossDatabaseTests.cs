using System.Linq;
using SqlSense.Core.Completion;
using SqlSense.Core.Schema;
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
        public void A_server_only_stand_in_lists_databases_while_the_used_database_loads()
        {
            var source = TestSchema.WithOtherDatabases();
            source.Principals.Add(new DbPrincipal("appLogin", DbPrincipalKind.Login));
            source.Principals.Add(new DbPrincipal("appUser", DbPrincipalKind.User));
            var standIn = DatabaseSchema.ServerOnlyFrom(source, "Archive");

            Assert.True(standIn.ServerOnly);
            Assert.Equal("Archive", standIn.DatabaseName);
            Assert.Empty(standIn.Objects);
            Assert.Equal(new[] { "Shop", "master", "Pending" }, standIn.OtherDatabases);
            Assert.Equal(new[] { "REMOTE" }, standIn.LinkedServers);
            Assert.All(standIn.Principals, p => Assert.True(p.IsServerLevel));
            Assert.Contains(standIn.Principals, p => p.Name == "appLogin");
            Assert.DoesNotContain(standIn.Principals, p => p.Name == "appUser");

            var caret = "USE ".Length;
            var names = _engine.GetCompletions("USE ", caret, standIn).Items.Select(i => i.DisplayText).ToList();
            Assert.Contains("Shop", names);
            Assert.Contains("Archive", names);
        }

        [Fact]
        public void A_database_resolved_to_a_stand_in_is_still_pending()
        {
            var source = TestSchema.WithOtherDatabases();
            var current = source.Resolver;
            source.Resolver = (server, db) => server == null && db == "Pending" ? DatabaseSchema.ServerOnlyFrom(source, db) : current(server, db);
            Assert.True(_engine.GetCompletions("SELECT * FROM Pending.", 22, source).SchemaPending);
        }

        [Fact]
        public void Picked_objects_in_another_database_insert_just_their_name()
        {
            Assert.Equal("OldOrders", Complete("SELECT * FROM Archive.hist.|").Items.Single().InsertText);
        }

        [Theory]
        [InlineData("INSERT Archive.hist.|", "OldOrders (OldOrderID)\nVALUES (0)")]
        [InlineData("INSERT INTO Archive.hist.Old|", "OldOrders (OldOrderID)\nVALUES (0)")]
        [InlineData("INSERT INTO REMOTE.Sales2.dbo.|", "Invoices (InvoiceNo)\nVALUES ('')")]
        public void Insert_into_another_database_generates_the_column_list(string sql, string expected)
        {
            var item = Complete(sql).Items.Single();
            Assert.Equal(expected, item.InsertText);
            Assert.Equal(expected.LastIndexOf('(') + 1, item.CaretOffset);
        }

        [Fact]
        public void Insert_column_list_of_another_database_offers_its_columns()
        {
            Assert.Contains("OldOrderID", Names("INSERT INTO Archive.hist.OldOrders (|"));
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
