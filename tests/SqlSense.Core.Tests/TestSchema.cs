using SqlSense.Core.Schema;

namespace SqlSense.Core.Tests
{
    internal static class TestSchema
    {
        /// <summary>
        /// Shop plus database Archive (loaded), database Pending (still loading) and linked server REMOTE with
        /// database Sales2.
        /// </summary>
        public static DatabaseSchema WithOtherDatabases()
        {
            var s = Create();
            s.OtherDatabases.AddRange(new[] { "Archive", "Pending" });
            s.LinkedServers.Add("REMOTE");

            var archive = new DatabaseSchema("Archive");
            archive.Schemas.AddRange(new[] { "dbo", "hist" });
            archive.Add(new DbObject("hist", "OldOrders", DbObjectKind.Table))
                .Columns.Add(new DbColumn("OldOrderID", "int", false, false, false, 1));
            archive.Add(new DbObject("dbo", "usp_Purge", DbObjectKind.Procedure));

            var remoteCatalog = new DatabaseSchema("master") { Server = "REMOTE" };
            remoteCatalog.OtherDatabases.Add("Sales2");
            var sales2 = new DatabaseSchema("Sales2");
            sales2.Schemas.Add("dbo");
            sales2.Add(new DbObject("dbo", "Invoices", DbObjectKind.Table))
                .Columns.Add(new DbColumn("InvoiceNo", "varchar(20)", false, false, false, 1));

            s.Resolver = (server, db) =>
                server == null ? (db == "Archive" ? archive : null)
                : db == null ? remoteCatalog
                : db == "Sales2" ? sales2 : null;
            return s;
        }

        public static DatabaseSchema Create()
        {
            var s = new DatabaseSchema("Shop");
            s.Schemas.AddRange(new[] { "dbo", "sales" });
            s.OtherDatabases.Add("master");

            var customers = s.Add(new DbObject("dbo", "Customers", DbObjectKind.Table));
            customers.Columns.Add(new DbColumn("CustomerID", "int", false, true, false, 1));
            customers.Columns.Add(new DbColumn("Name", "nvarchar(100)", false, false, false, 2));
            customers.Columns.Add(new DbColumn("Email", "varchar(200)", true, false, false, 3));

            var orders = s.Add(new DbObject("sales", "SalesOrderHeader", DbObjectKind.Table));
            orders.Columns.Add(new DbColumn("OrderID", "int", false, true, false, 1));
            orders.Columns.Add(new DbColumn("CustomerID", "int", false, false, false, 2));
            orders.Columns.Add(new DbColumn("OrderDate", "datetime2", false, false, false, 3));
            orders.Columns.Add(new DbColumn("Total", "money", false, false, false, 4));

            var view = s.Add(new DbObject("dbo", "ActiveCustomers", DbObjectKind.View));
            view.Columns.Add(new DbColumn("CustomerID", "int", false, false, false, 1));

            var proc = s.Add(new DbObject("dbo", "usp_GetOrders", DbObjectKind.Procedure));
            proc.Parameters.Add(new DbParameter("@CustomerID", "int", false, false, 1));
            proc.Parameters.Add(new DbParameter("@From", "date", false, true, 2));

            s.Add(new DbObject("dbo", "fn_Tax", DbObjectKind.ScalarFunction));
            s.Add(new DbObject("dbo", "fn_OrdersOf", DbObjectKind.TableFunction))
                .Columns.Add(new DbColumn("OrderID", "int", false, false, false, 1));
            s.Add(new DbObject("dbo", "trg_Customers_Audit", DbObjectKind.Trigger) { Parent = "dbo.Customers" });

            s.ForeignKeys.Add(new DbForeignKey("FK_Orders_Customers", "sales", "SalesOrderHeader", "dbo", "Customers",
                new[] { "CustomerID" }, new[] { "CustomerID" }));

            var pk = new DbIndex("PK_Customers", "CLUSTERED", true, true);
            pk.KeyColumns.Add("CustomerID");
            customers.Indexes.Add(pk);
            var ix = new DbIndex("IX_Orders_Customer", "NONCLUSTERED", false, false);
            ix.KeyColumns.Add("CustomerID");
            ix.IncludedColumns.Add("Total");
            orders.Indexes.Add(ix);

            s.Databases.Add(new DbDatabase("Shop", "ONLINE", "FULL", 160));
            s.Databases.Add(new DbDatabase("master", "ONLINE", "SIMPLE", 160));
            s.Databases.Add(new DbDatabase("Shop_copy", "RESTORING", "FULL", 160));
            s.Files.Add(new DbFile("Shop", "ROWS", @"D:\Data\Shop.mdf", 2048));
            s.Files.Add(new DbFile("Shop_log", "LOG", @"L:\Log\Shop_log.ldf", 512));
            s.Backups.Add(new DbBackup("Shop", @"B:\Backup\Shop_log_2.trn", 'L', new System.DateTime(2026, 10, 22, 23, 30, 0)));
            s.Backups.Add(new DbBackup("Shop", @"B:\Backup\Shop_full.bak", 'D', new System.DateTime(2026, 10, 22, 22, 0, 0)));
            s.Backups.Add(new DbBackup("master", @"B:\Backup\master.bak", 'D', new System.DateTime(2026, 10, 21, 22, 0, 0)));

            var bob = new DbPrincipal("bob", DbPrincipalKind.User, "SQL_USER") { Default = "dbo", Login = "bob" };
            var alice = new DbPrincipal(@"CORP\alice", DbPrincipalKind.User, "WINDOWS_USER") { Default = "sales" };
            var readers = new DbPrincipal("app_readers", DbPrincipalKind.DatabaseRole);
            var dataReader = new DbPrincipal("db_datareader", DbPrincipalKind.DatabaseRole) { IsFixedRole = true };
            readers.Members.Add("bob");
            bob.MemberOf.Add("app_readers");
            s.Principals.AddRange(new[]
            {
                bob, alice, readers, dataReader,
                new DbPrincipal("reporting_app", DbPrincipalKind.ApplicationRole),
                new DbPrincipal("bob", DbPrincipalKind.Login, "SQL_LOGIN") { Default = "Shop" },
                new DbPrincipal("old_login", DbPrincipalKind.Login, "SQL_LOGIN") { IsDisabled = true },
                new DbPrincipal("sysadmin", DbPrincipalKind.ServerRole) { IsFixedRole = true },
            });
            return s;
        }
    }
}
