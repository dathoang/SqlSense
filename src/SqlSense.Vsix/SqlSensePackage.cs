using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using SqlSense.Commands;
using SqlSense.Diagnostics;
using SqlSense.Settings;
using Task = System.Threading.Tasks.Task;

namespace SqlSense
{
    /// <summary>
    /// Package entry point: options page and menu commands. Editor features are MEF parts and work
    /// as soon as a query window opens; the package loads in the background at startup so the
    /// saved options apply from the first keystroke.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("SqlSense for SSMS 22", "T-SQL suggestions, autocomplete and formatting.", "1.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(OptionsPage), "SqlSense", "General", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.PackageString)]
    public sealed class SqlSensePackage : AsyncPackage
    {
        internal static SqlSensePackage Instance { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            Instance = this;
            Options.Publish();
            await SqlSenseCommands.InitializeAsync(this);
            Log.Info($"SqlSense {typeof(SqlSensePackage).Assembly.GetName().Version} loaded. Enabled: {SqlSenseSettings.Current.Enabled}.");
        }

        internal OptionsPage Options => (OptionsPage)GetDialogPage(typeof(OptionsPage));
    }
}
