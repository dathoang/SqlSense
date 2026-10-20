using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using SqlSense.Core.Export;
using SqlSense.Diagnostics;
using SqlSense.Editor;

namespace SqlSense.Results
{
    /// <summary>Results grid commands: open the grid in Excel, script it as INSERT statements.</summary>
    internal static class ResultActions
    {
        public static void OpenInExcel()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = ResultGridReader.ReadActive(out var why);
            if (snapshot == null)
            {
                EditorActions.Status("SqlSense: " + why);
                return;
            }
            if (snapshot.Table.Rows.Count >= XlsxWriter.MaxRows)
                Log.Info($"Excel export truncated to {XlsxWriter.MaxRows - 1:N0} rows (grid has {snapshot.Table.Rows.Count:N0}).");

            var dir = Path.Combine(Path.GetTempPath(), "SqlSense");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "Results_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".xlsx");
            using (var file = File.Create(path))
                XlsxWriter.Write(file, snapshot.Table, "Result" + (snapshot.GridCount > 1 ? " " + (snapshot.GridIndex + 1) : string.Empty));

            Log.Info($"Exported {snapshot.Table.Rows.Count:N0} rows × {snapshot.Table.Columns.Count} columns to {path}");
            try
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                EditorActions.Status($"SqlSense: opened {Rows(snapshot)} in Excel.");
            }
            catch (Exception ex)
            {
                // No app registered for .xlsx: leave the file and say where it is.
                Log.Error("Opening " + path, ex);
                EditorActions.Status("SqlSense: saved " + path + " (no application is registered for .xlsx).");
            }
        }

        /// <param name="sourceView">The query window's editor, used to guess the target table; may be null.</param>
        public static void ScriptAsInsert(IWpfTextView sourceView, Func<IWpfTextView> activeView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = ResultGridReader.ReadActive(out var why);
            if (snapshot == null)
            {
                EditorActions.Status("SqlSense: " + why);
                return;
            }

            var query = sourceView == null ? null
                : sourceView.Selection.IsEmpty ? sourceView.TextBuffer.CurrentSnapshot.GetText()
                : sourceView.Selection.StreamSelectionSpan.GetText();
            var table = InsertScriptGenerator.GuessTableName(query, snapshot.GridIndex) ?? InsertScriptGenerator.DefaultTableName;
            var script = "-- " + Rows(snapshot) + " from result set " + (snapshot.GridIndex + 1) + Environment.NewLine
                         + InsertScriptGenerator.Generate(snapshot.Table, table);

            if (!TryOpenInNewQuery(script, sourceView, activeView))
            {
                System.Windows.Clipboard.SetText(script);
                EditorActions.Status($"SqlSense: INSERT script for {Rows(snapshot)} copied to the clipboard.");
                return;
            }
            EditorActions.Status($"SqlSense: scripted {Rows(snapshot)} as INSERT into {table}.");
        }

        /// <summary>Opens a new query window (same connection, like Ctrl+N) and puts the script in it.</summary>
        private static bool TryOpenInNewQuery(string script, IWpfTextView sourceView, Func<IWpfTextView> activeView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!(Package.GetGlobalService(typeof(DTE)) is DTE dte)) return false;
                dte.ExecuteCommand("File.NewQuery");
                var view = activeView();
                if (view == null || ReferenceEquals(view, sourceView) || view.TextBuffer.CurrentSnapshot.Length > 0) return false;
                view.TextBuffer.Insert(0, script);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Opening a new query window for the INSERT script", ex);
                return false;
            }
        }

        private static string Rows(GridSnapshot s) =>
            s.Table.Rows.Count.ToString("N0", CultureInfo.CurrentCulture) + (s.Table.Rows.Count == 1 ? " row" : " rows")
            + (s.FromSelection ? " (selection)" : string.Empty);
    }
}
