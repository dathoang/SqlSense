using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlSense.Diagnostics;
using SqlSense.Settings;

namespace SqlSense.Connections
{
    /// <summary>
    /// Hides the shell's document tab › Set Tab Color commands (VsStd17 SelectNoTabColor, SelectTabColor1-16)
    /// while connection colours own the tab colour: that colour is stored per file, so on a query window it
    /// would fight the per-database colour. A priority command target sees the commands before the shell does.
    /// </summary>
    internal sealed class SetTabColorBlocker : IOleCommandTarget, IDisposable
    {
        private static readonly Guid VsStd17 = VSConstants.VsStd17;
        private const uint First = (uint)VSConstants.VSStd17CmdID.SelectNoTabColor;
        private const uint Last = (uint)VSConstants.VSStd17CmdID.SelectTabColor16;

        private readonly IVsRegisterPriorityCommandTarget _registry;
        private uint _cookie;

        private SetTabColorBlocker(IVsRegisterPriorityCommandTarget registry) => _registry = registry;

        public static SetTabColorBlocker Register(IVsRegisterPriorityCommandTarget registry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (registry == null) return null;
            var blocker = new SetTabColorBlocker(registry);
            if (ErrorHandler.Failed(registry.RegisterPriorityCommandTarget(0, blocker, out blocker._cookie)))
            {
                Log.Info("Couldn't register the Set Tab Color blocker; that menu stays visible.");
                return null;
            }
            return blocker;
        }

        private static bool Blocked(ref Guid group, uint id)
        {
            var s = SqlSenseSettings.Current;
            return group == VsStd17 && id >= First && id <= Last && s.ConnectionColors && s.ColorTabs && s.HideSetTabColor;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            var handled = false;
            for (var i = 0; i < cCmds; i++)
            {
                if (!Blocked(ref pguidCmdGroup, prgCmds[i].cmdID)) continue;
                prgCmds[i].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_INVISIBLE | OLECMDF.OLECMDF_DEFHIDEONCTXTMENU);
                handled = true;
            }
            return handled ? VSConstants.S_OK : (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        // Also swallows the commands when run from a keyboard shortcut or the command window.
        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
            => Blocked(ref pguidCmdGroup, nCmdID) ? VSConstants.S_OK : (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cookie == 0) return;
            _registry.UnregisterPriorityCommandTarget(_cookie);
            _cookie = 0;
        }
    }
}
