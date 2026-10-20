using System.Linq;
using SqlSense.Core.Analysis;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class InsertPairsTests
    {
        /// <summary>"|" marks the caret; returns the highlighted texts in document order.</summary>
        private static string[] Highlights(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(caret, 1);
            return InsertPairs.Find(text, caret)
                .OrderBy(r => r.Start)
                .Select(r => text.Substring(r.Start, r.Length))
                .ToArray();
        }

        [Fact]
        public void Column_highlights_its_value()
        {
            Assert.Equal(new[] { "Email", "'a@b.c'" },
                Highlights("INSERT INTO dbo.Customers (Name, Em|ail) VALUES (N'Ann', 'a@b.c')"));
        }

        [Fact]
        public void Value_highlights_its_column()
        {
            Assert.Equal(new[] { "Name", "N'Ann'" },
                Highlights("INSERT INTO dbo.Customers (Name, Email)\nVALUES (N'A|nn', 'a@b.c')"));
        }

        [Fact]
        public void Column_highlights_value_in_every_row()
        {
            Assert.Equal(new[] { "Email", "'x'", "'y'" },
                Highlights("INSERT dbo.Customers (Name, |Email) VALUES ('a', 'x'), ('b', 'y')"));
        }

        [Fact]
        public void Value_in_second_row_highlights_only_that_value()
        {
            Assert.Equal(new[] { "Email", "'y'" },
                Highlights("INSERT dbo.Customers (Name, Email) VALUES ('a', 'x'), ('b', 'y|')"));
        }

        [Fact]
        public void Expressions_with_commas_inside_parentheses_are_one_item()
        {
            Assert.Equal(new[] { "Total", "ROUND(@x, 2)" },
                Highlights("INSERT INTO sales.SalesOrderHeader (CustomerID, Tot|al) VALUES (1, ROUND(@x, 2))"));
        }

        [Fact]
        public void Insert_select_pairs_with_select_list()
        {
            Assert.Equal(new[] { "Email", "c.Email AS e" },
                Highlights("INSERT INTO #t (Name, Email)\nSELECT TOP (10) c.Name, c.Ema|il AS e FROM dbo.Customers AS c WHERE c.Name > ''"));
        }

        [Fact]
        public void Caret_at_word_edge_counts()
        {
            Assert.Equal(new[] { "Name", "1" }, Highlights("INSERT INTO @t (Name|, Email) VALUES (1, 2)"));
        }

        [Fact]
        public void Works_with_table_hints_and_cte()
        {
            Assert.Equal(new[] { "b", "2" },
                Highlights("WITH x AS (SELECT 1 AS n)\nINSERT INTO dbo.T WITH (TABLOCK) (a, |b) VALUES (1, 2)"));
        }

        [Fact]
        public void Row_still_being_typed_is_paired()
        {
            Assert.Equal(new[] { "b", "2" }, Highlights("INSERT INTO dbo.T (a, |b) VALUES (1, 2"));
        }

        [Fact]
        public void Only_the_statement_under_the_caret_is_used()
        {
            Assert.Equal(new[] { "b", "4" },
                Highlights("INSERT INTO dbo.T (a, b) VALUES (1, 2);\nINSERT INTO dbo.U (a, |b) VALUES (3, 4);"));
        }

        [Theory]
        [InlineData("INSERT INTO dbo.T VALUES (1|, 2)")]                       // no column list
        [InlineData("INSERT INTO dbo.T (a, b) VALUES (1, 2) -- (a|, b)")]      // in a comment
        [InlineData("SELECT a, |b FROM dbo.T")]                               // not an INSERT
        [InlineData("INSERT INTO dbo.T (a, b) EXEC dbo.p 1, |2")]             // EXEC source
        [InlineData("INSERT INTO dbo.T (a, b) VALUES (1, 2) |")]              // between items
        public void Nothing_to_highlight(string sql)
        {
            Assert.Empty(Highlights(sql));
        }
    }
}
