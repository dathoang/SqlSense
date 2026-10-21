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
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using SqlSense.Core.Connections;
using SqlSense.Diagnostics;
using SqlSense.Settings;

namespace SqlSense.Connections
{
    /// <summary>
    /// Colours each SSMS query window by its server and database: the document tab (through the VS shell's
    /// own tab colouring, DocumentView.IsTabGroupColorized / TabAccentBrush) and a frame around the editor
    /// (<see cref="ConnectionColorMargin"/>). The window's DocView is SSMS's SqlScriptEditorControl, whose
    /// ConnectionInfoList[0] is the UIConnectionInfo; SSMS rewrites its DATABASE option when the database
    /// changes (drop-down or USE). None of this raises a public event, so open windows are re-read every second.
    /// </summary>
    internal sealed class ConnectionColorService : IDisposable
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly object BufferKey = typeof(ConnectionColorService);

        private readonly IVsUIShell _shell;
        private readonly IVsEditorAdaptersFactoryService _adapters;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<DependencyObject, TabPainter> _tabs = new Dictionary<DependencyObject, TabPainter>();
        private readonly HashSet<ITextBuffer> _buffers = new HashSet<ITextBuffer>();

        private SqlSenseSettings _settings;
        private ConnectionColorRules _rules = ConnectionColorRules.Empty;
        private string _rulesPath;
        private DateTime _rulesStamp;
        private bool _loggedTabPreference;

        /// <summary>Raised on the UI thread when any editor's border colour or the border settings change.</summary>
        public static event EventHandler Changed;

        /// <summary>The border colour of the query window showing this buffer, or null.</summary>
        public static Color? BorderColorOf(ITextBuffer buffer)
            => buffer != null && buffer.Properties.TryGetProperty(BufferKey, out Color color) ? color : (Color?)null;

        private ConnectionColorService(IVsUIShell shell, IVsEditorAdaptersFactoryService adapters)
        {
            _shell = shell;
            _adapters = adapters;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Refresh();
            };
            _timer.Start();
        }

        public static ConnectionColorService Create(IVsUIShell shell, IVsEditorAdaptersFactoryService adapters)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (shell == null || adapters == null)
            {
                Log.Info("Connection colours are off: shell or editor adapter service unavailable.");
                return null;
            }
            var service = new ConnectionColorService(shell, adapters);
            service.Refresh();
            return service;
        }

        /// <summary>Re-reads every open query window and repaints what changed.</summary>
        public void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var settings = SqlSenseSettings.Current;
                var settingsChanged = !ReferenceEquals(settings, _settings);
                _settings = settings;
                var rulesChanged = LoadRules(settings);

                var seenTabs = new HashSet<DependencyObject>();
                var seenBuffers = new HashSet<ITextBuffer>();
                var bordersChanged = settingsChanged || rulesChanged;

                if (settings.ConnectionColors && ErrorHandler.Succeeded(_shell.GetDocumentWindowEnum(out var frames)) && frames != null)
                {
                    var one = new IVsWindowFrame[1];
                    while (frames.Next(1, one, out var fetched) == VSConstants.S_OK && fetched == 1)
                    {
                        var frame = one[0];
                        if (ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView))) continue;
                        if (!TryReadConnection(docView, out var server, out var database, out var ssmsColor)) continue;

                        var color = Resolve(server, database, ssmsColor);
                        if (settings.ColorTabs && color.HasValue) PaintTab(frame, color.Value, seenTabs);

                        if (settings.ColorBorder && color.HasValue && BufferOf(frame, docView) is ITextBuffer buffer)
                        {
                            seenBuffers.Add(buffer);
                            if (BorderColorOf(buffer) != color)
                            {
                                buffer.Properties[BufferKey] = color.Value;
                                bordersChanged = true;
                            }
                        }
                    }
                }

                foreach (var view in new List<DependencyObject>(_tabs.Keys))
                {
                    if (seenTabs.Contains(view)) continue;
                    _tabs[view].Release();
                    _tabs.Remove(view);
                }
                foreach (var buffer in _buffers)
                {
                    if (seenBuffers.Contains(buffer)) continue;
                    buffer.Properties.RemoveProperty(BufferKey);
                    bordersChanged = true;
                }
                _buffers.Clear();
                _buffers.UnionWith(seenBuffers);

                if (bordersChanged) Changed?.Invoke(null, EventArgs.Empty);
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

        /// <summary>Reloads the rules file when its path or timestamp changed. True when the rules changed.</summary>
        private bool LoadRules(SqlSenseSettings settings)
        {
            var path = settings.ConnectionColorsFile;
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (path == _rulesPath && stamp == _rulesStamp) return false;
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
                return false;
            }
            Log.Info($"Connection colours: {_rules.Rules.Count} rule(s) from {path}.");
            foreach (var error in _rules.Errors) Log.Info($"{Path.GetFileName(path)} {error}");
            return true;
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

        /// <summary>The editor buffer of a query window: its DocData, or SSMS's ScriptEditorControl.DocData.</summary>
        private ITextBuffer BufferOf(IVsWindowFrame frame, object docView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out var frameDocData);
            var controlDocData = docView.GetType().GetProperty("DocData", Any)?.GetValue(docView);
            foreach (var docData in new[] { frameDocData, controlDocData })
            {
                var vsBuffer = docData as IVsTextBuffer;
                if (vsBuffer == null && docData is IVsTextBufferProvider provider && provider.GetTextBuffer(out var lines) == VSConstants.S_OK)
                    vsBuffer = lines;
                if (vsBuffer != null && _adapters.GetDocumentBuffer(vsBuffer) is ITextBuffer buffer) return buffer;
            }
            return null;
        }

        private void PaintTab(IVsWindowFrame frame, Color color, HashSet<DependencyObject> seen)
        {
            if (!(frame.GetType().GetProperty("FrameView", Any)?.GetValue(frame) is DependencyObject view)) return;
            if (!_tabs.TryGetValue(view, out var painter))
            {
                painter = TabPainter.Create(view);
                if (painter == null) return;
                _tabs.Add(view, painter);
                if (!_loggedTabPreference)
                {
                    _loggedTabPreference = true;
                    if (TabPainter.ColorizeDocumentTabs() == false)
                        Log.Info("Document tabs are not coloured until Tools › Options › Environment › Tabs and Windows › Colorize document tabs is on.");
                }
            }
            seen.Add(view);
            painter.Paint(color);
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _timer.Stop();
            foreach (var painter in _tabs.Values) painter.Release();
            _tabs.Clear();
            foreach (var buffer in _buffers) buffer.Properties.RemoveProperty(BufferKey);
            _buffers.Clear();
            Changed?.Invoke(null, EventArgs.Empty);
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
            public static bool? ColorizeDocumentTabs()
            {
                try
                {
                    var viewManager = Type.GetType("Microsoft.VisualStudio.PlatformUI.Shell.ViewManager, Microsoft.VisualStudio.Shell.ViewManager");
                    var instance = viewManager?.GetProperty("Instance", Any)?.GetValue(null);
                    var preferences = instance?.GetType().GetProperty("Preferences", Any)?.GetValue(instance);
                    return preferences?.GetType().GetProperty("ColorizeDocumentTabs", Any)?.GetValue(preferences) as bool?;
                }
                catch (Exception)
                {
                    return null;
                }
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
    }
}
