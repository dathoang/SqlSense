using System;
using System.ComponentModel.Design;
using System.IO;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using SqlSense.Connections;
using SqlSense.Core.Connections;
using SqlSense.Diagnostics;
using SqlSense.Editor;
using SqlSense.Results;
using SqlSense.Schema;
using SqlSense.Settings;
using Task = System.Threading.Tasks.Task;

namespace SqlSense.Commands
{
    /// <summary>Tools › SqlSense menu: Format SQL (Ctrl+K, Ctrl+Y), Expand Wildcard (Ctrl+B, Ctrl+W), …</summary>
    internal static class SqlSenseCommands
    {
        private static SqlSensePackage _package;
        private static IVsEditorAdaptersFactoryService _adapters;
        private static IVsTextManager _textManager;

        public static async Task InitializeAsync(SqlSensePackage package)
        {
            _package = package;
            var commands = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            var componentModel = await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            _textManager = await package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (commands == null || componentModel == null)
            {
                Log.Error("Menu or component-model service unavailable; SqlSense menu commands are disabled");
                return;
            }
            _adapters = componentModel.GetService<IVsEditorAdaptersFactoryService>();

            Add(commands, PackageIds.FormatSql, (s, e) => WithSqlView(EditorActions.Format), SqlViewVisible);
            Add(commands, PackageIds.ExpandWildcards, (s, e) => WithSqlView(v =>
            {
                if (!EditorActions.TryExpandWildcard(v, searchLine: true))
                    EditorActions.Status("SqlSense: no SELECT * with known columns on this line (is the schema loaded?).");
            }), SqlViewVisible);
            Add(commands, PackageIds.RefreshSuggestions, (s, e) =>
            {
                SchemaService.Clear();
                EditorActions.Status("SqlSense: suggestions will reload database metadata.");
            });
            Add(commands, PackageIds.ToggleEnabled, (s, e) =>
            {
                var enable = !SqlSenseSettings.Current.Enabled;
                var error = _package.Options?.SetEnabled(enable) ?? "Unified Settings is not available.";
                EditorActions.Status(error != null ? "Couldn't change SqlSense: " + error
                    : enable ? "SqlSense enabled." : "SqlSense disabled.");
            }, cmd => cmd.Text = SqlSenseSettings.Current.Enabled ? "Disable SqlSense" : "Enable SqlSense");
            Add(commands, PackageIds.ShowLog, (s, e) => LogPane.Show());
            Add(commands, PackageIds.ImportSqlPromptStyle, (s, e) => StyleImport.Run(_package, _package.Options));
            Add(commands, PackageIds.EditConnectionColors, (s, e) => EditConnectionColors());

            // Document tab › Connection Color (the right-clicked tab is the active window).
            Add(commands, PackageIds.TabDatabaseColor, (s, e) => PickTabColor(forDatabase: true), cmd => TabColorStatus(cmd, forDatabase: true, clear: false));
            Add(commands, PackageIds.TabServerColor, (s, e) => PickTabColor(forDatabase: false), cmd => TabColorStatus(cmd, forDatabase: false, clear: false));
            Add(commands, PackageIds.TabClearDatabaseColor, (s, e) => ClearTabColor(forDatabase: true), cmd => TabColorStatus(cmd, forDatabase: true, clear: true));
            Add(commands, PackageIds.TabClearServerColor, (s, e) => ClearTabColor(forDatabase: false), cmd => TabColorStatus(cmd, forDatabase: false, clear: true));
            Add(commands, PackageIds.TabEditConnectionColors, (s, e) => EditConnectionColors(), cmd =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                cmd.Visible = ConnectionColorService.TryGetActiveWindow(out _, out _, out _);
            });
            Add(commands, PackageIds.OpenResultsInExcel, (s, e) => ResultActions.OpenInExcel());
            Add(commands, PackageIds.ScriptResultsAsInsert, (s, e) => ResultActions.ScriptAsInsert(ActiveSqlView(), ActiveTextView));
        }

        private static void Add(OleMenuCommandService service, int id, EventHandler invoke, Action<OleMenuCommand> beforeQueryStatus = null)
        {
            var cmd = new OleMenuCommand((s, e) =>
            {
                try
                {
                    invoke(s, e);
                }
                catch (Exception ex)
                {
                    Log.Error("Command " + id, ex);
                }
            }, new CommandID(PackageGuids.CommandSet, id));
            if (beforeQueryStatus != null) cmd.BeforeQueryStatus += (s, e) => beforeQueryStatus((OleMenuCommand)s);
            service.AddCommand(cmd);
        }

        /// <summary>Opens the connection colours file, creating it from the commented template first.</summary>
        private static void EditConnectionColors()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = SqlSenseSettings.Current.ConnectionColorsFile;
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, ConnectionColorRules.Template);
            }
            VsShellUtilities.OpenDocument(_package, path);
            if (!SqlSenseSettings.Current.ConnectionColors)
                EditorActions.Status("SqlSense: connection colours are off (Tools › Options › SqlSense › Connection Colors).");
        }

        private static void TabColorStatus(OleMenuCommand cmd, bool forDatabase, bool clear)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var rules = _package.ConnectionColors?.Rules;
            var connected = ConnectionColorService.TryGetActiveWindow(out var server, out var database, out _);
            cmd.Visible = rules != null && connected
                          && (!forDatabase || !string.IsNullOrEmpty(database))
                          && (!clear || rules.FindExact(server, forDatabase ? database : null) != null);
            if (!cmd.Visible) return;
            var target = forDatabase ? $"Database '{database}'" : $"Server '{server}'";
            cmd.Text = clear ? $"Remove Color for {target}" : $"Color for {target}...";
        }

        private static void PickTabColor(bool forDatabase)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!ConnectionColorService.TryGetActiveWindow(out var server, out var database, out var current)) return;
            var target = forDatabase ? $"{server} / {database}" : $"{server} (all databases)";
            if (!ConnectionColorPicker.Show(target, current, out var argb)) return;
            UpdateRules(text => ConnectionColorRules.SetRule(text, server, forDatabase ? database : null, argb));
        }

        private static void ClearTabColor(bool forDatabase)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!ConnectionColorService.TryGetActiveWindow(out var server, out var database, out _)) return;
            UpdateRules(text => ConnectionColorRules.RemoveRule(text, server, forDatabase ? database : null));
        }

        /// <summary>Rewrites the connection colours file (created from the template if missing) and repaints.</summary>
        private static void UpdateRules(Func<string, string> change)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = SqlSenseSettings.Current.ConnectionColorsFile;
            try
            {
                var text = File.Exists(path) ? File.ReadAllText(path) : ConnectionColorRules.Template;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, change(text));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("Writing " + path, ex);
                EditorActions.Status("SqlSense: couldn't save " + path + ": " + ex.Message);
                return;
            }
            _package.ConnectionColors?.Reload();
            if (!SqlSenseSettings.Current.ConnectionColors)
                EditorActions.Status("SqlSense: saved, but connection colours are off (Tools › Options › SqlSense › Connection Colors).");
        }

        private static void SqlViewVisible(OleMenuCommand cmd)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var visible = ActiveSqlView() != null;
            cmd.Visible = visible;
            cmd.Enabled = visible;
        }

        private static void WithSqlView(Action<IWpfTextView> action)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = ActiveSqlView();
            if (view == null)
            {
                EditorActions.Status("SqlSense: open a SQL query window first.");
                return;
            }
            action(view);
        }

        private static IWpfTextView ActiveSqlView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = ActiveTextView();
            return view != null && SqlDocument.IsSql(view.TextBuffer) ? view : null;
        }

        private static IWpfTextView ActiveTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_textManager == null || _textManager.GetActiveView(1, null, out var vsView) != 0 || vsView == null) return null;
            return _adapters.GetWpfTextView(vsView);
        }
    }
}
