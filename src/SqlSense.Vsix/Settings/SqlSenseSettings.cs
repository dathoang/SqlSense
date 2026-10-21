using System;
using System.IO;
using SqlSense.Core.Completion;
using SqlSense.Core.Formatting;

namespace SqlSense.Settings
{
    /// <summary>Current settings, updated by SettingsStore from Unified Settings. Read from any thread.</summary>
    internal sealed class SqlSenseSettings
    {
        public static SqlSenseSettings Current { get; private set; } = new SqlSenseSettings();

        public bool Enabled { get; set; } = true;
        public bool AutoTrigger { get; set; } = true;
        public bool AutoCloseBrackets { get; set; } = true;
        public bool TabExpandsSnippets { get; set; } = true;
        public bool TabExpandsWildcard { get; set; } = true;
        public bool HighlightInsertPairs { get; set; } = true;
        public bool IncludeSystemObjects { get; set; } = true;
        public int SchemaCacheMinutes { get; set; } = 30;
        public string SnippetsFolder { get; set; } = DefaultSnippetsFolder;

        public bool ConnectionColors { get; set; } = true;
        public bool ColorTabs { get; set; } = true;
        public bool ColorStatusBar { get; set; } = true;
        public bool HideSetTabColor { get; set; } = true;
        public bool UseSsmsConnectionColor { get; set; } = true;
        public string ConnectionColorsFile { get; set; } = DefaultConnectionColorsFile;

        public CompletionOptions Completion { get; set; } = new CompletionOptions();
        public FormatOptions Format { get; set; } = new FormatOptions();

        public static string DefaultSnippetsFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SqlSense", "Snippets");

        public static string DefaultConnectionColorsFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SqlSense", "ConnectionColors.txt");

        public static event EventHandler Changed;

        public static void Update(SqlSenseSettings settings)
        {
            Current = settings ?? new SqlSenseSettings();
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public SnippetLibrary LoadSnippets()
        {
            var lib = new SnippetLibrary();
            try
            {
                lib.LoadFolder(SnippetsFolder);
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error("Loading snippets from " + SnippetsFolder, ex);
            }
            return lib;
        }
    }
}
