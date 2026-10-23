using System;
using System.Linq;
using SqlSense.Core.Analysis;
using SqlSense.Core.Completion;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class MaintenanceCompletionTests
    {
        private readonly CompletionEngine _engine = new CompletionEngine { NewLine = "\n", Now = () => new DateTime(2026, 10, 23, 14, 5, 0) };

        /// <summary>"|" marks the caret.</summary>
        private CompletionResult Complete(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(caret, 1);
            return _engine.GetCompletions(text, caret, TestSchema.Create());
        }

        private string[] Names(string sql) => Complete(sql).Items.Select(i => i.DisplayText).ToArray();

        [Theory]
        [InlineData("BACKUP DATABASE |", "master,Shop")]
        [InlineData("BACKUP LOG |", "master,Shop")]
        [InlineData("RESTORE DATABASE |", "master,Shop,Shop_copy")]
        [InlineData("RESTORE LOG |", "master,Shop,Shop_copy")]
        [InlineData("DBCC CHECKDB (|", "master,Shop")]
        [InlineData("DBCC SHRINKDATABASE (|", "master,Shop")]
        [InlineData("DBCC SHRINKFILE (|", "Shop,Shop_log")]
        [InlineData("ALTER DATABASE Shop MODIFY FILE (NAME = |", "Shop,Shop_log")]
        public void Suggests_databases_and_files(string sql, string expected)
        {
            Assert.Equal(expected, string.Join(",", Names(sql).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
        }

        [Fact]
        public void Backup_to_offers_a_dated_file_for_the_database()
        {
            var first = Complete("BACKUP DATABASE Shop TO |").Items.First();
            Assert.Equal("DISK = N'Shop_20261023_1405.bak'", first.InsertText);
            Assert.Equal("DISK = N'Shop_log_20261023_1405.trn'", Complete("BACKUP LOG [Shop] TO |").Items.First().InsertText);
            var disk = Complete("BACKUP DATABASE Shop TO |").Items.Single(i => i.InsertText == "DISK = N''");
            Assert.Equal(9, disk.CaretOffset);
        }

        [Fact]
        public void Restore_from_offers_recent_backups_of_that_database()
        {
            var names = Names("RESTORE DATABASE Shop FROM |");
            Assert.Equal(@"DISK = N'B:\Backup\Shop_full.bak'", names[0]);
            Assert.DoesNotContain(names, n => n.Contains("master.bak") || n.Contains(".trn"));
            Assert.Equal(@"DISK = N'B:\Backup\Shop_log_2.trn'", Names("RESTORE LOG Shop FROM |")[0]);
            Assert.Contains(@"DISK = N'B:\Backup\master.bak'", Names("RESTORE FILELISTONLY FROM |"));
        }

        [Theory]
        [InlineData("BACKUP |", "DATABASE")]
        [InlineData("RESTORE |", "FILELISTONLY FROM")]
        [InlineData("BACKUP DATABASE Shop TO DISK = N'x.bak' WITH |", "COMPRESSION")]
        [InlineData("BACKUP DATABASE Shop TO DISK = N'x.bak' WITH COMPRESSION, |", "COPY_ONLY")]
        [InlineData("RESTORE DATABASE Shop FROM DISK = N'x.bak' WITH |", "NORECOVERY")]
        [InlineData("RESTORE DATABASE Shop FROM DISK = N'x.bak' WITH MOVE N'a' TO N'b', |", "REPLACE")]
        [InlineData("DBCC |", "CHECKDB")]
        [InlineData("DBCC CHECKDB (Shop) WITH |", "NO_INFOMSGS")]
        [InlineData("ALTER DATABASE Shop |", "SET SINGLE_USER WITH ROLLBACK IMMEDIATE")]
        [InlineData("ALTER DATABASE Shop SET |", "RECOVERY SIMPLE")]
        [InlineData("ALTER DATABASE Shop SET RECOVERY |", "BULK_LOGGED")]
        [InlineData("ALTER INDEX ALL ON dbo.Customers |", "REBUILD")]
        [InlineData("UPDATE STATISTICS dbo.Customers |", "WITH FULLSCAN")]
        public void Offers_statement_options(string sql, string expected)
        {
            var r = Complete(sql);
            Assert.Equal(ContextKind.Options, r.Context.Kind);
            Assert.Contains(expected, r.Items.Select(i => i.DisplayText));
        }

        [Fact]
        public void Options_follow_keyword_case_but_keep_literals()
        {
            var engine = new CompletionEngine(new CompletionOptions { UppercaseKeywords = false });
            var items = engine.GetCompletions("DBCC ", 5, TestSchema.Create()).Items;
            Assert.Contains("freesystemcache('ALL')", items.Select(i => i.InsertText));
            var move = Complete("RESTORE DATABASE Shop FROM DISK = N'x' WITH |").Items.Single(i => i.DisplayText.StartsWith("MOVE"));
            Assert.Equal("MOVE N'' TO N''", move.InsertText);
            Assert.Equal(7, move.CaretOffset);
        }

        [Fact]
        public void Restore_with_move_to_suggests_nothing()
        {
            Assert.Empty(Complete("RESTORE DATABASE Shop FROM DISK = N'x' WITH MOVE N'a' TO |").Items);
        }

        [Fact]
        public void Dbcc_table_commands_insert_names_as_strings()
        {
            var item = Complete("DBCC CHECKIDENT (Cust|").Items.First();
            Assert.Equal("'dbo.Customers'", item.InsertText);
        }

        [Fact]
        public void Alter_index_lists_indexes_with_their_table()
        {
            var items = Complete("ALTER INDEX |").Items;
            Assert.Equal("ALL ON ", items[0].InsertText);
            var ix = items.Single(i => i.DisplayText == "IX_Orders_Customer");
            Assert.Equal("IX_Orders_Customer ON sales.SalesOrderHeader", ix.InsertText);
            Assert.Equal("nonclustered index (CustomerID) include (Total) on sales.SalesOrderHeader", ix.Description);
            Assert.DoesNotContain(Complete("DROP INDEX IF EXISTS |").Items, i => i.DisplayText == "ALL");
        }

        [Theory]
        [InlineData("ALTER INDEX ALL ON |")]
        [InlineData("ALTER INDEX IX_x ON |")]
        [InlineData("DROP INDEX IX_x ON |")]
        [InlineData("CREATE NONCLUSTERED INDEX IX_x ON |")]
        [InlineData("CREATE UNIQUE INDEX IX_x ON |")]
        [InlineData("UPDATE STATISTICS |")]
        public void Index_targets_are_tables(string sql)
        {
            var r = Complete(sql);
            Assert.Equal(ContextKind.TableSource, r.Context.Kind);
            Assert.Equal("dbo.Customers", r.Items.First(i => i.Kind == CompletionItemKind.Table).InsertText);
        }

        [Theory]
        [InlineData("CREATE INDEX IX_x ON dbo.Customers (|", "CustomerID,Name,Email")]
        [InlineData("CREATE INDEX IX_x ON dbo.Customers (Name, |", "CustomerID,Name,Email")]
        [InlineData("CREATE INDEX IX_x ON sales.SalesOrderHeader (CustomerID) INCLUDE (|", "OrderID,CustomerID,OrderDate,Total")]
        public void Create_index_lists_the_tables_columns(string sql, string expected)
        {
            Assert.Equal(expected, string.Join(",", Names(sql)));
        }

        [Fact]
        public void Backup_and_dbcc_start_new_statements()
        {
            Assert.Equal(ContextKind.Database, Complete("SELECT 1\nBACKUP DATABASE |").Context.Kind);
            Assert.Equal(ContextKind.Options, Complete("SELECT 1\nDBCC |").Context.Kind);
        }
    }
}
