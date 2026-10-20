using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SqlSense.Diagnostics
{
    /// <summary>Writes to a "SqlSense" pane in the Output window. Callable from any thread.</summary>
    internal static class Log
    {
        public static void Info(string message) => Write("INFO ", message);

        /// <summary>Suggestion-list diagnostics: written to the log file only, never to the Output pane.</summary>
        public static void Trace(string message) => AppendToFile($"{DateTime.Now:HH:mm:ss.fff} TRACE {message}{Environment.NewLine}");

        public static void Error(string message, Exception ex = null) =>
            Write("ERROR", ex == null ? message : message + ": " + ex);

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss} {level} {message}{Environment.NewLine}";
            if (level == "ERROR") AppendToFile(line);
            // Fire-and-forget: the lambda switches to the UI thread before touching the pane, so
            // logging is safe from any thread (the analyzers can't see through the switch).
#pragma warning disable VSTHRD010, VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                LogPane.Get()?.OutputStringThreadSafe(line);
            }).FileAndForget("SqlSense/Log");
#pragma warning restore VSTHRD010, VSSDK007
        }

        /// <summary>%LOCALAPPDATA%\SqlSense\sqlsense.log, so errors survive closing SSMS.</summary>
        public static readonly string FilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlSense", "sqlsense.log");

        private static readonly object FileLock = new object();

        private static void AppendToFile(string line)
        {
            try
            {
                lock (FileLock)
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                    System.IO.File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd ") + line);
                }
            }
            catch (Exception)
            {
                // Logging must never break the editor.
            }
        }
    }

    internal static class LogPane
    {
        private static readonly Guid PaneGuid = new Guid("5e0c3b2a-91d4-4f6e-a7b8-3c2d1e0f9a8b");
        private static IVsOutputWindowPane _pane;

        public static void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Get()?.Activate();
        }

        /// <summary>The pane, or null when called off the UI thread.</summary>
        public static IVsOutputWindowPane Get()
        {
            if (!ThreadHelper.CheckAccess()) return null;
#pragma warning disable VSTHRD010 // guarded by CheckAccess above
            if (_pane != null) return _pane;
            if (!(Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow output)) return null;
            var guid = PaneGuid;
            if (output.GetPane(ref guid, out _pane) != VSConstants.S_OK || _pane == null)
            {
                output.CreatePane(ref guid, "SqlSense", 1, 0);
                output.GetPane(ref guid, out _pane);
            }
            return _pane;
#pragma warning restore VSTHRD010
        }
    }
}
