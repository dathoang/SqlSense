using System;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Outlining;
using SqlSense.Core.Folding;
using SqlSense.Settings;

namespace SqlSense.Editor
{
    /// <summary>
    /// VS Code style folding commands (Tools › SqlSense › Folding) over SqlSense's own regions (<see cref="FoldingTag"/>).
    /// Unfold All also opens the shell's regions; everything else leaves them alone.
    /// </summary>
    internal static class FoldingActions
    {
        /// <summary>Collapses every region at <paramref name="level"/>, except those containing the caret (as VS Code does).</summary>
        public static void FoldLevel(IOutliningManagerService outlining, IWpfTextView view, int level)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var caret = view.Caret.Position.BufferPosition.Position;
            Collapse(outlining, view, (r, extent) => r.Level == level && !(caret > extent.Start && caret < extent.End),
                $"level {level} regions");
        }

        public static void FoldAll(IOutliningManagerService outlining, IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Collapse(outlining, view, (r, extent) => true, "regions");
        }

        public static void FoldKind(IOutliningManagerService outlining, IWpfTextView view, FoldKind kind, string what)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Collapse(outlining, view, (r, extent) => r.Kind == kind, what);
        }

        public static void UnfoldAll(IOutliningManagerService outlining, IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetManager(outlining, view, out var manager)) return;
            manager.ExpandAll(Whole(view), c => true);
        }

        /// <summary>The innermost region at the caret and every region inside it.</summary>
        public static void FoldRecursively(IOutliningManagerService outlining, IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetManager(outlining, view, out var manager)) return;
            var target = RegionAtCaret(manager, view);
            if (target == null)
            {
                EditorActions.Status("SqlSense: no foldable region at the caret.");
                return;
            }
            var span = target.Extent.GetSpan(view.TextSnapshot);
            manager.CollapseAll(span, c => c.Tag is FoldingTag && span.Contains(c.Extent.GetSpan(span.Snapshot)));
        }

        /// <summary>The outermost collapsed region on the caret line (else the innermost region at the caret) and everything inside it.</summary>
        public static void UnfoldRecursively(IOutliningManagerService outlining, IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetManager(outlining, view, out var manager)) return;
            var snapshot = view.TextSnapshot;
            var line = view.Caret.Position.BufferPosition.GetContainingLine().Extent;
            var collapsed = manager.GetCollapsedRegions(line)
                .Select(c => c.Extent.GetSpan(snapshot))
                .OrderByDescending(s => s.Length)
                .FirstOrDefault();
            var span = collapsed.Snapshot != null ? collapsed : RegionAtCaret(manager, view)?.Extent.GetSpan(snapshot);
            if (span == null)
            {
                EditorActions.Status("SqlSense: no folded region at the caret.");
                return;
            }
            manager.ExpandAll(span.Value, c => span.Value.Contains(c.Extent.GetSpan(snapshot)));
        }

        /// <summary>Collapses the SqlSense regions matching <paramref name="predicate"/> (given each region and its current extent).</summary>
        private static void Collapse(IOutliningManagerService outlining, IWpfTextView view,
            Func<FoldRegion, SnapshotSpan, bool> predicate, string what)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetManager(outlining, view, out var manager)) return;
            var snapshot = view.TextSnapshot;
            var count = manager.CollapseAll(Whole(view),
                c => c.Tag is FoldingTag tag && predicate(tag.Region, c.Extent.GetSpan(snapshot))).Count();
            if (count == 0) EditorActions.Status($"SqlSense: no {what} to fold.");
        }

        /// <summary>Innermost SqlSense region containing the caret; if none, the largest one starting on the caret line.</summary>
        private static ICollapsible RegionAtCaret(IOutliningManager manager, ITextView view)
        {
            var caret = view.Caret.Position.BufferPosition;
            var line = caret.GetContainingLine().Extent;
            var regions = manager.GetAllRegions(line).Where(c => c.Tag is FoldingTag).ToList();
            return regions
                       .Where(c => Contains(c.Extent.GetSpan(caret.Snapshot), caret))
                       .OrderBy(c => c.Extent.GetSpan(caret.Snapshot).Length)
                       .FirstOrDefault()
                   ?? regions
                       .Where(c => line.Contains(c.Extent.GetStartPoint(caret.Snapshot)))
                       .OrderByDescending(c => c.Extent.GetSpan(caret.Snapshot).Length)
                       .FirstOrDefault();
        }

        private static bool Contains(SnapshotSpan span, SnapshotPoint point) =>
            point.Position >= span.Start.Position && point.Position <= span.End.Position;

        private static bool TryGetManager(IOutliningManagerService outlining, IWpfTextView view, out IOutliningManager manager)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            manager = SqlSenseSettings.Current.Folding ? outlining?.GetOutliningManager(view) : null;
            if (manager == null)
                EditorActions.Status(SqlSenseSettings.Current.Folding
                    ? "SqlSense: outlining is not available in this window."
                    : "SqlSense: code folding is off (Tools › Options › SqlSense › General › Code folding).");
            return manager != null;
        }

        private static SnapshotSpan Whole(ITextView view)
        {
            var snapshot = view.TextSnapshot;
            return new SnapshotSpan(snapshot, 0, snapshot.Length);
        }
    }
}
