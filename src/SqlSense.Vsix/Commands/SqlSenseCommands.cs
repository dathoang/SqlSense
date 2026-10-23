using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
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
                // The window's database (or the one a USE before the caret names), reloaded now behind the old snapshot.
                var view = ActiveSqlView();
                string useDatabase = null;
                if (view != null)
                    useDatabase = SqlDocument.DatabaseFromUse(view.TextSnapshot.GetText(), view.Caret.Position.BufferPosition.Position);
                var conn = SsmsConnection.GetActive();
                if (!SchemaService.Refresh(conn, useDatabase))
                    EditorActions.Status(conn?.Unsupported != null
                        ? "SqlSense: no metadata for this connection (" + conn.Unsupported + ")."
                        : "SqlSense: open a query window connected to a database first.");
            });
            Add(commands, PackageIds.RefreshAllSuggestions, (s, e) =>
            {
                SchemaService.RefreshAll();
                EditorActions.Status("SqlSense: all cached metadata reloads on next use (suggestions keep working meanwhile).");
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

            // Document tab › Connection Color › Database / Server (the right-clicked tab is the active window).
            AddTabColorMenu(commands, PackageIds.TabDbMenu, PackageIds.TabDbColorBase, forDatabase: true);
            AddTabColorMenu(commands, PackageIds.TabServerMenu, PackageIds.TabServerColorBase, forDatabase: false);
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

        /// <summary>
        /// One of the tab menu's colour lists: the submenu (its caption names the database or server), a slot per
        /// <see cref="ColorText.Palette"/> colour, then No Color, Custom... and Remove Rule. The ticked item is the
        /// colour of the rule written for exactly this database or server.
        /// </summary>
        private static void AddTabColorMenu(OleMenuCommandService commands, int menuId, int baseId, bool forDatabase)
        {
            Add(commands, menuId, (s, e) => { }, cmd =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                cmd.Visible = TryGetTabTarget(forDatabase, out var server, out var database, out _);
                if (cmd.Visible) cmd.Text = forDatabase ? $"Database '{database}'" : $"Server '{server}'";
            });
            for (var i = 0; i < PackageIds.TabPaletteSlots; i++)
            {
                var slot = i;
                Add(commands, baseId + slot, (s, e) => SetTabColor(forDatabase, ColorText.Palette[slot].Value), cmd =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    ConnectionColorRule rule = null;
                    cmd.Visible = slot < ColorText.Palette.Count && TryGetTabTarget(forDatabase, out _, out _, out rule);
                    if (!cmd.Visible) return;
                    cmd.Text = Spaced(ColorText.Palette[slot].Key);
                    cmd.Checked = rule?.Argb == ColorText.Palette[slot].Value;
                });
            }
            Add(commands, baseId + PackageIds.TabNoColorOffset, (s, e) => SetTabColor(forDatabase, null), cmd =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                cmd.Visible = TryGetTabTarget(forDatabase, out _, out _, out var rule);
                cmd.Checked = rule != null && rule.Argb == null;
            });
            Add(commands, baseId + PackageIds.TabCustomOffset, (s, e) => PickCustomTabColor(forDatabase), cmd =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                cmd.Visible = TryGetTabTarget(forDatabase, out _, out _, out var rule);
                cmd.Checked = rule?.Argb is uint argb && !ColorText.Palette.Any(p => p.Value == argb);
            });
            Add(commands, baseId + PackageIds.TabRemoveOffset, (s, e) => RemoveTabColor(forDatabase), cmd =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                cmd.Visible = TryGetTabTarget(forDatabase, out _, out _, out var rule) && rule != null;
            });
        }

        /// <summary>The active query window's server and database, and the rule written for exactly that target.</summary>
        private static bool TryGetTabTarget(bool forDatabase, out string server, out string database, out ConnectionColorRule rule)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            server = database = null;
            rule = null;
            var rules = _package.ConnectionColors?.Rules;
            if (rules == null || !ConnectionColorService.TryGetActiveWindow(out server, out database, out _)) return false;
            if (forDatabase && string.IsNullOrEmpty(database)) return false;
            rule = rules.FindExact(server, forDatabase ? database : null);
            return true;
        }

        /// <summary>"DarkRed" → "Dark Red".</summary>
        private static string Spaced(string name)
            => System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");

        private static void SetTabColor(bool forDatabase, uint? argb)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetTabTarget(forDatabase, out var server, out var database, out _)) return;
            UpdateRules(text => ConnectionColorRules.SetRule(text, server, forDatabase ? database : null, argb));
        }

        private static void PickCustomTabColor(bool forDatabase)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetTabTarget(forDatabase, out _, out _, out var rule)) return;
            ConnectionColorService.TryGetActiveWindow(out _, out _, out var current);
            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
            {
                if (rule?.Argb is uint argb) dialog.Color = System.Drawing.Color.FromArgb(unchecked((int)(argb | 0xFF000000)));
                else if (current is System.Windows.Media.Color c) dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                SetTabColor(forDatabase, 0xFF000000u | (uint)(dialog.Color.ToArgb() & 0xFFFFFF));
            }
        }

        private static void RemoveTabColor(bool forDatabase)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetTabTarget(forDatabase, out var server, out var database, out _)) return;
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

        internal static IWpfTextView ActiveSqlView()
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
