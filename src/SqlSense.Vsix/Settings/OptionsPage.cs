using System.ComponentModel;
using Microsoft.VisualStudio.Shell;
using SqlSense.Core.Completion;
using SqlSense.Core.Formatting;

namespace SqlSense.Settings
{
    /// <summary>Tools › Options › SqlSense › General.</summary>
    [DesignerCategory("code")]
    public sealed class OptionsPage : DialogPage
    {
        private const string General = "1. General";
        private const string Suggestions = "2. Suggestions";
        private const string Formatting = "3. Formatting";

        [Category(General), DisplayName("Enabled"), Description("Turn SqlSense on or off in SQL query windows.")]
        public bool Enabled { get; set; } = true;

        [Category(General), DisplayName("Highlight INSERT columns and values"),
         Description("With the caret on a column of an INSERT column list, highlight its values (and on a value, its column). The colour is 'SqlSense INSERT column/value' in Fonts and Colors.")]
        public bool HighlightInsertPairs { get; set; } = true;

        [Category(General), DisplayName("Snippets folder"),
         Description("Folder of user snippets: each <shortcut>.sql file becomes a snippet. An optional first line '-- text' is its description; $CURSOR$ marks the caret.")]
        public string SnippetsFolder { get; set; } = SqlSenseSettings.DefaultSnippetsFolder;

        [Category(General), DisplayName("Schema cache (minutes)"), Description("How long database metadata is reused before it is reloaded.")]
        public int SchemaCacheMinutes { get; set; } = 30;

        [Category(Suggestions), DisplayName("Show suggestions while typing"), Description("Open the list automatically as you type. Ctrl+Space always opens it.")]
        public bool AutoTrigger { get; set; } = true;

        [Category(Suggestions), DisplayName("Add table aliases"), Description("Picking a table in FROM/JOIN appends a generated alias (Customers → c).")]
        public bool AutoAlias { get; set; } = true;

        [Category(Suggestions), DisplayName("Use AS before aliases"), Description("Write 'Customers AS c' instead of 'Customers c'.")]
        public bool AliasWithAs { get; set; } = true;

        [Category(Suggestions), DisplayName("Always qualify with schema"), Description("Insert dbo.Customers rather than Customers.")]
        public bool AlwaysQualifySchema { get; set; } = true;

        [Category(Suggestions), DisplayName("INSERT INTO adds column list"), Description("Picking a table after INSERT INTO writes the column list and a VALUES template.")]
        public bool InsertGeneratesColumns { get; set; } = true;

        [Category(Suggestions), DisplayName("EXEC adds parameters"), Description("Picking a procedure after EXEC writes its parameters.")]
        public bool ExecGeneratesParameters { get; set; } = true;

        [Category(Suggestions), DisplayName("Suggest JOIN conditions"), Description("After JOIN … ON, offer conditions from foreign keys.")]
        public bool SuggestJoinConditions { get; set; } = true;

        [Category(Suggestions), DisplayName("Close brackets and quotes"), Description("Typing ( [ or ' inserts the closing character.")]
        public bool AutoCloseBrackets { get; set; } = true;

        [Category(Suggestions), DisplayName("Tab expands snippets"), Description("Typing a snippet shortcut (ssf, st100, …) and pressing Tab expands it.")]
        public bool TabExpandsSnippets { get; set; } = true;

        [Category(Suggestions), DisplayName("Tab expands SELECT *"), Description("Pressing Tab right after * in a select list replaces it with the column list.")]
        public bool TabExpandsWildcard { get; set; } = true;

        [Category(Suggestions), DisplayName("Include system objects"), Description("Suggest sys.* and INFORMATION_SCHEMA views.")]
        public bool IncludeSystemObjects { get; set; } = true;

        [Category(Suggestions), DisplayName("Upper-case keywords"), Description("Keywords inserted by suggestions are upper case.")]
        public bool UppercaseKeywords { get; set; } = true;

        [Category(Formatting), DisplayName("Indent size")]
        public int IndentSize { get; set; } = 4;

        [Category(Formatting), DisplayName("Keyword case")]
        public KeywordCasing KeywordCase { get; set; } = KeywordCasing.Upper;

        [Category(Formatting), DisplayName("Function case")]
        public KeywordCasing FunctionCase { get; set; } = KeywordCasing.Upper;

        [Category(Formatting), DisplayName("Data type case")]
        public KeywordCasing DataTypeCase { get; set; } = KeywordCasing.Lower;

        [Category(Formatting), DisplayName("Leading commas"), Description("Put commas at the start of lines.")]
        public bool LeadingCommas { get; set; }

        [Category(Formatting), DisplayName("One list item per line"), Description("Select lists, GROUP BY, ORDER BY and SET get one item per line.")]
        public bool OneItemPerLine { get; set; } = true;

        [Category(Formatting), DisplayName("New line before AND / OR")]
        public bool BreakOnAndOr { get; set; } = true;

        [Category(Formatting), DisplayName("Indent JOINs under FROM")]
        public bool IndentJoins { get; set; } = true;

        public override void LoadSettingsFromStorage()
        {
            base.LoadSettingsFromStorage();
            Publish();
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);
            if (e.ApplyBehavior == ApplyKind.Apply) Publish();
        }

        public void Publish()
        {
            SqlSenseSettings.Update(new SqlSenseSettings
            {
                Enabled = Enabled,
                AutoTrigger = AutoTrigger,
                AutoCloseBrackets = AutoCloseBrackets,
                TabExpandsSnippets = TabExpandsSnippets,
                TabExpandsWildcard = TabExpandsWildcard,
                HighlightInsertPairs = HighlightInsertPairs,
                IncludeSystemObjects = IncludeSystemObjects,
                SchemaCacheMinutes = SchemaCacheMinutes < 1 ? 1 : SchemaCacheMinutes,
                SnippetsFolder = string.IsNullOrWhiteSpace(SnippetsFolder) ? SqlSenseSettings.DefaultSnippetsFolder : SnippetsFolder,
                Completion = new CompletionOptions
                {
                    AutoAlias = AutoAlias,
                    AliasWithAs = AliasWithAs,
                    AlwaysQualifySchema = AlwaysQualifySchema,
                    InsertGeneratesColumns = InsertGeneratesColumns,
                    ExecGeneratesParameters = ExecGeneratesParameters,
                    SuggestJoinConditions = SuggestJoinConditions,
                    UppercaseKeywords = UppercaseKeywords,
                },
                Format = new FormatOptions
                {
                    IndentSize = IndentSize < 1 ? 4 : IndentSize,
                    KeywordCase = KeywordCase,
                    FunctionCase = FunctionCase,
                    DataTypeCase = DataTypeCase,
                    LeadingCommas = LeadingCommas,
                    OneItemPerLine = OneItemPerLine,
                    BreakOnAndOr = BreakOnAndOr,
                    IndentJoins = IndentJoins,
                },
            });
        }
    }
}
