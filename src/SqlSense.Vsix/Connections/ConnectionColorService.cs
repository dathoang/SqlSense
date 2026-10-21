using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlSense.Core.Connections;
using SqlSense.Diagnostics;
using SqlSense.Settings;

namespace SqlSense.Connections
{
    /// <summary>
    /// Colours each SSMS query window by its server and database, in the places SSMS and the shell already
    /// colour:
    /// <list type="bullet">
    /// <item>the document tab and the frame around the active document: the shell's tab colouring
    /// (DocumentView.IsTabGroupColorized / TabAccentBrush, which the document well's content border also uses
    /// while "colorize document tabs" is on);</item>
    /// <item>the query window status bar: QEStatusBarManager.singleServerBackColor, the colour SSMS takes from
    /// the Connect dialog's "Use custom color".</item>
    /// </list>
    /// The window's DocView is SSMS's SqlScriptEditorControl, whose ConnectionInfoList[0] is the UIConnectionInfo;
    /// SSMS rewrites its DATABASE option when the database changes (drop-down or USE). None of this raises a
    /// public event, so open windows are re-read every second.
    /// </summary>
    internal sealed class ConnectionColorService : IDisposable
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private readonly IVsUIShell _shell;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<DependencyObject, TabPainter> _tabs = new Dictionary<DependencyObject, TabPainter>();
        private readonly Dictionary<object, StatusBarPainter> _statusBars = new Dictionary<object, StatusBarPainter>();

        private SqlSenseSettings _settings;
        private ConnectionColorRules _rules = ConnectionColorRules.Empty;
        private string _rulesPath;
        private DateTime _rulesStamp;
        private bool _turnedOnTabColors;

        private ConnectionColorService(IVsUIShell shell)
        {
            _shell = shell;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Refresh();
            };
            _timer.Start();
        }

        public static ConnectionColorService Create(IVsUIShell shell)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (shell == null)
            {
                Log.Info("Connection colours are off: the shell service is unavailable.");
                return null;
            }
            var service = new ConnectionColorService(shell);
            service.Refresh();
            return service;
        }

        /// <summary>Re-reads every open query window and repaints what changed.</summary>
        public void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var settings = _settings = SqlSenseSettings.Current;
                LoadRules(settings);

                var seenTabs = new HashSet<DependencyObject>();
                var seenStatusBars = new HashSet<object>();
                var anyTabColor = false;

                if (ErrorHandler.Succeeded(_shell.GetDocumentWindowEnum(out var frames)) && frames != null)
                {
                    var one = new IVsWindowFrame[1];
                    while (frames.Next(1, one, out var fetched) == VSConstants.S_OK && fetched == 1)
                    {
                        var frame = one[0];
                        if (ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView))) continue;
                        var connected = TryReadConnection(docView, out var server, out var database, out var ssmsColor);
                        var color = connected && settings.ConnectionColors ? Resolve(server, database, ssmsColor) : null;

                        if (settings.ColorTabs && color.HasValue)
                        {
                            PaintTab(frame, color.Value, seenTabs);
                            anyTabColor = true;
                        }
                        // Every query window is visited, so one that lost its colour gets SSMS's back.
                        if (docView != null && (connected || _statusBars.ContainsKey(docView)))
                            PaintStatusBar(docView, settings.ColorStatusBar ? color : null, seenStatusBars);
                    }
                }

                foreach (var view in new List<DependencyObject>(_tabs.Keys))
                {
                    if (seenTabs.Contains(view)) continue;
                    _tabs[view].Release();
                    _tabs.Remove(view);
                }
                foreach (var docView in new List<object>(_statusBars.Keys))
                    if (!seenStatusBars.Contains(docView)) _statusBars.Remove(docView);

                SyncTabColorPreference(anyTabColor || (_turnedOnTabColors && settings.ConnectionColors && settings.ColorTabs));
            }
            catch (Exception ex)
            {
                _timer.Stop();
                Log.Error("Connection colours stopped", ex);
            }
        }

        /// <summary>Reloads the rules file now (after SqlSense itself wrote it) and repaints.</summary>
        public void Reload()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _rulesPath = null;
            Refresh();
        }

        /// <summary>The rules as last read from the file.</summary>
        public ConnectionColorRules Rules => _rules;

        /// <summary>
        /// Server, database and current colour of the active query window. Right-clicking a document tab
        /// activates it first, so this is also the window a tab context menu was opened on.
        /// </summary>
        public static bool TryGetActiveWindow(out string server, out string database, out Color? color)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            server = database = null;
            color = null;
            if (!(Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection selection)
                || ErrorHandler.Failed(selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var value))
                || !(value is IVsWindowFrame frame)
                || ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView))
                || !TryReadConnection(docView, out server, out database, out var ssmsColor))
                return false;
            var service = SqlSensePackage.Instance?.ConnectionColors;
            if (service?._settings != null) color = service.Resolve(server, database, ssmsColor);
            return true;
        }

        private Color? Resolve(string server, string database, int? ssmsColor)
        {
            var rule = _rules.Find(server, database);
            if (rule != null) return rule.Argb is uint argb ? FromArgb(argb) : (Color?)null;
            return _settings.UseSsmsConnectionColor && ssmsColor.HasValue ? FromArgb(unchecked((uint)ssmsColor.Value)) : (Color?)null;
        }

        private static Color FromArgb(uint argb)
            => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        /// <summary>Reloads the rules file when its path or timestamp changed.</summary>
        private void LoadRules(SqlSenseSettings settings)
        {
            var path = settings.ConnectionColorsFile;
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (path == _rulesPath && stamp == _rulesStamp) return;
            _rulesPath = path;
            _rulesStamp = stamp;
            try
            {
                _rules = stamp == DateTime.MinValue ? ConnectionColorRules.Empty : ConnectionColorRules.Parse(File.ReadAllText(path));
            }
            catch (IOException ex)
            {
                // Probably still being written; read it again on the next tick.
                _rulesStamp = DateTime.MinValue;
                Log.Info($"Reading {path}: {ex.Message}");
                return;
            }
            Log.Info($"Connection colours: {_rules.Rules.Count} rule(s) from {path}.");
            foreach (var error in _rules.Errors) Log.Info($"{Path.GetFileName(path)} {error}");
        }

        /// <summary>Server, database and the SSMS "Use custom color" colour of a query window's DocView.</summary>
        private static bool TryReadConnection(object docView, out string server, out string database, out int? ssmsColor)
        {
            server = database = null;
            ssmsColor = null;
            var list = docView?.GetType().GetProperty("ConnectionInfoList", Any)?.GetValue(docView) as IList;
            if (list == null || list.Count == 0 || list[0] == null) return false;

            var ui = list[0];
            var t = ui.GetType();
            server = t.GetProperty("ServerName", Any)?.GetValue(ui) as string;
            if (string.IsNullOrEmpty(server)) return false;

            var advanced = t.GetProperty("AdvancedOptions", Any)?.GetValue(ui) as NameValueCollection;
            database = advanced?["DATABASE"];
            if (bool.TryParse(advanced?["USE_CUSTOM_CONNECTION_COLOR"], out var useColor) && useColor
                && int.TryParse(advanced["CUSTOM_CONNECTION_COLOR"], out var argb))
                ssmsColor = argb;
            return true;
        }

        private void PaintTab(IVsWindowFrame frame, Color color, HashSet<DependencyObject> seen)
        {
            if (!(frame.GetType().GetProperty("FrameView", Any)?.GetValue(frame) is DependencyObject view)) return;
            if (!_tabs.TryGetValue(view, out var painter))
            {
                painter = TabPainter.Create(view);
                if (painter == null) return;
                _tabs.Add(view, painter);
            }
            seen.Add(view);
            painter.Paint(color);
        }

        private void PaintStatusBar(object docView, Color? color, HashSet<object> seen)
        {
            if (!_statusBars.TryGetValue(docView, out var painter))
            {
                if (color == null) return;
                painter = StatusBarPainter.Create(docView);
                if (painter == null) return;
                _statusBars.Add(docView, painter);
            }
            seen.Add(docView);
            painter.Paint(color);
        }

        /// <summary>
        /// The shell draws tab colours, and the active document's frame in its tab colour, only while its
        /// "colorize document tabs" preference is on. It is switched on for this session while SqlSense colours
        /// a tab, and off again when SqlSense stops, only if SqlSense switched it on.
        /// </summary>
        private void SyncTabColorPreference(bool wanted)
        {
            var on = TabPainter.ColorizeDocumentTabs;
            if (wanted && on == false && TabPainter.SetColorizeDocumentTabs(true))
            {
                _turnedOnTabColors = true;
                Log.Info("Turned on document tab colours for this session (Environment › Tabs and Windows › Colorize document tabs).");
            }
            else if (!wanted && _turnedOnTabColors)
            {
                if (on == true) TabPainter.SetColorizeDocumentTabs(false);
                _turnedOnTabColors = false;
            }
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _timer.Stop();
            foreach (var painter in _tabs.Values) painter.Release();
            _tabs.Clear();
            foreach (var painter in _statusBars.Values) painter.Paint(null);
            _statusBars.Clear();
            SyncTabColorPreference(false);
        }

        /// <summary>
        /// Paints one document tab through WMView.IsTabGroupColorized and TabAccentBrush, the properties the
        /// shell's "Set Tab Color" uses. The shell resets them on its own triggers (e.g. a caption change), so
        /// they are re-applied whenever they change.
        /// </summary>
        private sealed class TabPainter
        {
            private static DependencyProperty _colorizedProperty;
            private static DependencyProperty _brushProperty;

            private readonly DependencyObject _view;
            private readonly DependencyPropertyDescriptor _colorized;
            private readonly DependencyPropertyDescriptor _brush;
            private SolidColorBrush _wanted;
            private bool _pending;

            private TabPainter(DependencyObject view)
            {
                _view = view;
                _colorized = DependencyPropertyDescriptor.FromProperty(_colorizedProperty, view.GetType());
                _brush = DependencyPropertyDescriptor.FromProperty(_brushProperty, view.GetType());
                _colorized.AddValueChanged(view, OnReset);
                _brush.AddValueChanged(view, OnReset);
            }

            public static TabPainter Create(DependencyObject view)
            {
                if (_colorizedProperty == null)
                {
                    _colorizedProperty = StaticField(view.GetType(), "IsTabGroupColorizedProperty");
                    _brushProperty = StaticField(view.GetType(), "TabAccentBrushProperty");
                    if (_colorizedProperty == null || _brushProperty == null)
                    {
                        Log.Info($"Document tabs can't be coloured: {view.GetType().FullName} has no tab colour properties.");
                        return null;
                    }
                }
                return _colorizedProperty.OwnerType.IsInstanceOfType(view) ? new TabPainter(view) : null;
            }

            /// <summary>The shell's "colorize document tabs" preference, or null when it can't be read.</summary>
            public static bool? ColorizeDocumentTabs
            {
                get
                {
                    var preferences = Preferences(out var preference);
                    return preferences == null ? null : preference.GetValue(preferences) as bool?;
                }
            }

            /// <summary>Sets the preference in memory (the Tabs and Windows options page saves it only when used).</summary>
            public static bool SetColorizeDocumentTabs(bool on)
            {
                var preferences = Preferences(out var preference);
                if (preferences == null || preference?.CanWrite != true) return false;
                try
                {
                    preference.SetValue(preferences, on);
                    return true;
                }
                catch (TargetInvocationException ex)
                {
                    Log.Error("Changing the document tab colour preference", ex.InnerException ?? ex);
                    return false;
                }
            }

            private static object Preferences(out PropertyInfo colorizeDocumentTabs)
            {
                colorizeDocumentTabs = null;
                var viewManager = Type.GetType("Microsoft.VisualStudio.PlatformUI.Shell.ViewManager, Microsoft.VisualStudio.Shell.ViewManager");
                var instance = viewManager?.GetProperty("Instance", Any)?.GetValue(null);
                var preferences = instance?.GetType().GetProperty("Preferences", Any)?.GetValue(instance);
                colorizeDocumentTabs = preferences?.GetType().GetProperty("ColorizeDocumentTabs", Any);
                return colorizeDocumentTabs == null ? null : preferences;
            }

            public void Paint(Color color)
            {
                if (_wanted == null || _wanted.Color != color)
                {
                    _wanted = new SolidColorBrush(color);
                    _wanted.Freeze();
                }
                Apply();
            }

            /// <summary>Stops painting and lets the shell work out the tab's own colour again.</summary>
            public void Release()
            {
                _colorized.RemoveValueChanged(_view, OnReset);
                _brush.RemoveValueChanged(_view, OnReset);
                if (_wanted == null) return;
                _wanted = null;
                _view.SetValue(_colorizedProperty, false);
                _view.ClearValue(_brushProperty);
                try
                {
                    _view.GetType().GetMethod("UpdateTabColor", Any, null, Type.EmptyTypes, null)?.Invoke(_view, null);
                }
                catch (TargetInvocationException ex)
                {
                    Log.Error("Restoring the tab colour", ex.InnerException ?? ex);
                }
            }

            private void Apply()
            {
                if (_wanted == null) return;
                if (!(_view.GetValue(_colorizedProperty) is bool on && on)) _view.SetValue(_colorizedProperty, true);
                if (!(_view.GetValue(_brushProperty) is SolidColorBrush b && b.Color == _wanted.Color)) _view.SetValue(_brushProperty, _wanted);
            }

            private void OnReset(object sender, EventArgs e)
            {
                if (_wanted == null || _pending) return;
                _pending = true;
                // Not inside the shell's own property change: re-apply once it has finished.
#pragma warning disable VSSDK007
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
                    _pending = false;
                    Apply();
                }).FileAndForget("SqlSense/TabColor");
#pragma warning restore VSSDK007
            }

            private static DependencyProperty StaticField(Type type, string name)
            {
                for (var t = type; t != null; t = t.BaseType)
                    if (t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)?.GetValue(null) is DependencyProperty dp)
                        return dp;
                return null;
            }
        }

        /// <summary>
        /// Paints a query window's status bar through SSMS's QEStatusBarManager (ScriptEditorControl.statusBarManager):
        /// its singleServerBackColor field is what UpdateStatusBar paints. SSMS sets the field again on every
        /// connect, so the colour it sets is remembered and given back when SqlSense stops colouring the window.
        /// </summary>
        private sealed class StatusBarPainter
        {
            private static FieldInfo _colorField;
            private static MethodInfo _update;

            private readonly object _manager;
            private System.Drawing.Color _ssmsColor;
            private System.Drawing.Color? _written;

            private StatusBarPainter(object manager) => _manager = manager;

            public static StatusBarPainter Create(object docView)
            {
                FieldInfo managerField = null;
                for (var t = docView.GetType(); t != null && managerField == null; t = t.BaseType)
                    managerField = t.GetField("statusBarManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                var manager = managerField?.GetValue(docView);
                if (manager == null) return null;
                if (_colorField == null)
                {
                    _colorField = manager.GetType().GetField("singleServerBackColor", BindingFlags.Instance | BindingFlags.NonPublic);
                    _update = manager.GetType().GetMethod("UpdateStatusBar", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                    if (_colorField?.FieldType != typeof(System.Drawing.Color) || _update == null)
                    {
                        _colorField = null;
                        Log.Info($"Status bars can't be coloured: {manager.GetType().FullName} has changed.");
                        return null;
                    }
                }
                return new StatusBarPainter(manager);
            }

            /// <summary>Paints the colour, or with null gives the status bar back its SSMS colour.</summary>
            public void Paint(Color? color)
            {
                var current = (System.Drawing.Color)_colorField.GetValue(_manager);
                if (_written.HasValue && current.ToArgb() != _written.Value.ToArgb()) _written = null; // SSMS reconnected
                if (!_written.HasValue) _ssmsColor = current;

                if (color is Color c)
                {
                    var wanted = System.Drawing.Color.FromArgb(c.R, c.G, c.B); // the status strip can't be translucent
                    if (current.ToArgb() != wanted.ToArgb()) Set(wanted);
                    _written = wanted;
                }
                else if (_written.HasValue)
                {
                    Set(_ssmsColor);
                    _written = null;
                }
            }

            private void Set(System.Drawing.Color color)
            {
                _colorField.SetValue(_manager, color);
                if (_manager.GetType().GetProperty("IsInitialized")?.GetValue(_manager) is bool ready && !ready) return;
                try
                {
                    _update.Invoke(_manager, null);
                }
                catch (TargetInvocationException ex)
                {
                    Log.Error("Colouring the status bar", ex.InnerException ?? ex);
                }
            }
        }
    }
}
