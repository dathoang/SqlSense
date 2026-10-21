using System;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlSense.Core.Formatting;
using SqlSense.Diagnostics;

namespace SqlSense.Settings
{
    /// <summary>Tools › SqlSense › Import SQL Prompt Style…: copies a SQL Prompt .json style into the formatter options.</summary>
    internal static class StyleImport
    {
        public static void Run(IServiceProvider services, SettingsStore options)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (options == null)
            {
                Show(services, "Unified Settings is not available, so the style can't be saved.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import SQL Prompt formatting style",
                Filter = "SQL Prompt style (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = DefaultFolder(),
            };
            if (dialog.ShowDialog() != true) return;

            StyleImportResult result;
            try
            {
                result = SqlPromptStyle.Import(File.ReadAllText(dialog.FileName), options.ReadFormatOptions());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                Log.Error("Importing " + dialog.FileName + " failed", ex);
                Show(services, "Couldn't import the style:\n" + ex.Message, OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var error = options.WriteFormatOptions(result.Options);
            if (error != null)
            {
                Show(services, "Couldn't save the imported style:\n" + error, OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            Log.Info("Imported SQL Prompt style " + dialog.FileName);
            foreach (var line in result.Applied) Log.Info("  applied: " + line);
            foreach (var line in result.Ignored) Log.Info("  not supported: " + line);

            var message = $"Imported {result.Applied.Count} setting(s) from {Path.GetFileName(dialog.FileName)}.";
            if (result.Ignored.Count > 0)
                message += $"\n\n{result.Ignored.Count} setting(s) have no SqlSense equivalent yet:\n  " + string.Join("\n  ", result.Ignored);
            message += "\n\nDetails are in Tools › SqlSense › Show Log; the values are in Tools › Options › SqlSense › Formatting.";
            Show(services, message, OLEMSGICON.OLEMSGICON_INFO);
        }

        private static string DefaultFolder()
        {
            var redGate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Red Gate");
            try
            {
                if (Directory.Exists(redGate))
                {
                    // Newest SQL Prompt version that has a Styles folder.
                    string best = null;
                    foreach (var dir in Directory.GetDirectories(redGate, "SQL Prompt*"))
                    {
                        var styles = Path.Combine(dir, "Styles");
                        if (Directory.Exists(styles) && (best == null || string.CompareOrdinal(styles, best) > 0)) best = styles;
                    }
                    if (best != null) return best;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("Looking for SQL Prompt styles failed", ex);
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private static void Show(IServiceProvider services, string message, OLEMSGICON icon)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(services, message, "SqlSense", icon, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
