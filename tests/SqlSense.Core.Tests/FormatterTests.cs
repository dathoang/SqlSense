using SqlSense.Core.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace SqlSense.Core.Tests
{
    public class FormatterTests
    {
        private readonly ITestOutputHelper _out;
        private readonly SqlFormatter _f = new SqlFormatter(new FormatOptions { NewLine = "\n" });

        public FormatterTests(ITestOutputHelper output) => _out = output;

        private string Fmt(string sql)
        {
            var r = _f.Format(sql);
            _out.WriteLine(r.Text);
            Assert.True(r.Success, r.Error);
            return r.Text;
        }

        [Fact]
        public void Select_with_join_and_where()
        {
            var sql = "select c.CustomerID, c.Name, count(*) as n from dbo.Customers c inner join sales.Orders o on o.CustomerID = c.CustomerID and o.Total > 0 where c.Active = 1 and o.Date between '2024-01-01' and '2024-12-31' group by c.CustomerID, c.Name order by n desc";
            var expected =
                "SELECT c.CustomerID,\n" +
                "       c.Name,\n" +
                "       COUNT(*) AS n\n" +
                "FROM dbo.Customers c\n" +
                "    INNER JOIN sales.Orders o\n" +
                "        ON o.CustomerID = c.CustomerID\n" +
                "           AND o.Total > 0\n" +
                "WHERE c.Active = 1\n" +
                "      AND o.Date BETWEEN '2024-01-01' AND '2024-12-31'\n" +
                "GROUP BY c.CustomerID,\n" +
                "         c.Name\n" +
                "ORDER BY n DESC";
            Assert.Equal(expected, Fmt(sql));
        }

        [Fact]
        public void Subquery_is_indented()
        {
            var sql = "select * from dbo.A where id in (select id from dbo.B where x = 1)";
            var expected =
                "SELECT *\n" +
                "FROM dbo.A\n" +
                "WHERE id IN (\n" +
                "    SELECT id\n" +
                "    FROM dbo.B\n" +
                "    WHERE x = 1\n" +
                ")";
            Assert.Equal(expected, Fmt(sql));
        }

        [Fact]
        public void Case_expression()
        {
            var sql = "select case when a = 1 then 'one' when a = 2 then 'two' else 'many' end as label from t";
            var expected =
                "SELECT CASE\n" +
                "           WHEN a = 1 THEN 'one'\n" +
                "           WHEN a = 2 THEN 'two'\n" +
                "           ELSE 'many'\n" +
                "       END AS label\n" +
                "FROM t";
            Assert.Equal(expected, Fmt(sql));
        }

        [Fact]
        public void Blocks_if_else_and_try_catch()
        {
            var sql = "begin try begin tran; update dbo.t set a = 1, b = 2 where id = @id; if @@rowcount = 0 throw 50000, 'missing', 1; else print 'ok'; commit; end try begin catch rollback; throw; end catch";
            var expected =
                "BEGIN TRY\n" +
                "    BEGIN TRAN;\n" +
                "    UPDATE dbo.t\n" +
                "    SET a = 1,\n" +
                "        b = 2\n" +
                "    WHERE id = @id;\n" +
                "    IF @@rowcount = 0\n" +
                "        THROW 50000, 'missing', 1;\n" +
                "    ELSE\n" +
                "        PRINT 'ok';\n" +
                "    COMMIT;\n" +
                "END TRY\n" +
                "BEGIN CATCH\n" +
                "    ROLLBACK;\n" +
                "    THROW;\n" +
                "END CATCH";
            Assert.Equal(expected, Fmt(sql));
        }

        [Fact]
        public void Comments_are_preserved()
        {
            var sql = "-- header\nselect a, -- first\n b /* second */ from t -- tail\nwhere x = 1";
            var result = Fmt(sql);
            Assert.Contains("-- header", result);
            Assert.Contains("a, -- first", result);
            Assert.Contains("/* second */", result);
            Assert.Contains("-- tail", result);
            Assert.Null(SqlFormatter.Verify(sql, result));
        }

        [Fact]
        public void Insert_values_and_create_table()
        {
            var sql = "create table dbo.T (Id int not null primary key, Name nvarchar(50) null); insert into dbo.T (Id, Name) values (1, N'a'), (2, N'b');";
            var expected =
                "CREATE TABLE dbo.T (\n" +
                "    Id int NOT NULL PRIMARY KEY,\n" +
                "    Name nvarchar(50) NULL\n" +
                ");\n" +
                "INSERT INTO dbo.T (Id, Name)\n" +
                "VALUES (1, N'a'),\n" +
                "       (2, N'b');";
            Assert.Equal(expected, Fmt(sql));
        }

        [Fact]
        public void Cte_and_declare()
        {
            var sql = "declare @from date = getdate(), @n int = -1; with x as (select a from t) select a from x";
            var expected =
                "DECLARE @from date = GETDATE(),\n" +
                "        @n int = -1;\n" +
                "WITH x AS (\n" +
                "    SELECT a\n" +
                "    FROM t\n" +
                ")\n" +
                "SELECT a\n" +
                "FROM x";
            Assert.Equal(expected, Fmt(sql));
        }

        [Fact]
        public void Go_separates_batches_and_blank_lines_are_kept()
        {
            var sql = "select 1\ngo\n\nselect 2\n\n\n\nselect 3";
            Assert.Equal("SELECT 1\nGO\n\nSELECT 2\n\nSELECT 3", Fmt(sql));
        }

        [Theory]
        [InlineData("select a.b, [weird name], @v, N'x' + 'y' from [dbo].[t] with (nolock) where a.b <> 1 and c >= 2 or d != 3")]
        [InlineData("merge dbo.t as tgt using dbo.s as src on tgt.id = src.id when matched then update set tgt.v = src.v when not matched then insert (id, v) values (src.id, src.v);")]
        [InlineData("select row_number() over (partition by a order by b desc) rn, left(name, 3), cast(x as decimal(10, 2)) from t order by 1 offset 10 rows fetch next 5 rows only")]
        [InlineData("create or alter procedure dbo.p @a int, @b varchar(10) = null output as begin set nocount on; select @a; end")]
        [InlineData("while @i < 10 begin set @i += 1; end")]
        [InlineData("select * from a cross apply (select top (1) * from b where b.id = a.id order by b.d desc) x outer apply dbo.fn(a.id) f")]
        [InlineData("select x from t where exists (select 1 from u where u.id = t.id) and not exists (select 1 from v)")]
        [InlineData("SELECT 1 UNION ALL SELECT 2 EXCEPT SELECT 3")]
        [InlineData("select -1, - 2, 3 - 4, a*-b from t")]
        [InlineData("delete from t where id = 1; delete t from t join u on u.id = t.id")]
        [InlineData("if exists (select 1 from t) begin select 1 end else if 1 = 2 select 2 else begin select 3 end")]
        [InlineData("select a, b from t where c in (1, 2, 3) and d not in (select x from u)")]
        [InlineData("select case when a = 1 then 'one' else 'many' end as label, b bb, top1 from t -- c1\nwhere x = 1 -- c2")]
        [InlineData("create table dbo.T (Id int not null, Name nvarchar(50) null); declare @t table (a int)")]
        [InlineData("select top 10 a, top (5) b from t; select distinct a as x, (select max(b) from u where u.id = t.id) as m from t")]
        [InlineData("if @a = 1 select a from t else begin update t set a = 1 where b = 2; delete from t where c = 3 end")]
        [InlineData("with x as (select a from t) insert into u (a) select a from x")]
        public void Formatting_never_changes_tokens_and_is_idempotent(string sql)
        {
            foreach (var f in new[] { _f, new SqlFormatter(PromptLike()) })
            {
                var once = f.Format(sql);
                _out.WriteLine(once.Text);
                Assert.True(once.Success, once.Error);
                Assert.Null(SqlFormatter.Verify(sql, once.Text));
                Assert.Equal(once.Text, f.Format(once.Text).Text);
            }
        }

        private static FormatOptions PromptLike() => new FormatOptions
        {
            NewLine = "\n",
            KeywordCase = KeywordCasing.Lower,
            FunctionCase = KeywordCasing.Lower,
            LeadingCommas = true,
            AlignAliases = true,
            AlignComments = true,
            CollapseStatementsShorterThan = 75,
            CollapseSubqueriesShorterThan = 75,
            CollapseCaseShorterThan = 75,
            CollapseDdlShorterThan = 75,
            DdlParenStyle = ParenthesisStyle.ExpandedToStatement,
            SpaceInsideInLists = true,
        };

        private static string Fmt(FormatOptions o, string sql)
        {
            var r = new SqlFormatter(o).Format(sql);
            Assert.True(r.Success, r.Error);
            return r.Text;
        }

        [Fact]
        public void Short_statements_collapse_to_one_line()
        {
            var o = new FormatOptions { NewLine = "\n", CollapseStatementsShorterThan = 75 };
            Assert.Equal("SELECT a, b FROM dbo.T t INNER JOIN u ON u.id = t.id WHERE a = 1;\nSELECT 1",
                Fmt(o, "select a, b from dbo.T t inner join u on u.id = t.id where a = 1; select 1"));
            Assert.Equal("SELECT a\nFROM dbo.SomeRatherLongTableName\nWHERE SomeColumn = 1\n      AND OtherColumn = 2",
                Fmt(o, "select a from dbo.SomeRatherLongTableName where SomeColumn = 1 and OtherColumn = 2"));
        }

        [Fact]
        public void Leading_commas_are_not_used_inside_a_collapsed_statement()
        {
            var o = new FormatOptions { NewLine = "\n", CollapseStatementsShorterThan = 75, LeadingCommas = true };
            Assert.Equal("SELECT a, b FROM t", Fmt(o, "select a, b from t"));
        }

        [Fact]
        public void Short_subqueries_collapse()
        {
            var o = new FormatOptions { NewLine = "\n", CollapseSubqueriesShorterThan = 40 };
            Assert.Equal("SELECT *\nFROM dbo.A\nWHERE id IN (SELECT id FROM dbo.B WHERE x = 1)",
                Fmt(o, "select * from dbo.A where id in (select id from dbo.B where x = 1)"));
        }

        [Fact]
        public void Short_case_collapses()
        {
            // The statement is too long to collapse; its CASE still is (ELSE inside CASE doesn't end the statement).
            var o = new FormatOptions { NewLine = "\n", CollapseCaseShorterThan = 75, CollapseStatementsShorterThan = 40 };
            Assert.Equal("SELECT CASE WHEN a = 1 THEN 'one' ELSE 'many' END AS label\nFROM t",
                Fmt(o, "select case when a = 1 then 'one' else 'many' end as label from t"));
        }

        [Fact]
        public void Statement_with_a_comment_is_not_collapsed()
        {
            var o = new FormatOptions { NewLine = "\n", CollapseStatementsShorterThan = 75 };
            Assert.Equal("SELECT a, -- note\n       b\nFROM t", Fmt(o, "select a, -- note\n b from t"));
        }

        [Fact]
        public void Aliases_are_aligned()
        {
            var o = new FormatOptions { NewLine = "\n", AlignAliases = true };
            var expected =
                "SELECT c.CustomerID AS id,\n" +
                "       c.Name       name,\n" +
                "       COUNT(*)     AS n,\n" +
                "       TOP1\n" +
                "FROM t";
            Assert.Equal(expected, Fmt(o, "select c.CustomerID as id, c.Name name, count(*) as n, TOP1 from t"));
        }

        [Fact]
        public void Top_is_not_mistaken_for_an_alias()
        {
            var o = new FormatOptions { NewLine = "\n", AlignAliases = true };
            Assert.Equal("SELECT TOP 10 a,\n       b AS bb\nFROM t", Fmt(o, "select top 10 a, b as bb from t"));
        }

        [Fact]
        public void Trailing_comments_are_aligned()
        {
            var o = new FormatOptions { NewLine = "\n", AlignComments = true };
            Assert.Equal("SELECT a,        -- first\n       bbbbbbbb, -- second\n       c\nFROM t",
                Fmt(o, "select a, -- first\n bbbbbbbb, -- second\n c from t"));
        }

        [Fact]
        public void Ddl_parentheses_expanded_to_statement()
        {
            var o = new FormatOptions { NewLine = "\n", DdlParenStyle = ParenthesisStyle.ExpandedToStatement };
            Assert.Equal("CREATE TABLE dbo.T\n(\n    Id int NOT NULL,\n    Name nvarchar(50) NULL\n);",
                Fmt(o, "create table dbo.T (Id int not null, Name nvarchar(50) null);"));
        }

        [Fact]
        public void Short_create_table_collapses()
        {
            var o = new FormatOptions { NewLine = "\n", CollapseDdlShorterThan = 75, DdlParenStyle = ParenthesisStyle.ExpandedToStatement };
            Assert.Equal("CREATE TABLE dbo.T (Id int NOT NULL, Name nvarchar(50) NULL);",
                Fmt(o, "create table dbo.T (Id int not null, Name nvarchar(50) null);"));
        }

        [Fact]
        public void Subquery_parentheses_expanded_to_statement()
        {
            var o = new FormatOptions { NewLine = "\n", SubqueryParenStyle = ParenthesisStyle.ExpandedToStatement };
            Assert.Equal("SELECT *\nFROM dbo.A\nWHERE id IN\n(\n    SELECT id\n    FROM dbo.B\n)",
                Fmt(o, "select * from dbo.A where id in (select id from dbo.B)"));
        }

        [Fact]
        public void In_lists_get_inner_spaces()
        {
            var o = new FormatOptions { NewLine = "\n", SpaceInsideInLists = true };
            Assert.Equal("SELECT a\nFROM t\nWHERE a IN ( 1, 2, 3 )\n      AND f(b) = 1", Fmt(o, "select a from t where a in (1,2,3) and f(b) = 1"));
        }

        [Fact]
        public void Unterminated_string_is_rejected_without_changes()
        {
            var r = _f.Format("select 'abc from t");
            Assert.False(r.Success);
            Assert.Equal("select 'abc from t", r.Text);
        }

        [Fact]
        public void Base_indent_is_applied_to_selection()
        {
            var r = _f.Format("select a from t where b = 1", "    ");
            Assert.Equal("    SELECT a\n    FROM t\n    WHERE b = 1", r.Text);
        }

        [Fact]
        public void Leading_commas_option()
        {
            var f = new SqlFormatter(new FormatOptions { NewLine = "\n", LeadingCommas = true });
            Assert.Equal("SELECT a\n     , b\nFROM t", f.Format("select a, b from t").Text);
        }
    }
}
