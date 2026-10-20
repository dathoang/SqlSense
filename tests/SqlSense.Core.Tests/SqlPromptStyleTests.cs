using System;
using SqlSense.Core.Formatting;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class SqlPromptStyleTests
    {
        private const string Style = @"{
  ""metadata"": { ""id"": ""20fcbc8f-f493-4ff3-b209-c777c22321bf"", ""name"": ""Custom style"" },
  ""whitespace"": { ""newLines"": { ""preserveExistingEmptyLinesAfterBatchSeparator"": false } },
  ""lists"": { ""alignItemsAcrossClauses"": false, ""alignAliases"": true, ""alignComments"": true, ""placeCommasBeforeItems"": true },
  ""parentheses"": { ""parenthesisStyle"": ""expandedSplit"", ""indentParenthesesContents"": true },
  ""casing"": { ""reservedKeywords"": ""lowercase"", ""builtInFunctions"": ""lowercase"", ""builtInDataTypes"": ""lowercase"" },
  ""dml"": { ""collapseShortStatements"": true, ""collapseStatementsShorterThan"": 75, ""collapseShortSubqueries"": true, ""collapseSubqueriesShorterThan"": 60 },
  ""ddl"": { ""parenthesisStyle"": ""expandedToStatement"", ""collapseShortStatements"": true, ""collapseStatementsShorterThan"": 70 },
  ""joinStatements"": { ""join"": { ""keywordAlignment"": ""indented"" } },
  ""caseExpressions"": { ""collapseShortCaseExpressions"": true, ""collapseCaseExpressionsShorterThan"": 50 },
  ""operators"": { ""andOr"": { ""alignment"": ""toFirstListItem"" }, ""in"": { ""placeFirstValueOnNewLine"": ""never"", ""addSpaceAroundInContents"": true } }
}";

        [Fact]
        public void Maps_supported_settings()
        {
            var r = SqlPromptStyle.Import(Style, new FormatOptions());
            var o = r.Options;
            Assert.Equal(KeywordCasing.Lower, o.KeywordCase);
            Assert.Equal(KeywordCasing.Lower, o.FunctionCase);
            Assert.Equal(KeywordCasing.Lower, o.DataTypeCase);
            Assert.True(o.LeadingCommas);
            Assert.True(o.AlignAliases);
            Assert.True(o.AlignComments);
            Assert.Equal(75, o.CollapseStatementsShorterThan);
            Assert.Equal(60, o.CollapseSubqueriesShorterThan);
            Assert.Equal(50, o.CollapseCaseShorterThan);
            Assert.Equal(70, o.CollapseDdlShorterThan);
            Assert.Equal(ParenthesisStyle.ExpandedSplit, o.SubqueryParenStyle);
            Assert.Equal(ParenthesisStyle.ExpandedToStatement, o.DdlParenStyle);
            Assert.True(o.IndentJoins);
            Assert.True(o.SpaceInsideInLists);
            Assert.False(o.KeepBlankLinesAfterGo);
            Assert.Empty(r.Ignored);
        }

        [Fact]
        public void Maps_layout_settings()
        {
            var r = SqlPromptStyle.Import(@"{
  ""whitespace"": { ""newLines"": { ""alignMultilineCommentsMatchingPatterns"": true } },
  ""casing"": { ""globalVariables"": ""lowercase"", ""useObjectDefinitionCase"": true },
  ""dml"": { ""addNewLineAfterDistinctAndTopClauses"": true },
  ""ddl"": { ""placeConstraintsOnNewLines"": true, ""placeConstraintColumnsOnNewLines"": ""ifLongerOrMultipleColumns"" },
  ""caseExpressions"": { ""placeFirstWhenOnNewLine"": ""ifInputExpression"" },
  ""variables"": { ""placeEqualsSignOnNewLine"": true }
}", new FormatOptions());
            var o = r.Options;
            Assert.True(o.AlignMultilineComments);
            Assert.Equal(KeywordCasing.Lower, o.GlobalVariableCase);
            Assert.True(o.UseObjectDefinitionCase);
            Assert.True(o.BreakAfterDistinctTop);
            Assert.True(o.DdlConstraintsOnNewLines);
            Assert.Equal(ListWrap.IfMultipleItems, o.ConstraintColumnsOnNewLines);
            Assert.Equal(CaseWhenPlacement.IfInputExpression, o.FirstWhenOnNewLine);
            Assert.Equal(new[] { "variables.placeEqualsSignOnNewLine = true" }, r.Ignored);
        }

        [Fact]
        public void Unmentioned_settings_keep_their_value_and_input_is_not_modified()
        {
            var current = new FormatOptions { IndentSize = 2, BreakOnAndOr = false };
            var r = SqlPromptStyle.Import(@"{ ""casing"": { ""reservedKeywords"": ""uppercase"" } }", current);
            Assert.Equal(2, r.Options.IndentSize);
            Assert.False(r.Options.BreakOnAndOr);
            Assert.Equal(KeywordCasing.Upper, r.Options.KeywordCase);
            Assert.NotSame(current, r.Options);
        }

        [Fact]
        public void Disabled_collapse_sets_zero()
        {
            var r = SqlPromptStyle.Import(@"{ ""dml"": { ""collapseShortStatements"": false, ""collapseStatementsShorterThan"": 75 } }",
                new FormatOptions { CollapseStatementsShorterThan = 40 });
            Assert.Equal(0, r.Options.CollapseStatementsShorterThan);
        }

        [Fact]
        public void Invalid_json_is_rejected()
        {
            Assert.Throws<FormatException>(() => SqlPromptStyle.Import("{ \"casing\": ", new FormatOptions()));
            Assert.Throws<FormatException>(() => SqlPromptStyle.Import("[1, 2]", new FormatOptions()));
        }
    }
}
