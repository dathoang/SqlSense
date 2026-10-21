using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using SqlSense.Core.Completion;
using SqlSense.Core.Formatting;
using SqlSense.Diagnostics;
using SettingsScope = Microsoft.VisualStudio.Settings.SettingsScope;
using WritableSettingsStore = Microsoft.VisualStudio.Settings.WritableSettingsStore;

namespace SqlSense.Settings
{
    /// <summary>
    /// Tools › Options › SqlSense, backed by Unified Settings (Settings/registration.json).
    /// Reads every value into <see cref="SqlSenseSettings.Current"/> and re-reads on change.
    /// </summary>
    internal sealed class SettingsStore : IDisposable
    {
        private const string General = "sqlSense.general.";
        private const string Suggestions = "sqlSense.suggestions.";
        private const string Formatting = "sqlSense.formatting.";
        private const string Collapse = "sqlSense.formatting.collapse.";
        private const string ConnectionColors = "sqlSense.connectionColors.";

        private const string SsmsIntelliSense = "languages.sql.intelliSense.enableIntellisense";

        // Remembers that SqlSense (not the user) turned SSMS IntelliSense off, so only that gets undone.
        private const string StateCollection = "SqlSense";
        private const string TurnedOffFlag = "TurnedOffSsmsIntelliSense";

        private readonly ISettingsManager _manager;
        private readonly ISettingsReader _reader;
        private readonly WritableSettingsStore _state;
        private readonly object _syncLock = new object();
        private readonly IDisposable _subscription;

        private SettingsStore(ISettingsManager manager, WritableSettingsStore state)
        {
            _manager = manager;
            _reader = manager.GetReader();
            _state = state;
            _subscription = _reader.SubscribeToChanges(_ => Publish(), "sqlSense.*");
        }

        /// <summary>Connects to Unified Settings and publishes the current values; null if the service is missing.</summary>
        public static SettingsStore Create(IServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(services.GetService(typeof(SVsUnifiedSettingsManager)) is ISettingsManager manager))
            {
                Log.Info("Unified Settings is not available; using default options.");
                return null;
            }
            var state = new ShellSettingsManager(services).GetWritableSettingsStore(SettingsScope.UserSettings);
            var store = new SettingsStore(manager, state);
            store.Publish();
            return store;
        }

        public void Publish()
        {
            try
            {
                var settings = Read();
                SqlSenseSettings.Update(settings);
                SyncSsmsIntelliSense(settings.Enabled && Get(General + "disableSsmsIntelliSense", true));
            }
            catch (Exception ex)
            {
                Log.Error("Reading SqlSense options", ex);
            }
        }

        /// <summary>
        /// Turns SSMS's own T-SQL IntelliSense off while SqlSense is enabled with that option on, and back on
        /// afterwards, but only if SqlSense was the one that turned it off.
        /// </summary>
        private void SyncSsmsIntelliSense(bool wantOff)
        {
            lock (_syncLock)
            {
                var ssmsOn = Get(SsmsIntelliSense, true);
                var turnedOff = _state.CollectionExists(StateCollection) && _state.GetBoolean(StateCollection, TurnedOffFlag, false);
                if (wantOff && ssmsOn)
                {
                    if (!WriteSsmsIntelliSense(false)) return;
                    if (!_state.CollectionExists(StateCollection)) _state.CreateCollection(StateCollection);
                    _state.SetBoolean(StateCollection, TurnedOffFlag, true);
                }
                else if (!wantOff && turnedOff)
                {
                    if (!ssmsOn && !WriteSsmsIntelliSense(true)) return;
                    _state.SetBoolean(StateCollection, TurnedOffFlag, false);
                }
            }
        }

        private bool WriteSsmsIntelliSense(bool on)
        {
            var writer = _manager.GetWriter("SqlSense");
            writer.EnqueueChange(SsmsIntelliSense, on);
            var result = writer.RequestCommit(on ? "Restore SSMS IntelliSense" : "Turn off SSMS IntelliSense for SqlSense");
            var ok = result.Outcome == SettingCommitOutcome.Success;
            Log.Info(ok ? (on ? "Turned SSMS IntelliSense back on." : "Turned SSMS IntelliSense off (SqlSense option).")
                        : $"Changing SSMS IntelliSense failed: {result.Outcome} {result.Message}");
            return ok;
        }

        private SqlSenseSettings Read()
        {
            var snippets = Get(General + "snippetsFolder", "");
            var colorsFile = Get(ConnectionColors + "rulesFile", "");
            return new SqlSenseSettings
            {
                ConnectionColors = Get(ConnectionColors + "enabled", true),
                ColorTabs = Get(ConnectionColors + "colorTabs", true),
                ColorStatusBar = Get(ConnectionColors + "colorStatusBar", true),
                HideSetTabColor = Get(ConnectionColors + "hideSetTabColor", true),
                UseSsmsConnectionColor = Get(ConnectionColors + "useSsmsColor", true),
                ConnectionColorsFile = string.IsNullOrWhiteSpace(colorsFile) ? SqlSenseSettings.DefaultConnectionColorsFile : colorsFile,
                Enabled = Get(General + "enabled", true),
                HighlightInsertPairs = Get(General + "highlightInsertPairs", true),
                SnippetsFolder = string.IsNullOrWhiteSpace(snippets) ? SqlSenseSettings.DefaultSnippetsFolder : snippets,
                SchemaCacheMinutes = Math.Max(1, Get(General + "schemaCacheMinutes", 30)),
                AutoTrigger = Get(Suggestions + "autoTrigger", true),
                AutoCloseBrackets = Get(Suggestions + "autoCloseBrackets", true),
                TabExpandsSnippets = Get(Suggestions + "tabExpandsSnippets", true),
                TabExpandsWildcard = Get(Suggestions + "tabExpandsWildcard", true),
                IncludeSystemObjects = Get(Suggestions + "includeSystemObjects", true),
                Completion = new CompletionOptions
                {
                    AutoAlias = Get(Suggestions + "autoAlias", true),
                    AliasWithAs = Get(Suggestions + "aliasWithAs", true),
                    AlwaysQualifySchema = Get(Suggestions + "alwaysQualifySchema", true),
                    InsertGeneratesColumns = Get(Suggestions + "insertGeneratesColumns", true),
                    ExecGeneratesParameters = Get(Suggestions + "execGeneratesParameters", true),
                    AlterScriptsDefinition = Get(Suggestions + "alterScriptsDefinition", true),
                    SuggestJoinConditions = Get(Suggestions + "suggestJoinConditions", true),
                    SuggestSameNameJoins = Get(Suggestions + "suggestSameNameJoins", true),
                    UppercaseKeywords = Get(Suggestions + "uppercaseKeywords", true),
                },
                Format = ReadFormatOptions(),
            };
        }

        public FormatOptions ReadFormatOptions() => new FormatOptions
        {
            IndentSize = Math.Max(1, Get(Formatting + "indentSize", 4)),
            KeywordCase = GetEnum(Formatting + "keywordCase", KeywordCasing.Upper),
            FunctionCase = GetEnum(Formatting + "functionCase", KeywordCasing.Upper),
            DataTypeCase = GetEnum(Formatting + "dataTypeCase", KeywordCasing.Lower),
            GlobalVariableCase = GetEnum(Formatting + "globalVariableCase", KeywordCasing.AsIs),
            UseObjectDefinitionCase = Get(Formatting + "useObjectDefinitionCase", false),
            LeadingCommas = Get(Formatting + "leadingCommas", false),
            OneItemPerLine = Get(Formatting + "oneItemPerLine", true),
            BreakAfterDistinctTop = Get(Formatting + "breakAfterDistinctTop", false),
            BreakOnAndOr = Get(Formatting + "breakOnAndOr", true),
            IndentJoins = Get(Formatting + "indentJoins", true),
            FirstWhenOnNewLine = GetEnum(Formatting + "firstWhenOnNewLine", CaseWhenPlacement.Always),
            SpaceInsideInLists = Get(Formatting + "spaceInsideInLists", false),
            SubqueryParenStyle = GetEnum(Formatting + "subqueryParenStyle", ParenthesisStyle.ExpandedSplit),
            DdlParenStyle = GetEnum(Formatting + "ddlParenStyle", ParenthesisStyle.ExpandedSplit),
            DdlConstraintsOnNewLines = Get(Formatting + "ddlConstraintsOnNewLines", false),
            ConstraintColumnsOnNewLines = GetEnum(Formatting + "constraintColumnsOnNewLines", ListWrap.Never),
            AlignAliases = Get(Formatting + "alignAliases", false),
            AlignComments = Get(Formatting + "alignComments", false),
            AlignMultilineComments = Get(Formatting + "alignMultilineComments", false),
            KeepBlankLinesAfterGo = Get(Formatting + "keepBlankLinesAfterGo", true),
            CollapseStatementsShorterThan = Math.Max(0, Get(Collapse + "statementsShorterThan", 0)),
            CollapseSubqueriesShorterThan = Math.Max(0, Get(Collapse + "subqueriesShorterThan", 0)),
            CollapseCaseShorterThan = Math.Max(0, Get(Collapse + "caseShorterThan", 0)),
            CollapseDdlShorterThan = Math.Max(0, Get(Collapse + "ddlShorterThan", 0)),
        };

        /// <summary>Saves formatter settings (e.g. from an imported SQL Prompt style). Returns an error message, or null.</summary>
        public string WriteFormatOptions(FormatOptions o)
        {
            var writer = _manager.GetWriter("SqlSense");
            writer.EnqueueChange(Formatting + "indentSize", o.IndentSize);
            writer.EnqueueChange(Formatting + "keywordCase", o.KeywordCase.ToString());
            writer.EnqueueChange(Formatting + "functionCase", o.FunctionCase.ToString());
            writer.EnqueueChange(Formatting + "dataTypeCase", o.DataTypeCase.ToString());
            writer.EnqueueChange(Formatting + "globalVariableCase", o.GlobalVariableCase.ToString());
            writer.EnqueueChange(Formatting + "useObjectDefinitionCase", o.UseObjectDefinitionCase);
            writer.EnqueueChange(Formatting + "leadingCommas", o.LeadingCommas);
            writer.EnqueueChange(Formatting + "oneItemPerLine", o.OneItemPerLine);
            writer.EnqueueChange(Formatting + "breakAfterDistinctTop", o.BreakAfterDistinctTop);
            writer.EnqueueChange(Formatting + "breakOnAndOr", o.BreakOnAndOr);
            writer.EnqueueChange(Formatting + "indentJoins", o.IndentJoins);
            writer.EnqueueChange(Formatting + "firstWhenOnNewLine", o.FirstWhenOnNewLine.ToString());
            writer.EnqueueChange(Formatting + "spaceInsideInLists", o.SpaceInsideInLists);
            writer.EnqueueChange(Formatting + "subqueryParenStyle", o.SubqueryParenStyle.ToString());
            writer.EnqueueChange(Formatting + "ddlParenStyle", o.DdlParenStyle.ToString());
            writer.EnqueueChange(Formatting + "ddlConstraintsOnNewLines", o.DdlConstraintsOnNewLines);
            writer.EnqueueChange(Formatting + "constraintColumnsOnNewLines", o.ConstraintColumnsOnNewLines.ToString());
            writer.EnqueueChange(Formatting + "alignAliases", o.AlignAliases);
            writer.EnqueueChange(Formatting + "alignComments", o.AlignComments);
            writer.EnqueueChange(Formatting + "alignMultilineComments", o.AlignMultilineComments);
            writer.EnqueueChange(Formatting + "keepBlankLinesAfterGo", o.KeepBlankLinesAfterGo);
            writer.EnqueueChange(Collapse + "statementsShorterThan", o.CollapseStatementsShorterThan);
            writer.EnqueueChange(Collapse + "subqueriesShorterThan", o.CollapseSubqueriesShorterThan);
            writer.EnqueueChange(Collapse + "caseShorterThan", o.CollapseCaseShorterThan);
            writer.EnqueueChange(Collapse + "ddlShorterThan", o.CollapseDdlShorterThan);
            return Commit(writer, "Import SQL Prompt style");
        }

        /// <summary>Tools › SqlSense › Enable/Disable. Returns an error message, or null.</summary>
        public string SetEnabled(bool enabled)
        {
            var writer = _manager.GetWriter("SqlSense");
            writer.EnqueueChange(General + "enabled", enabled);
            return Commit(writer, enabled ? "Enable SqlSense" : "Disable SqlSense");
        }

        private string Commit(ISettingsWriter writer, string description)
        {
            var result = writer.RequestCommit(description);
            Publish();
            if (result.Outcome == SettingCommitOutcome.Success) return null;
            Log.Info($"Saving options ({description}): {result.Outcome} {result.Message}");
            return result.Outcome + ": " + result.Message;
        }

        private T Get<T>(string moniker, T fallback)
        {
            var r = _reader.GetValue<T>(moniker);
            if (r.Outcome == SettingRetrievalOutcome.Success) return r.Value;
            Log.Info($"Option {moniker}: {r.Outcome} {r.Message}; using {fallback}.");
            return fallback;
        }

        private TEnum GetEnum<TEnum>(string moniker, TEnum fallback) where TEnum : struct
            => Enum.TryParse(Get(moniker, fallback.ToString()), true, out TEnum value) ? value : fallback;

        public void Dispose() => _subscription.Dispose();
    }

    /// <summary>
    /// Service id of <see cref="ISettingsManager"/>. The real type is in Microsoft.Internal.VisualStudio.Interop,
    /// which the public SDK doesn't ship; GetService only needs the GUID.
    /// </summary>
    [ComImport, Guid("e3684f31-344e-42ea-9047-b620fdc7ac25"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface SVsUnifiedSettingsManager
    {
    }
}
