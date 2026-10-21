using SqlSense.Core.Connections;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class ConnectionColorRulesTests
    {
        private const string Rules = @"
# comment
PROD-SQL01            = Red
PROD-SQL01/Reporting  = #E67E22
PROD-SQL01/tempdb     = none
*.test.contoso.com    = Green
localhost\*/*_Staging = #80112233
";

        private static uint? Color(string server, string database)
            => ConnectionColorRules.Parse(Rules).Find(server, database)?.Argb;

        [Fact]
        public void Server_rule_applies_to_every_database()
        {
            Assert.Equal(0xFFE81123u, Color("PROD-SQL01", "Sales"));
            Assert.Equal(0xFFE81123u, Color("prod-sql01", null));
        }

        [Fact]
        public void Database_rule_beats_server_rule_whatever_the_order()
        {
            Assert.Equal(0xFFE67E22u, Color("PROD-SQL01", "reporting"));
        }

        [Fact]
        public void None_matches_but_has_no_colour()
        {
            var rule = ConnectionColorRules.Parse(Rules).Find("PROD-SQL01", "tempdb");
            Assert.NotNull(rule);
            Assert.Null(rule.Argb);
        }

        [Fact]
        public void Wildcards_match_server_and_database()
        {
            Assert.Equal(0xFF16C60Cu, Color("db1.test.contoso.com", "Anything"));
            Assert.Equal(0x80112233u, Color(@"localhost\SQLEXPRESS", "Shop_Staging"));
            Assert.Null(Color(@"localhost\SQLEXPRESS", "Shop"));
            Assert.Null(Color("PROD-SQL02", "Sales"));
        }

        [Fact]
        public void Bad_lines_are_reported_and_skipped()
        {
            var rules = ConnectionColorRules.Parse("A = Red\nB = Chartreuse-ish\nno equals sign\n/db = Red\nC/ = Red");
            Assert.Single(rules.Rules);
            Assert.Equal(new[]
            {
                "line 2: unknown colour 'Chartreuse-ish'",
                "line 3: expected 'server[/database] = colour'",
                "line 4: missing server or database name",
                "line 5: missing server or database name",
            }, rules.Errors);
        }

        [Fact]
        public void Template_has_no_rules_or_errors()
        {
            var rules = ConnectionColorRules.Parse(ConnectionColorRules.Template);
            Assert.Empty(rules.Rules);
            Assert.Empty(rules.Errors);
        }

        [Fact]
        public void SetRule_rewrites_the_line_for_that_exact_target()
        {
            var text = "# c\r\nprod-sql01 = Red\r\nPROD-SQL01/Sales = Blue\r\n";
            Assert.Equal("# c\r\nPROD-SQL01 = #123456\r\nPROD-SQL01/Sales = Blue\r\n",
                ConnectionColorRules.SetRule(text, "PROD-SQL01", null, 0xFF123456));
            Assert.Equal("# c\r\nprod-sql01 = Red\r\nPROD-SQL01/sales = none\r\n",
                ConnectionColorRules.SetRule(text, "PROD-SQL01", "sales", null));
        }

        [Fact]
        public void SetRule_appends_a_new_target()
        {
            Assert.Equal("A = Red\nB/db = Orange\n", ConnectionColorRules.SetRule("A = Red", "B", "db", 0xFFF7630C));
            Assert.Equal("B = #80112233\n", ConnectionColorRules.SetRule("", "B", "", 0x80112233));
        }

        [Fact]
        public void RemoveRule_removes_only_that_exact_target()
        {
            var text = "A = Red\nA/db = Blue\nA* = Green\n";
            Assert.Equal("A/db = Blue\nA* = Green\n", ConnectionColorRules.RemoveRule(text, "a", null));
            Assert.Equal("A = Red\nA* = Green\n", ConnectionColorRules.RemoveRule(text, "A", "DB"));
        }

        [Fact]
        public void FindExact_ignores_wildcard_rules()
        {
            var rules = ConnectionColorRules.Parse("A* = Green\nA/db = Blue");
            Assert.Null(rules.FindExact("A", null));
            Assert.Equal(2, rules.FindExact("a", "DB").Line);
        }

        [Theory]
        [InlineData("#f00", 0xFFFF0000u)]
        [InlineData("#00FF00", 0xFF00FF00u)]
        [InlineData("#4000FF00", 0x4000FF00u)]
        [InlineData("orange", 0xFFF7630Cu)]
        public void Colours_parse(string text, uint expected)
        {
            Assert.True(ColorText.TryParse(text, out var argb));
            Assert.Equal(expected, argb);
        }
    }
}
