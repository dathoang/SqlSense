namespace SqlSense.Core.Completion
{
    public sealed class CompletionOptions
    {
        /// <summary>Append a generated alias when a table is picked in FROM/JOIN. Off by default.</summary>
        public bool AutoAlias { get; set; }

        /// <summary>Write "Table AS t" instead of "Table t". Off by default.</summary>
        public bool AliasWithAs { get; set; }

        /// <summary>Always prefix objects with their schema, even the default one.</summary>
        public bool AlwaysQualifySchema { get; set; } = true;

        /// <summary>Picking a table after INSERT INTO adds the column list and a VALUES template.</summary>
        public bool InsertGeneratesColumns { get; set; } = true;

        /// <summary>Picking a procedure after EXEC adds its parameters.</summary>
        public bool ExecGeneratesParameters { get; set; } = true;

        /// <summary>Picking an object after ALTER PROCEDURE/FUNCTION/VIEW/TRIGGER scripts its definition.</summary>
        public bool AlterScriptsDefinition { get; set; } = true;

        /// <summary>Offer foreign-key based conditions after JOIN … ON.</summary>
        public bool SuggestJoinConditions { get; set; } = true;

        /// <summary>Offer conditions on same-named columns after JOIN … ON (also for CTEs and temp tables).</summary>
        public bool SuggestSameNameJoins { get; set; } = true;

        /// <summary>Recently picked suggestions move up the list (needs <see cref="CompletionEngine.History"/>).</summary>
        public bool RankRecentlyUsed { get; set; } = true;

        /// <summary>Keyword case used by completion items.</summary>
        public bool UppercaseKeywords { get; set; } = true;
    }
}
