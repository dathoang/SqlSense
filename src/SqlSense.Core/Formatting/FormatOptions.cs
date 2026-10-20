namespace SqlSense.Core.Formatting
{
    public enum KeywordCasing
    {
        Upper,
        Lower,
        AsIs,
    }

    public enum ParenthesisStyle
    {
        /// <summary>"(" ends the line, contents are indented below, ")" gets its own line.</summary>
        ExpandedSplit,

        /// <summary>"(" and ")" each get their own line at the statement's indent, contents indented below.</summary>
        ExpandedToStatement,
    }

    public sealed class FormatOptions
    {
        public int IndentSize { get; set; } = 4;
        public KeywordCasing KeywordCase { get; set; } = KeywordCasing.Upper;
        public KeywordCasing FunctionCase { get; set; } = KeywordCasing.Upper;
        public KeywordCasing DataTypeCase { get; set; } = KeywordCasing.Lower;

        /// <summary>Put commas at the start of the next line instead of the end of the line.</summary>
        public bool LeadingCommas { get; set; }

        /// <summary>One select-list / ORDER BY / GROUP BY / SET item per line.</summary>
        public bool OneItemPerLine { get; set; } = true;

        /// <summary>AND / OR in WHERE, ON and HAVING start a new line.</summary>
        public bool BreakOnAndOr { get; set; } = true;

        /// <summary>JOIN clauses are indented one level under FROM.</summary>
        public bool IndentJoins { get; set; } = true;

        /// <summary>Keep at most this many consecutive blank lines.</summary>
        public int MaxBlankLines { get; set; } = 1;

        /// <summary>SELECT/INSERT/UPDATE/DELETE/MERGE statements shorter than this stay on one line (0 = never).</summary>
        public int CollapseStatementsShorterThan { get; set; }

        /// <summary>Parenthesised subqueries shorter than this stay on one line (0 = never).</summary>
        public int CollapseSubqueriesShorterThan { get; set; }

        /// <summary>CASE expressions shorter than this stay on one line (0 = never).</summary>
        public int CollapseCaseShorterThan { get; set; }

        /// <summary>CREATE TABLE / table-variable column lists whose statement is shorter than this stay on one line (0 = never).</summary>
        public int CollapseDdlShorterThan { get; set; }

        /// <summary>Line up column aliases of a multi-line select list.</summary>
        public bool AlignAliases { get; set; }

        /// <summary>Line up end-of-line comments within a clause.</summary>
        public bool AlignComments { get; set; }

        /// <summary>Layout of subquery parentheses.</summary>
        public ParenthesisStyle SubqueryParenStyle { get; set; } = ParenthesisStyle.ExpandedSplit;

        /// <summary>Layout of CREATE TABLE / table-variable column-list parentheses.</summary>
        public ParenthesisStyle DdlParenStyle { get; set; } = ParenthesisStyle.ExpandedSplit;

        /// <summary>Write IN lists as "IN ( 1, 2 )".</summary>
        public bool SpaceInsideInLists { get; set; }

        public string NewLine { get; set; } = "\r\n";

        public FormatOptions Clone() => (FormatOptions)MemberwiseClone();
    }
}
