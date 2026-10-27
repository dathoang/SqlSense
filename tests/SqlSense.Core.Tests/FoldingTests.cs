using System.Linq;
using SqlSense.Core.Folding;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class FoldingTests
    {
        /// <summary>Each region as "level:header…last line", in document order.</summary>
        private static string[] Regions(string text, bool statements = true)
        {
            return FoldingAnalyzer.GetRegions(text, statements)
                .Select(r =>
                {
                    var body = text.Substring(r.Start, r.Length);
                    return r.Level + ":" + r.Header + "…" + body.Substring(body.LastIndexOf('\n') + 1).Trim();
                })
                .ToArray();
        }

        [Fact]
        public void Begin_end_blocks_nest_by_level()
        {
            var sql = "IF @a = 1\nBEGIN\n  WHILE @b < 3\n  BEGIN\n    SET @b += 1\n  END\nEND\n";
            Assert.Equal(new[] { "1:BEGIN…END", "2:BEGIN…END" }, Regions(sql));
        }

        [Fact]
        public void Try_catch_includes_its_keyword()
        {
            var sql = "BEGIN TRY\n  SELECT 1\nEND TRY\nBEGIN CATCH\n  THROW;\nEND CATCH";
            Assert.Equal(new[] { "1:BEGIN TRY…END TRY", "1:BEGIN CATCH…END CATCH" }, Regions(sql));
        }

        [Fact]
        public void Case_folds_and_its_end_does_not_close_begin()
        {
            var sql = "BEGIN\n  SELECT CASE WHEN 1 = 1 THEN 'a'\n    ELSE 'b' END\n  SELECT 2\nEND";
            Assert.Equal(new[]
            {
                "1:BEGIN…END",
                "2:SELECT CASE WHEN 1 = 1 THEN 'a'…ELSE 'b' END",
                "3:CASE WHEN 1 = 1 THEN 'a'…ELSE 'b' END",
            }, Regions(sql));
        }

        [Fact]
        public void Begin_tran_and_single_line_blocks_are_not_regions()
        {
            var sql = "BEGIN TRAN\nIF 1 = 1 BEGIN SELECT 1 END\nCOMMIT\nBEGIN TRANSACTION\nROLLBACK";
            Assert.Empty(Regions(sql));
        }

        [Fact]
        public void Region_comments_fold_and_contain_blocks()
        {
            var sql = "-- #region Setup\nIF 1 = 1\nBEGIN\n  SELECT 1\nEND\n--#endregion\nSELECT 2";
            Assert.Equal(new[] { "1:-- #region Setup…--#endregion", "2:BEGIN…END" }, Regions(sql));
            // Folding a -- comment must not swallow its line break and pull up the next line.
            var region = FoldingAnalyzer.GetRegions(sql).First();
            Assert.Equal("--#endregion", sql.Substring(region.End - 12, 12));
        }

        [Fact]
        public void Unclosed_block_is_dropped_at_go()
        {
            var sql = "BEGIN\n  SELECT 1\nGO\nBEGIN\n  SELECT 2\nEND";
            var region = FoldingAnalyzer.GetRegions(sql).Single(r => r.Kind == FoldKind.Block);
            Assert.Equal(sql.LastIndexOf("BEGIN"), region.Start);
        }

        [Fact]
        public void Multi_line_statements_fold_inside_blocks()
        {
            var sql = "SELECT a,\n  b\nFROM t;\nIF @x = 1\nBEGIN\n  INSERT INTO t (a)\n  VALUES (1)\n  UPDATE t SET a = 1 WHERE b = 2\nEND";
            Assert.Equal(new[] { "1:SELECT a,…FROM t;", "1:BEGIN…END", "2:INSERT INTO t (a)…VALUES (1)" }, Regions(sql));
        }

        [Fact]
        public void Statement_does_not_take_else_or_end()
        {
            var sql = "IF @x = 1\n  SELECT a\n  FROM t\nELSE\n  SELECT b\n  FROM u\nGO\nWITH c AS (\n  SELECT 1 AS n\n)\nSELECT n FROM c";
            Assert.Equal(new[] { "1:SELECT a…FROM t", "1:SELECT b…FROM u", "1:WITH c AS (…SELECT n FROM c", "2:(…)" }, Regions(sql));
        }

        [Fact]
        public void Statements_can_be_turned_off()
        {
            var sql = "SELECT a,\n  b\nFROM t\nWHERE a IN (\n  SELECT x FROM y)";
            Assert.Equal(new[] { "1:(…SELECT x FROM y)" }, Regions(sql, statements: false));
        }

        [Fact]
        public void Multi_line_parentheses_fold()
        {
            var sql = "CREATE TABLE dbo.T (\n  Id int NOT NULL,\n  Name nvarchar(50)\n)";
            Assert.Equal(new[] { "1:CREATE TABLE dbo.T (…)", "2:(…)" }, Regions(sql));
        }

        [Fact]
        public void Module_folds_to_end_of_batch()
        {
            var sql = "CREATE OR ALTER PROCEDURE dbo.P\n  @a int\nAS\nBEGIN\n  SELECT @a\nEND\nGO\nSELECT 1";
            Assert.Equal(new[] { "1:CREATE OR ALTER PROCEDURE dbo.P…END", "2:BEGIN…END" }, Regions(sql));
        }

        [Fact]
        public void Comments_fold_as_blocks_and_line_runs()
        {
            var sql = "/* Header\n   more */\n-- one\n-- two\n\n-- alone\nSELECT 1 -- trailing\n-- next";
            Assert.Equal(new[] { "1:/* Header…more */", "1:-- one…-- two" }, Regions(sql));
        }
    }
}
