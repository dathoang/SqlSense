using System;
using System.Data.SqlTypes;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using SqlSense.Core.Export;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class ExportTests
    {
        private static ResultTable Table(ResultColumn[] columns, params object[][] rows) => new ResultTable(columns, rows);

        [Fact]
        public void Insert_script_uses_typed_literals()
        {
            var table = Table(
                new[]
                {
                    new ResultColumn("Id", "int"), new ResultColumn("Name", "nvarchar"), new ResultColumn("Code", "varchar"),
                    new ResultColumn("Born", "date"), new ResultColumn("Active", "bit"), new ResultColumn("Amount", "decimal"),
                    new ResultColumn("Photo", "varbinary"), new ResultColumn("Key", "uniqueidentifier"),
                },
                new object[] { 1, "O'Brien", "A1", new DateTime(1990, 5, 1), true, 12.50m, new byte[] { 0xAB, 0x01 }, new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff") },
                new object[] { 2, DBNull.Value, null, DBNull.Value, false, -3m, new byte[0], DBNull.Value });

            var sql = InsertScriptGenerator.Generate(table, "[dbo].[People]");

            Assert.Equal(
                "INSERT INTO [dbo].[People] ([Id], [Name], [Code], [Born], [Active], [Amount], [Photo], [Key])\r\n" +
                "VALUES\r\n" +
                "    (1, N'O''Brien', 'A1', '1990-05-01', 1, 12.50, 0xAB01, '6F9619FF-8B86-D011-B42D-00CF4FC964FF'),\r\n" +
                "    (2, NULL, NULL, NULL, 0, -3, 0x, NULL);\r\n",
                sql.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }

        [Fact]
        public void Insert_script_splits_every_1000_rows_and_skips_rowversion()
        {
            var rows = Enumerable.Range(1, 1001).Select(i => new object[] { i, new byte[] { 1 } }).ToArray();
            var sql = InsertScriptGenerator.Generate(Table(new[] { new ResultColumn("Id", "int"), new ResultColumn("RV", "timestamp") }, rows), "#t");

            Assert.Equal(2, CountOf(sql, "INSERT INTO #t ([Id])"));
            Assert.DoesNotContain("RV", sql);
            Assert.Contains("    (1000);", sql);
            Assert.EndsWith("    (1001);" + Environment.NewLine, sql);
        }

        [Fact]
        public void Insert_script_handles_string_cells_and_sql_types()
        {
            Assert.Equal("42", InsertScriptGenerator.Literal("42", "int"));
            Assert.Equal("1", InsertScriptGenerator.Literal("True", "bit"));
            Assert.Equal("N'abc'", InsertScriptGenerator.Literal("abc", "int"));
            Assert.Equal("0x0102", InsertScriptGenerator.Literal("0x0102", "varbinary"));
            Assert.Equal("N'x'", InsertScriptGenerator.Literal(new SqlString("x")));
            Assert.Equal("NULL", InsertScriptGenerator.Literal(SqlInt32.Null));
            Assert.Equal("7", InsertScriptGenerator.Literal(new SqlInt32(7)));
            Assert.Equal("'2024-01-02T03:04:05.123'", InsertScriptGenerator.Literal(new DateTime(2024, 1, 2, 3, 4, 5, 123), "datetime"));
            Assert.Equal("'2024-01-02T03:04:05.1230000'", InsertScriptGenerator.Literal(new DateTime(2024, 1, 2, 3, 4, 5, 123), "datetime2"));
            Assert.Equal("'03:04:05.0000000'", InsertScriptGenerator.Literal(new TimeSpan(3, 4, 5)));
            Assert.Equal("1.5", InsertScriptGenerator.Literal(1.5d, "float"));
        }

        [Fact]
        public void Insert_script_names_unnamed_columns()
        {
            var sql = InsertScriptGenerator.Generate(Table(new[] { new ResultColumn("(No column name)"), new ResultColumn("a]b") }, new object[] { 1, 2 }), "t");
            Assert.StartsWith("INSERT INTO t ([Column1], [a]]b])", sql);
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Customers c WHERE c.Id = 1", 0, "[dbo].[Customers]")]
        [InlineData("select Name from Customers", 0, "[dbo].[Customers]")]
        [InlineData("SELECT * FROM a; SELECT * FROM sales.Orders o JOIN b ON 1=1", 1, "[sales].[Orders]")]
        [InlineData("SELECT * FROM a; SELECT * FROM b", 5, "[dbo].[b]")]
        [InlineData("SELECT * INTO #x FROM a; SELECT * FROM #x", 0, "[#x]")]
        [InlineData("WITH c AS (SELECT * FROM dbo.T) SELECT * FROM c JOIN dbo.U u ON 1=1", 0, null)]
        [InlineData("SELECT * FROM (SELECT 1 AS x) d", 0, null)]
        [InlineData("SELECT 1", 0, null)]
        [InlineData("SELECT * FROM Db1..T", 0, "[Db1].[dbo].[T]")]
        public void Guesses_table_name_from_query(string script, int index, string expected)
        {
            Assert.Equal(expected, InsertScriptGenerator.GuessTableName(script, index));
        }

        [Fact]
        public void Xlsx_contains_typed_cells_and_header()
        {
            var table = Table(
                new[] { new ResultColumn("Id", "bigint"), new ResultColumn("Name", "nvarchar"), new ResultColumn("When", "datetime"), new ResultColumn("Ok", "bit"), new ResultColumn("Big", "bigint") },
                new object[] { 1L, " lead\u0001", new DateTime(2024, 1, 2, 12, 0, 0), true, 1234567890123456789L },
                new object[] { 2L, DBNull.Value, DBNull.Value, false, DBNull.Value });

            var ms = new MemoryStream();
            XlsxWriter.Write(ms, table, "Re:sult");
            ms.Position = 0;
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (var name in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml" })
                    XDocument.Load(zip.GetEntry(name).Open());

                var wb = XDocument.Load(zip.GetEntry("xl/workbook.xml").Open());
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                Assert.Equal("Re_sult", wb.Descendants(ns + "sheet").Single().Attribute("name").Value);

                var sheet = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml").Open());
                var rows = sheet.Descendants(ns + "row").ToList();
                Assert.Equal(3, rows.Count);
                Assert.Equal(new[] { "Id", "Name", "When", "Ok", "Big" }, rows[0].Elements(ns + "c").Select(c => c.Value));

                var cells = rows[1].Elements(ns + "c").ToList();
                Assert.Equal("1", cells[0].Element(ns + "v").Value);
                Assert.Null(cells[0].Attribute("t"));
                Assert.Equal("inlineStr", (string)cells[1].Attribute("t"));
                Assert.Equal(" lead", cells[1].Value);
                Assert.Equal(new DateTime(2024, 1, 2, 12, 0, 0).ToOADate(), double.Parse(cells[2].Element(ns + "v").Value, System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal("2", (string)cells[2].Attribute("s"));
                Assert.Equal("b", (string)cells[3].Attribute("t"));
                Assert.Equal("inlineStr", (string)cells[4].Attribute("t"));
                Assert.Equal("1234567890123456789", cells[4].Value);

                Assert.Equal(new[] { "A3", "D3" }, rows[2].Elements(ns + "c").Select(c => (string)c.Attribute("r")));
                Assert.Equal("A1:E3", (string)sheet.Descendants(ns + "autoFilter").Single().Attribute("ref"));
            }
        }

        [Theory]
        [InlineData(0, "A")]
        [InlineData(25, "Z")]
        [InlineData(26, "AA")]
        [InlineData(701, "ZZ")]
        [InlineData(702, "AAA")]
        public void Excel_column_names(int index, string expected) => Assert.Equal(expected, XlsxWriter.ColumnName(index));

        private static int CountOf(string text, string part)
        {
            var count = 0;
            for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + 1, StringComparison.Ordinal)) count++;
            return count;
        }
    }
}
