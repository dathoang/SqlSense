using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SqlSense.Core.Analysis;
using SqlSense.Diagnostics;
using SqlSense.Settings;

namespace SqlSense.Editor
{
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [TagType(typeof(TextMarkerTag))]
    internal sealed class InsertPairTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || textView.TextBuffer != buffer) return null;
            return textView.Properties.GetOrCreateSingletonProperty(() => new InsertPairTagger(textView)) as ITagger<T>;
        }
    }

    /// <summary>Background colour of the paired INSERT column and value (Fonts and Colors › "SqlSense INSERT column/value").</summary>
    [Export(typeof(EditorFormatDefinition))]
    [Name(InsertPairTagger.FormatName)]
    [UserVisible(true)]
    internal sealed class InsertPairFormat : MarkerFormatDefinition
    {
        public InsertPairFormat()
        {
            DisplayName = "SqlSense INSERT column/value";
            var fill = new SolidColorBrush(Color.FromArgb(0x55, 0x3C, 0x8C, 0xE6));
            fill.Freeze();
            var border = new SolidColorBrush(Color.FromArgb(0xA0, 0x3C, 0x8C, 0xE6));
            border.Freeze();
            var pen = new Pen(border, 1.0);
            pen.Freeze();
            Fill = fill;
            Border = pen;
            ZOrder = 5;
        }
    }

    /// <summary>
    /// With the caret on a column of an INSERT column list, marks the matching values (and the other
    /// way round). Recomputed shortly after the caret or text stops changing, never per keystroke.
    /// </summary>
    internal sealed class InsertPairTagger : ITagger<TextMarkerTag>
    {
        public const string FormatName = "MarkerFormatDefinition/SqlSenseInsertPair";
        private static readonly TextMarkerTag Tag = new TextMarkerTag(FormatName);

        private readonly ITextView _view;
        private readonly DispatcherTimer _timer;
        private NormalizedSnapshotSpanCollection _spans;

        public InsertPairTagger(ITextView view)
        {
            _view = view;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
            _timer.Tick += OnTick;
            _view.Caret.PositionChanged += OnChanged;
            _view.TextBuffer.Changed += OnChanged;
            _view.Closed += OnClosed;
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public IEnumerable<ITagSpan<TextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            var current = _spans;
            if (current == null || current.Count == 0 || spans.Count == 0) yield break;
            var snapshot = spans[0].Snapshot;
            foreach (var span in current)
            {
                var translated = span.TranslateTo(snapshot, SpanTrackingMode.EdgeExclusive);
                if (spans.IntersectsWith(new NormalizedSnapshotSpanCollection(translated)))
                    yield return new TagSpan<TextMarkerTag>(translated, Tag);
            }
        }

        private void OnChanged(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _timer.Stop();
            if (_view.IsClosed) return;
            try
            {
                Update(Compute());
            }
            catch (Exception ex)
            {
                Log.Error("Highlighting INSERT columns", ex);
                Update(null);
            }
        }

        private NormalizedSnapshotSpanCollection Compute()
        {
            var settings = SqlSenseSettings.Current;
            if (!settings.Enabled || !settings.HighlightInsertPairs || !SqlDocument.IsSql(_view.TextBuffer)) return null;
            if (!_view.Selection.IsEmpty) return null;

            var caret = _view.Caret.Position.BufferPosition;
            var snapshot = caret.Snapshot;
            var ranges = InsertPairs.Find(snapshot.GetText(), caret.Position);
            // The item alone (no counterpart) is not worth marking.
            if (ranges.Count < 2) return null;
            return new NormalizedSnapshotSpanCollection(
                ranges.Select(r => new SnapshotSpan(snapshot, r.Start, r.Length)));
        }

        private void Update(NormalizedSnapshotSpanCollection spans)
        {
            var old = _spans;
            if ((old == null || old.Count == 0) && (spans == null || spans.Count == 0)) return;
            _spans = spans;
            var snapshot = _view.TextBuffer.CurrentSnapshot;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _view.Caret.PositionChanged -= OnChanged;
            _view.TextBuffer.Changed -= OnChanged;
            _view.Closed -= OnClosed;
        }
    }
}
