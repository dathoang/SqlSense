using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlSense.Core.Completion;
using SqlSense.Diagnostics;
using SqlSense.Editor;
using SqlSense.Schema;
using SqlSense.Settings;

namespace SqlSense.IntelliSense
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [ContentType("text")]
    [Name("SqlSense quick info")]
    [Order(Before = "default")]
    internal sealed class SqlQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
            textBuffer.Properties.GetOrCreateSingletonProperty(() => new SqlQuickInfoSource(textBuffer));
    }

    /// <summary>Tooltip for the table, column, routine, variable or principal under the mouse.</summary>
    internal sealed class SqlQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _buffer;

        public SqlQuickInfoSource(ITextBuffer buffer)
        {
            _buffer = buffer;
        }

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            var settings = SqlSenseSettings.Current;
            if (!settings.Enabled || !settings.QuickInfo) return null;
            var snapshot = _buffer.CurrentSnapshot;
            var trigger = session.GetTriggerPoint(snapshot);
            if (trigger == null) return null;

            // The SSMS connection is read on the UI thread, as for completion.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (!SqlDocument.IsSql(_buffer)) return null;
            try
            {
                var text = snapshot.GetText();
                var position = trigger.Value.Position;
                var schema = SchemaService.Get(SsmsConnection.GetActive(), SqlDocument.DatabaseFromUse(text, position));
                var info = QuickInfoProvider.Get(text, position, schema);
                if (info == null) return null;
                var span = snapshot.CreateTrackingSpan(info.Start, info.Length, SpanTrackingMode.EdgeInclusive);
                return new QuickInfoItem(span, Content(info));
            }
            catch (Exception ex)
            {
                Log.Error("Building quick info", ex);
                return null;
            }
        }

        private static object Content(QuickInfo info)
        {
            var title = new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Keyword, info.Kind),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.WhiteSpace, " "),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, info.Name));
            if (info.Lines.Count == 0) return title;
            var lines = info.Lines.Select(l => (object)new ClassifiedTextElement(new ClassifiedTextRun(PredefinedClassificationTypeNames.Other, l)));
            return new ContainerElement(ContainerElementStyle.Stacked, new object[] { title }.Concat(lines));
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Opens a quick info session on mouse hover in SQL views. SSMS's legacy T-SQL language service
    /// doesn't start the editor's own sessions, so without this the source above would never be asked.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SqlQuickInfoTrigger : IWpfTextViewCreationListener
    {
        [Import]
        internal IAsyncQuickInfoBroker Broker { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            textView.MouseHover += OnMouseHover;
            textView.Closed += (s, e) => textView.MouseHover -= OnMouseHover;
        }

        private void OnMouseHover(object sender, MouseHoverEventArgs e)
        {
            var settings = SqlSenseSettings.Current;
            if (!settings.Enabled || !settings.QuickInfo || !(sender is ITextView view)) return;
            if (!SqlDocument.IsSql(view.TextBuffer) || Broker.IsQuickInfoActive(view)) return;
            var point = e.TextPosition.GetPoint(view.TextBuffer, PositionAffinity.Predecessor);
            if (point == null) return;
            var trackingPoint = point.Value.Snapshot.CreateTrackingPoint(point.Value.Position, PointTrackingMode.Positive);
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await Broker.TriggerQuickInfoAsync(view, trackingPoint, QuickInfoSessionOptions.TrackMouse);
            }).FileAndForget("SqlSense/QuickInfo");
#pragma warning restore VSSDK007
        }
    }
}
