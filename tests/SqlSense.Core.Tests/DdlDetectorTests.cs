using SqlSense.Core.Analysis;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class DdlDetectorTests
    {
        [Theory]
        [InlineData("CREATE TABLE dbo.T (a int)", "Shop")]
        [InlineData("create or alter procedure dbo.p as select 1", "Shop")]
        [InlineData("ALTER VIEW v AS SELECT 1 AS a", "Shop")]
        [InlineData("DROP TABLE IF EXISTS dbo.T", "Shop")]
        [InlineData("CREATE UNIQUE NONCLUSTERED INDEX IX ON dbo.T (a)", "Shop")]
        [InlineData("CREATE INDEX IX ON Archive.dbo.T (a)", "Archive")]
        [InlineData("CREATE TABLE Archive.hist.T (a int)", "Archive")]
        [InlineData("CREATE TABLE Archive..T (a int)", "Archive")]
        [InlineData("USE Archive\nGO\nCREATE TABLE T (a int)", "Archive")]
        [InlineData("USE Archive; ALTER TABLE T ADD b int; USE Shop; DROP VIEW v;", "Archive,Shop")]
        [InlineData("SELECT * INTO dbo.Backup_T FROM dbo.T", "Shop")]
        [InlineData("EXEC sp_rename 'dbo.T.a', 'b', 'COLUMN'", "Shop")]
        [InlineData("CREATE USER bob FOR LOGIN bob", "Shop")]
        [InlineData("ALTER ROLE r ADD MEMBER bob", "Shop")]
        [InlineData("CREATE SCHEMA audit", "Shop")]
        [InlineData("ALTER DATABASE Archive SET RECOVERY SIMPLE", "Shop")]
        [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL DROP TABLE dbo.T", "Shop")]
        [InlineData("SELECT 1\nGO\nCREATE OR ALTER FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1 END", "Shop")]
        public void Finds_changed_databases(string script, string expected)
        {
            Assert.Equal(expected, string.Join(",", DdlDetector.ChangedDatabases(script, "Shop")));
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.T")]
        [InlineData("INSERT INTO dbo.T (a) VALUES (1); UPDATE dbo.T SET a = 2; DELETE FROM dbo.T")]
        [InlineData("CREATE TABLE #t (a int); DROP TABLE #t")]
        [InlineData("SELECT * INTO #t FROM dbo.T")]
        [InlineData("DECLARE @t TABLE (a int)")]
        [InlineData("CREATE INDEX IX ON #t (a)")]
        [InlineData("-- CREATE TABLE dbo.T (a int)\nSELECT 'DROP TABLE x'")]
        [InlineData("EXEC dbo.usp_GetOrders 1")]
        [InlineData("CREATE EVENT SESSION s ON SERVER ADD EVENT sqlserver.sql_batch_completed")]
        public void Ignores_scripts_without_metadata_changes(string script)
        {
            Assert.Empty(DdlDetector.ChangedDatabases(script, "Shop"));
        }
    }
}
