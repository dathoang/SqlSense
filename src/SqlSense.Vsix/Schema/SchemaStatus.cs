using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlSense.Diagnostics;
using SqlSense.Settings;

namespace SqlSense.Schema
{
    /// <summary>
    /// Reports metadata loads in the SSMS status bar: an animated "loading" message while any load runs, then
    /// the result (objects and time, or the error). Called from background threads.
    /// </summary>
    internal static class SchemaStatus
    {
        private static readonly object Gate = new object();
        private static readonly List<string> Running = new List<string>();
        private static bool _animating;

        public static void Started(string label)
        {
            lock (Gate) Running.Add(label);
            Show(null);
        }

        public static void Finished(string label, string result)
        {
            lock (Gate) Running.Remove(label);
            Show(result);
        }

        private static void Show(string result)
        {
            if (!SqlSenseSettings.Current.ShowLoadStatus) return;
            string[] running;
            lock (Gate) running = Running.ToArray();
#pragma warning disable VSTHRD010, VSSDK007
            // The lambda switches to the UI thread before touching the status bar (the analyzers can't see through it).
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    Update(running, result);
                }
                catch (Exception ex)
                {
                    Log.Error("Showing metadata status", ex);
                }
            }).FileAndForget("SqlSense/SchemaStatus");
#pragma warning restore VSTHRD010, VSSDK007
        }

        private static void Update(string[] running, string result)
        {
            if (!ThreadHelper.CheckAccess()) return;
#pragma warning disable VSTHRD010 // guarded by CheckAccess above
            if (!(Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar bar)) return;
            bar.IsFrozen(out var frozen);
            if (frozen != 0) bar.FreezeOutput(0);

            object icon = (short)Constants.SBAI_Synch;
            var loading = running.Length > 0;
            if (loading != _animating)
            {
                bar.Animation(loading ? 1 : 0, ref icon);
                _animating = loading;
            }

            // While others still load, a finished one's result goes first so it isn't lost.
            var text = loading
                ? "SqlSense: loading metadata for " + string.Join(", ", running.Distinct().Take(3)) + (running.Length > 3 ? ", …" : "") + "…"
                : null;
            if (result != null) text = text == null ? "SqlSense: " + result : "SqlSense: " + result + " — still loading " + running.Length;
            if (text != null) bar.SetText(text);
#pragma warning restore VSTHRD010
        }
    }
}
