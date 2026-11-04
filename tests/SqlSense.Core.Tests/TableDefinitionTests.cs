using System;
using System.Linq;
using SqlSense.Core.Analysis;
using SqlSense.Core.Completion;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class TableDefinitionTests
    {
        private readonly CompletionEngine _engine = new CompletionEngine { NewLine = "\n" };

        /// <summary>"|" marks the caret.</summary>
        private CompletionResult Complete(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(caret, 1);
            return _engine.GetCompletions(text, caret, TestSchema.Create());
        }

        private string[] Names(string sql) => Complete(sql).Items.Select(i => i.DisplayText).ToArray();

        private static ContextKind Kind(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            return ContextAnalyzer.Analyze(textWithCaret.Remove(caret, 1), caret, TestSchema.Create()).Kind;
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (Id |")]
        [InlineData("CREATE TABLE dbo.T (Id int, Name |")]
        [InlineData("CREATE TABLE dbo.T (\n  Id int NOT NULL,\n  Price |")]
        [InlineData("CREATE TABLE #t (a |")]
        [InlineData("DECLARE @t TABLE (a int, b |")]
        [InlineData("CREATE TYPE dbo.Ids AS TABLE (Id |")]
        [InlineData("CREATE FUNCTION f() RETURNS @r TABLE (Id |")]
        [InlineData("ALTER TABLE dbo.Customers ADD Phone |")]
        [InlineData("ALTER TABLE dbo.Customers ADD Phone varchar(20), Fax |")]
        [InlineData("ALTER TABLE dbo.Customers ALTER COLUMN Email |")]
        public void Column_name_is_followed_by_data_types(string sql)
        {
            var r = Complete(sql);
            Assert.Equal(ContextKind.DataType, r.Context.Kind);
            Assert.Contains("nvarchar", r.Items.Select(i => i.DisplayText));
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (Id int |")]
        [InlineData("CREATE TABLE dbo.T (Name nvarchar(50) |")]
        [InlineData("CREATE TABLE dbo.T (Id int IDENTITY(1, 1) |")]
        [InlineData("CREATE TABLE dbo.T (Id int NOT NULL |")]
        [InlineData("CREATE TABLE dbo.T (Id int PRIMARY KEY |")]
        [InlineData("ALTER TABLE dbo.Customers ADD Phone varchar(20) |")]
        public void Column_type_is_followed_by_column_options(string sql)
        {
            var names = Names(sql);
            Assert.Equal("NOT NULL", names[0]);
            Assert.Contains("IDENTITY(1, 1)", names);
            Assert.Contains("PRIMARY KEY", names);
            Assert.Contains("DEFAULT", names);
            Assert.Contains("REFERENCES", names);
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (Price decimal(|")]
        [InlineData("CREATE TABLE dbo.T (Price decimal(18, |")]
        [InlineData("CREATE TABLE dbo.T (Id int IDENTITY(|")]
        [InlineData("CREATE TABLE dbo.T (Id int CONSTRAINT |")]
        [InlineData("CREATE TABLE dbo.T (Name varchar(10) COLLATE |")]
        public void Nothing_inside_type_arguments_or_for_new_names(string sql)
        {
            Assert.Empty(Names(sql));
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (|", "CONSTRAINT")]
        [InlineData("CREATE TABLE dbo.T (Id int, |", "PRIMARY KEY ()")]
        [InlineData("ALTER TABLE dbo.Customers ADD |", "CONSTRAINT")]
        [InlineData("CREATE TABLE dbo.T (Id int NOT |", "NULL")]
        [InlineData("CREATE TABLE dbo.T (Id int, CONSTRAINT PK_T |", "PRIMARY KEY CLUSTERED ()")]
        [InlineData("ALTER TABLE dbo.Customers ADD CONSTRAINT DF_x |", "DEFAULT  FOR")]
        [InlineData("CREATE TABLE dbo.T (Id int CONSTRAINT DF_T_Id |", "DEFAULT")]
        [InlineData("CREATE TABLE dbo.T (Id int, CONSTRAINT PK_T PRIMARY |", "KEY CLUSTERED")]
        [InlineData("CREATE TABLE dbo.T (Id int, CONSTRAINT PK_T PRIMARY KEY |", "NONCLUSTERED")]
        [InlineData("CREATE TABLE dbo.T (Id int, CONSTRAINT FK_T FOREIGN KEY (Id) |", "REFERENCES")]
        [InlineData("CREATE TABLE dbo.T (CustomerID int FOREIGN KEY |", "REFERENCES")]
        [InlineData("CREATE TABLE dbo.T (CustomerID int REFERENCES dbo.Customers (CustomerID) |", "ON DELETE CASCADE")]
        [InlineData("CREATE TABLE dbo.T (CustomerID int REFERENCES dbo.Customers ON |", "DELETE")]
        [InlineData("CREATE TABLE dbo.T (CustomerID int REFERENCES dbo.Customers ON DELETE |", "SET NULL")]
        [InlineData("ALTER TABLE dbo.Customers |", "ALTER COLUMN")]
        [InlineData("ALTER TABLE dbo.Customers WITH |", "NOCHECK")]
        [InlineData("ALTER TABLE dbo.Customers WITH CHECK |", "ADD CONSTRAINT")]
        [InlineData("ALTER TABLE dbo.Customers DROP |", "CONSTRAINT")]
        [InlineData("ALTER TABLE dbo.Customers ALTER COLUMN Email varchar(300) |", "NOT NULL")]
        public void Offers_constraint_and_statement_keywords(string sql, string expected)
        {
            Assert.Contains(expected, Names(sql));
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (CustomerID int REFERENCES |")]
        [InlineData("CREATE TABLE dbo.T (Id int, CONSTRAINT FK_T FOREIGN KEY (Id) REFERENCES |")]
        [InlineData("ALTER TABLE x ADD CONSTRAINT FK_x FOREIGN KEY (a) REFERENCES |")]
        public void References_lists_tables(string sql)
        {
            Assert.Contains("dbo.Customers", Names(sql));
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (CustomerID int REFERENCES dbo.Customers (|", "CustomerID,Name,Email")]
        [InlineData("CREATE TABLE dbo.T (Id int, Code char(2), PRIMARY KEY (|", "Id,Code")]
        [InlineData("CREATE TABLE dbo.T (Id int, Code char(2), CONSTRAINT UQ UNIQUE NONCLUSTERED (Id, |", "Id,Code")]
        [InlineData("CREATE TABLE dbo.T (Id int, INDEX IX_T (|", "Id")]
        [InlineData("ALTER TABLE dbo.Customers ADD CONSTRAINT UQ_Email UNIQUE (|", "CustomerID,Name,Email")]
        [InlineData("ALTER TABLE dbo.Customers ADD CONSTRAINT DF_Name DEFAULT N'' FOR |", "CustomerID,Name,Email")]
        [InlineData("ALTER TABLE dbo.Customers ALTER COLUMN |", "CustomerID,Name,Email")]
        [InlineData("ALTER TABLE dbo.Customers DROP COLUMN |", "CustomerID,Name,Email")]
        [InlineData("ALTER TABLE dbo.Customers DROP COLUMN IF EXISTS Name, |", "CustomerID,Name,Email")]
        public void Column_lists_offer_the_right_columns(string sql, string expected)
        {
            Assert.Equal(expected, string.Join(",", Names(sql)));
        }

        [Theory]
        [InlineData("ALTER TABLE sales.SalesOrderHeader DROP CONSTRAINT |", "FK_Orders_Customers")]
        [InlineData("ALTER TABLE dbo.Customers DROP CONSTRAINT |", "PK_Customers")]
        [InlineData("ALTER TABLE sales.SalesOrderHeader NOCHECK CONSTRAINT |", "ALL,FK_Orders_Customers")]
        [InlineData("ALTER TABLE dbo.Customers DISABLE TRIGGER |", "ALL,trg_Customers_Audit")]
        public void Constraints_and_triggers_of_the_table(string sql, string expected)
        {
            Assert.Equal(expected, string.Join(",", Names(sql)));
        }

        [Theory]
        [InlineData("CREATE TABLE dbo.T (Total AS |", ContextKind.Expression)]
        [InlineData("CREATE TABLE dbo.T (Id int DEFAULT |", ContextKind.Expression)]
        [InlineData("CREATE TABLE dbo.T (Id int CHECK (|", ContextKind.Expression)]
        [InlineData("CREATE TABLE dbo.T (Id int);\n|", ContextKind.Keyword)]
        [InlineData("ALTER TABLE |", ContextKind.TableSource)]
        [InlineData("ALTER TABLE dbo.|", ContextKind.TableSource)]
        public void Other_positions(string sql, ContextKind expected)
        {
            Assert.Equal(expected, Kind(sql));
        }
    }
}
