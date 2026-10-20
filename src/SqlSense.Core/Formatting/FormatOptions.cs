namespace SqlSense.Core.Formatting
{
    public enum KeywordCasing
    {
        Upper,
        Lower,
        AsIs,
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

        public string NewLine { get; set; } = "\r\n";
    }
}
