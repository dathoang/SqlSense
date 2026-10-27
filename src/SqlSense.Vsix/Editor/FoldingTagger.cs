using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using SqlSense.Core.Folding;
using SqlSense.Diagnostics;
using SqlSense.Settings;

namespace SqlSense.Editor
{
    [Export(typeof(ITaggerProvider))]
    [ContentType("text")]
    [TagType(typeof(IOutliningRegionTag))]
    internal sealed class FoldingTaggerProvider : ITaggerProvider
    {
        // Weak, so closed documents' taggers can go; all are recomputed when the folding options change.
        private readonly List<WeakReference<FoldingTagger>> _taggers = new List<WeakReference<FoldingTagger>>();

        public FoldingTaggerProvider()
        {
            SqlSenseSettings.Changed += (s, e) =>
            {
                List<FoldingTagger> live;
                lock (_taggers)
                {
                    _taggers.RemoveAll(w => !w.TryGetTarget(out _));
                    live = _taggers.Select(w => w.TryGetTarget(out var t) ? t : null).Where(t => t != null).ToList();
                }
                foreach (var tagger in live) tagger.Refresh();
            };
        }

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            if (buffer == null) return null;
            return buffer.Properties.GetOrCreateSingletonProperty(() =>
            {
                var tagger = new FoldingTagger(buffer);
                lock (_taggers) _taggers.Add(new WeakReference<FoldingTagger>(tagger));
                return tagger;
            }) as ITagger<T>;
        }
    }

    /// <summary>
    /// SqlSense's own outlining regions (Core/Folding/FoldingAnalyzer); the tag carries the region so the
    /// folding commands can tell SqlSense regions from the shell's and know their level and kind.
    /// </summary>
    internal sealed class FoldingTag : OutliningRegionTag
    {
        public FoldingTag(FoldRegion region, string hint)
            : base(false, false, region.Header + " …", hint)
        {
            Region = region;
        }

        public FoldRegion Region { get; }
    }

    /// <summary>Recomputes the regions on a background thread shortly after the text stops changing.</summary>
    internal sealed class FoldingTagger : ITagger<IOutliningRegionTag>
    {
        private const int MaxHintLines = 40;

        private readonly ITextBuffer _buffer;
        private readonly DispatcherTimer _timer;
        private ITextSnapshot _snapshot;
        private List<FoldRegion> _regions = new List<FoldRegion>();
        private int _version;

        public FoldingTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
            _timer.Tick += OnTick;
            _buffer.Changed += OnChanged;
            _buffer.ContentTypeChanged += OnChanged;
            _timer.Start();
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public IEnumerable<ITagSpan<IOutliningRegionTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            var snapshot = _snapshot;
            var regions = _regions;
            if (snapshot == null || regions.Count == 0 || spans.Count == 0) yield break;
            var target = spans[0].Snapshot;
            foreach (var r in regions)
            {
                if (r.End > snapshot.Length) continue;
                var span = new SnapshotSpan(snapshot, r.Start, r.Length).TranslateTo(target, SpanTrackingMode.EdgeExclusive);
                if (span.IsEmpty || !spans.IntersectsWith(new NormalizedSnapshotSpanCollection(span))) continue;
                yield return new TagSpan<IOutliningRegionTag>(span, new FoldingTag(r, Hint(snapshot, r)));
            }
        }

        private static string Hint(ITextSnapshot snapshot, FoldRegion r)
        {
            var first = snapshot.GetLineNumberFromPosition(r.Start);
            var last = Math.Min(snapshot.GetLineNumberFromPosition(r.End), first + MaxHintLines - 1);
            var end = snapshot.GetLineFromLineNumber(last).End.Position;
            var text = snapshot.GetText(r.Start, Math.Min(r.End, end) - r.Start);
            return end < r.End ? text + Environment.NewLine + "…" : text;
        }

        private void OnChanged(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Start();
        }

        /// <summary>Recomputes soon, e.g. after the folding options changed. Any thread.</summary>
        public void Refresh()
        {
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OnChanged(this, EventArgs.Empty);
            }).FileAndForget("SqlSense/FoldingRefresh");
#pragma warning restore VSSDK007
        }

        private void OnTick(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _timer.Stop();
            var version = ++_version;
            var settings = SqlSenseSettings.Current;
            if (!settings.Enabled || !settings.Folding || !SqlDocument.IsSql(_buffer))
            {
                Update(_buffer.CurrentSnapshot, new List<FoldRegion>());
                return;
            }

            var snapshot = _buffer.CurrentSnapshot;
            var statements = settings.FoldStatements;
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await TaskScheduler.Default;
                List<FoldRegion> regions;
                try
                {
                    regions = FoldingAnalyzer.GetRegions(snapshot.GetText(), statements);
                }
                catch (Exception ex)
                {
                    Log.Error("Computing folding regions", ex);
                    regions = new List<FoldRegion>();
                }
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (version == _version) Update(snapshot, regions);
            }).FileAndForget("SqlSense/Folding");
#pragma warning restore VSSDK007
        }

        private void Update(ITextSnapshot snapshot, List<FoldRegion> regions)
        {
            if (regions.Count == 0 && _regions.Count == 0) return;
            if (_snapshot != null && Same(_regions, regions) && snapshot.Version.VersionNumber == _snapshot.Version.VersionNumber) return;
            _snapshot = snapshot;
            _regions = regions;
            var current = _buffer.CurrentSnapshot;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(current, 0, current.Length)));
        }

        private static bool Same(List<FoldRegion> a, List<FoldRegion> b) =>
            a.Count == b.Count && a.Zip(b, (x, y) => x.Start == y.Start && x.End == y.End).All(s => s);
    }
}
