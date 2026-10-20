using System.ComponentModel.Composition;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace SqlSense.Editor
{
    /// <summary>
    /// Attaches the SqlSense command filter to every editable text view. Whether the view is SQL is
    /// decided per command, because SSMS can assign the T-SQL language service after creation.
    /// </summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class SqlViewListener : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService Adapters { get; set; }

        [Import]
        internal ICompletionBroker CompletionBroker { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = Adapters.GetWpfTextView(textViewAdapter);
            if (view == null || view.Properties.ContainsProperty(typeof(SqlCommandFilter))) return;

            var filter = new SqlCommandFilter(view, CompletionBroker);
            if (textViewAdapter.AddCommandFilter(filter, out var next) == VSConstants.S_OK && next != null)
            {
                filter.Next = next;
                view.Properties.AddProperty(typeof(SqlCommandFilter), filter);
                view.Closed += (s, e) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    textViewAdapter.RemoveCommandFilter(filter);
                    filter.Detach();
                };
            }
        }
    }
}
