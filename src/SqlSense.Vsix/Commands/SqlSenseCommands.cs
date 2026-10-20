using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
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
                var options = _package.Options;
                options.Enabled = !options.Enabled;
                options.SaveSettingsToStorage();
                options.Publish();
                EditorActions.Status(options.Enabled ? "SqlSense enabled." : "SqlSense disabled.");
            }, cmd => cmd.Text = SqlSenseSettings.Current.Enabled ? "Disable SqlSense" : "Enable SqlSense");
            Add(commands, PackageIds.ShowLog, (s, e) => LogPane.Show());
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
