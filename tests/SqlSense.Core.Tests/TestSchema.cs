using SqlSense.Core.Schema;

namespace SqlSense.Core.Tests
{
    internal static class TestSchema
    {
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

            s.ForeignKeys.Add(new DbForeignKey("FK_Orders_Customers", "sales", "SalesOrderHeader", "dbo", "Customers",
                new[] { "CustomerID" }, new[] { "CustomerID" }));
            return s;
        }
    }
}
