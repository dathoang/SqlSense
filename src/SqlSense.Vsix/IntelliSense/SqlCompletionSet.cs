using System.Collections.Generic;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using SqlSense.Core.Completion;

namespace SqlSense.IntelliSense
{
    /// <summary>
    /// Completion set that filters with SqlSense matching (prefix, then initials like "soh" for
    /// SalesOrderHeader, then substring) instead of the editor's plain prefix match.
    /// </summary>
    internal sealed class SqlCompletionSet : CompletionSet
    {
        private readonly FilteredObservableCollection<Completion> _filtered;

        public SqlCompletionSet(string moniker, string displayName, ITrackingSpan applicableTo, IEnumerable<Completion> completions)
            : base(moniker, displayName, applicableTo, completions, null)
        {
            _filtered = new FilteredObservableCollection<Completion>(WritableCompletions);
        }

        public override IList<Completion> Completions => _filtered;

        /// <summary>All suggestions, before filtering.</summary>
        public int TotalCount => WritableCompletions.Count;

        private string Typed => ApplicableTo.GetText(ApplicableTo.TextBuffer.CurrentSnapshot);

        private string _filteredFor;

        public override void Filter()
        {
            var typed = Typed;
            // SqlCommandFilter filters first, then the session filters again: skip the repeat.
            if (typed == _filteredFor) return;
            _filteredFor = typed;
            if (string.IsNullOrEmpty(typed)) _filtered.StopFiltering();
            else _filtered.Filter(c => CompletionMatcher.Score(typed, c.DisplayText) > 0);
        }

        public override void SelectBestMatch()
        {
            var typed = Typed;
            Completion best = null;
            var bestScore = 0;
            var count = 0;
            foreach (var c in _filtered)
            {
                count++;
                var score = CompletionMatcher.Score(typed, c.DisplayText);
                // Prefix matches are equally good; the list order (priority) breaks ties.
                var rank = score >= 800 ? 800 : score;
                if (rank > bestScore)
                {
                    best = c;
                    bestScore = rank;
                }
            }
            // With nothing typed the top item is only highlighted, so Enter still inserts a new line.
            SelectionStatus = best == null
                ? new CompletionSelectionStatus(null, false, false)
                : new CompletionSelectionStatus(best, isSelected: typed.Length > 0, isUnique: count == 1);
        }
    }
}
