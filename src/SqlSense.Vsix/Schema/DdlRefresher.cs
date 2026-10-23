using System;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using SqlSense.Core.Analysis;
using SqlSense.Diagnostics;
using SqlSense.Settings;

namespace SqlSense.Schema
{
    /// <summary>
    /// Watches SSMS's Query.Execute: when the script being run (the selection, else the whole window) creates,
    /// alters or drops objects, the affected databases' metadata is reloaded shortly after, so new tables and
    /// columns show up without Refresh Suggestions.
    /// </summary>
    internal sealed class DdlRefresher
    {
        /// <summary>Time for the batch to reach the server; a metadata read that meets the DDL's locks waits for it.</summary>
        private static readonly TimeSpan Delay = TimeSpan.FromSeconds(1.5);

        private readonly Func<IWpfTextView> _activeSqlView;
        private CommandEvents _events; // must stay referenced, or the COM event sink is collected

        private DdlRefresher(Func<IWpfTextView> activeSqlView)
        {
            _activeSqlView = activeSqlView;
        }

        public static DdlRefresher Create(DTE dte, Func<IWpfTextView> activeSqlView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var command = dte?.Commands.Item("Query.Execute", -1);
                if (command == null) return null;
                var refresher = new DdlRefresher(activeSqlView) { _events = dte.Events.CommandEvents[command.Guid, command.ID] };
                refresher._events.BeforeExecute += refresher.OnBeforeExecute;
                return refresher;
            }
            catch (Exception ex)
            {
                Log.Info("Query.Execute not found; metadata won't refresh after DDL: " + ex.Message);
                return null;
            }
        }

        private void OnBeforeExecute(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var settings = SqlSenseSettings.Current;
                if (!settings.Enabled || !settings.RefreshAfterDdl) return;
                var view = _activeSqlView();
                if (view == null) return;
                var selection = view.Selection;
                var script = selection.IsEmpty
                    ? view.TextSnapshot.GetText()
                    : string.Join(Environment.NewLine, selection.SelectedSpans.Select(s => s.GetText()));
                var conn = SsmsConnection.GetActive();
                if (conn == null || conn.Unsupported != null) return;
                var databases = DdlDetector.ChangedDatabases(script, conn.Database);
                if (databases.Count == 0) return;
                Log.Info("DDL executed; refreshing metadata for " + string.Join(", ", databases) + ".");

#pragma warning disable VSSDK007
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await Task.Delay(Delay);
                    foreach (var db in databases) SchemaService.Refresh(conn, db);
                }).FileAndForget("SqlSense/RefreshAfterDdl");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Log.Error("Checking the executed script for DDL", ex);
            }
        }
    }
}
