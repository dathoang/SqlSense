using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlSense.Commands;
using SqlSense.Connections;
using SqlSense.Diagnostics;
using SqlSense.Settings;
using Task = System.Threading.Tasks.Task;

namespace SqlSense
{
    /// <summary>
    /// Package entry point: options (Unified Settings) and menu commands. Editor features are MEF parts and work
    /// as soon as a query window opens; the package loads in the background at startup so the
    /// saved options apply from the first keystroke.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("SqlSense for SSMS 22", "T-SQL suggestions, autocomplete and formatting.", "1.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideSettingsManifest(PackageRelativeManifestFile = @"Settings\registration.json")]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.PackageString)]
    public sealed class SqlSensePackage : AsyncPackage
    {
        internal static SqlSensePackage Instance { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            Instance = this;
            Options = SettingsStore.Create(this);
            await SqlSenseCommands.InitializeAsync(this);
            var shell = await GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
            var componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            ConnectionColors = ConnectionColorService.Create(shell, componentModel?.GetService<IVsEditorAdaptersFactoryService>());
            Log.Info($"SqlSense {typeof(SqlSensePackage).Assembly.GetName().Version} loaded. Enabled: {SqlSenseSettings.Current.Enabled}.");
        }

        /// <summary>Null when Unified Settings is unavailable; <see cref="SqlSenseSettings.Current"/> then keeps the defaults.</summary>
        internal SettingsStore Options { get; private set; }

        /// <summary>Null when the shell or editor services were unavailable.</summary>
        internal ConnectionColorService ConnectionColors { get; private set; }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing)
            {
                Options?.Dispose();
                ConnectionColors?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
