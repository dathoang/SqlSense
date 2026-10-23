using SqlSense.Core.Completion;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class QuickInfoTests
    {
        /// <summary>"|" marks the mouse position (just before the character under the pointer).</summary>
        private static QuickInfo Hover(string textWithMouse, bool withSchema = true)
        {
            var pos = textWithMouse.IndexOf('|');
            var text = textWithMouse.Remove(pos, 1);
            return QuickInfoProvider.Get(text, pos, withSchema ? TestSchema.Create() : null);
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.|Customers c", "table dbo.Customers")]
        [InlineData("SELECT * FROM |Customers", "table dbo.Customers")]
        [InlineData("SELECT * FROM dbo.Cust|omers", "table dbo.Customers")]
        [InlineData("SELECT * FROM |dbo.Customers", "schema dbo")]
        [InlineData("SELECT c.|Name FROM dbo.Customers c", "column dbo.Customers.Name")]
        [InlineData("SELECT |Name FROM dbo.Customers", "column dbo.Customers.Name")]
        [InlineData("SELECT dbo.Customers.|Email FROM dbo.Customers", "column dbo.Customers.Email")]
        [InlineData("SELECT * FROM Shop.dbo.|Customers", "table dbo.Customers")]
        [InlineData("SELECT * FROM |Shop.dbo.Customers", "database Shop")]
        [InlineData("EXEC |usp_GetOrders 1", "procedure dbo.usp_GetOrders")]
        [InlineData("SELECT |fn_Tax(1)", "scalar function dbo.fn_Tax")]
        [InlineData("SELECT * FROM |sales.SalesOrderHeader", "schema sales")]
        [InlineData("ALTER TRIGGER |trg_Customers_Audit", "trigger dbo.trg_Customers_Audit")]
        [InlineData("SELECT * FROM |ActiveCustomers", "view dbo.ActiveCustomers")]
        [InlineData("ALTER ROLE |app_readers ADD MEMBER bob", "database role app_readers")]
        [InlineData("ALTER ROLE app_readers ADD MEMBER |bob", "user bob")]
        [InlineData("ALTER LOGIN |bob DISABLE", "login bob")]
        [InlineData("GRANT SELECT ON dbo.Customers TO |reporting_app", "application role reporting_app")]
        [InlineData("ALTER SERVER ROLE |sysadmin ADD MEMBER bob", "server role sysadmin")]
        [InlineData("ALTER SCHEMA |sales TRANSFER dbo.x", "schema sales")]
        [InlineData("USE |master", "database master")]
        public void Names_the_object_under_the_mouse(string sql, string title)
        {
            Assert.Equal(title, Hover(sql)?.Title);
        }

        [Fact]
        public void Alias_describes_its_table()
        {
            var info = Hover("SELECT |c.Name FROM dbo.Customers AS c");
            Assert.Equal("table dbo.Customers", info.Title);
            Assert.Equal("alias c", info.Lines[0]);
            Assert.Contains("  Name nvarchar(100) not null", info.Lines);
        }

        [Fact]
        public void Table_lists_columns_and_foreign_keys()
        {
            var info = Hover("SELECT * FROM sales.|SalesOrderHeader");
            Assert.Contains("  OrderID int not null identity", info.Lines);
            Assert.Contains("  (CustomerID) → dbo.Customers (CustomerID)", info.Lines);        }

        [Fact]
        public void Span_covers_the_word()
        {
            var info = Hover("SELECT * FROM dbo.Cust|omers");
            Assert.Equal(18, info.Start);
            Assert.Equal(9, info.Length);
        }

        [Fact]
        public void Procedure_lists_parameters()
        {
            var info = Hover("EXEC dbo.|usp_GetOrders 1");
            Assert.Equal(new[] { "  @CustomerID int", "  @From date = default" }, info.Lines);
        }

        [Fact]
        public void Role_lists_members_and_user_its_roles()
        {
            Assert.Contains("members: bob", Hover("ALTER ROLE |app_readers ADD MEMBER bob").Lines);
            var bob = Hover("ALTER USER |bob WITH DEFAULT_SCHEMA = dbo");
            Assert.Contains("member of: app_readers", bob.Lines);
            Assert.Contains("default schema: dbo", bob.Lines);
            Assert.Contains("disabled", Hover("DROP LOGIN |old_login").Lines);
        }

        [Fact]
        public void Variables_ctes_and_temp_tables()
        {
            Assert.Equal("variable @x", Hover("DECLARE @x int = 1; SELECT |@x").Title);
            Assert.Equal("int", Hover("DECLARE @x int = 1; SELECT |@x").Lines[0]);
            Assert.Equal("CTE o", Hover("WITH o AS (SELECT 1 AS a) SELECT * FROM |o").Title);
            Assert.Equal("temp table #t", Hover("CREATE TABLE #t (a int); SELECT * FROM |#t").Title);
            Assert.Equal("column o.a", Hover("WITH o AS (SELECT 1 AS a) SELECT |a FROM o").Title);
        }

        [Fact]
        public void Built_in_functions_work_without_schema()
        {
            Assert.StartsWith("function ", Hover("SELECT |ISNULL(a, 0)", withSchema: false).Title);
            Assert.StartsWith("function ", Hover("SELECT |LEFT(a, 2)", withSchema: false).Title);
            Assert.StartsWith("function @@ROWCOUNT", Hover("SELECT |@@ROWCOUNT", withSchema: false).Title);
        }

        [Fact]
        public void Databases_indexes_and_tables_show_maintenance_facts()
        {
            var db = Hover("BACKUP DATABASE |Shop TO DISK = N'x'");
            Assert.Equal("database Shop", db.Title);
            Assert.Contains("recovery: FULL, compatibility level 160", db.Lines);
            Assert.Contains(@"  Shop_log: log file, 512 MB — L:\Log\Shop_log.ldf", db.Lines);
            Assert.Contains(@"last full backup: 2026-10-22 22:00 — B:\Backup\Shop_full.bak", db.Lines);
            Assert.Equal("state: RESTORING", Hover("RESTORE DATABASE |Shop_copy").Lines[0]);

            var ix = Hover("ALTER INDEX |IX_Orders_Customer ON sales.SalesOrderHeader REBUILD");
            Assert.Equal("index IX_Orders_Customer", ix.Title);
            Assert.Equal("on sales.SalesOrderHeader", ix.Lines[1]);

            Assert.Contains("  PK_Customers: primary key clustered index (CustomerID)", Hover("SELECT * FROM |Customers").Lines);
        }

        [Theory]
        [InlineData("|SELECT 1")]
        [InlineData("SELECT 'dbo.|Customers'")]
        [InlineData("-- dbo.|Customers")]
        [InlineData("SELECT * FROM dbo.|Nothing")]
        [InlineData("SELECT 1 |")]
        public void Nothing_for_keywords_strings_comments_and_unknown_names(string sql)
        {
            Assert.Null(Hover(sql));
        }
    }
}
