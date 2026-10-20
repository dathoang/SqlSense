namespace SqlSense.Core.Completion
{
    public sealed class CompletionOptions
    {
        /// <summary>Append a generated alias when a table is picked in FROM/JOIN.</summary>
        public bool AutoAlias { get; set; } = true;

        /// <summary>Write "Table AS t" instead of "Table t".</summary>
        public bool AliasWithAs { get; set; } = true;

        /// <summary>Always prefix objects with their schema, even the default one.</summary>
        public bool AlwaysQualifySchema { get; set; } = true;

        /// <summary>Picking a table after INSERT INTO adds the column list and a VALUES template.</summary>
        public bool InsertGeneratesColumns { get; set; } = true;

        /// <summary>Picking a procedure after EXEC adds its parameters.</summary>
        public bool ExecGeneratesParameters { get; set; } = true;

        /// <summary>Offer foreign-key based conditions after JOIN … ON.</summary>
        public bool SuggestJoinConditions { get; set; } = true;

        /// <summary>Keyword case used by completion items.</summary>
        public bool UppercaseKeywords { get; set; } = true;
    }
}
