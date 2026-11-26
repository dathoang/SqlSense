using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlSense.Core.Completion
{
    /// <summary>
    /// Suggestions the user picked recently. A picked item moves up among items of equal match quality (it never
    /// passes join conditions or context-ranked columns). Thread-safe; kept in memory for the SSMS session.
    /// </summary>
    public sealed class CompletionHistory
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, long> _lastUse = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private long _clock;

        public CompletionHistory(int capacity = 300)
        {
            Capacity = Math.Max(1, capacity);
        }

        public int Capacity { get; }

        public int Count
        {
            get { lock (_lock) return _lastUse.Count; }
        }

        public void Record(CompletionItem item)
        {
            if (item == null || !Ranked(item.Kind)) return;
            lock (_lock)
            {
                _lastUse[Key(item)] = ++_clock;
                if (_lastUse.Count <= Capacity) return;
                var oldest = _lastUse.OrderBy(p => p.Value).First().Key;
                _lastUse.Remove(oldest);
            }
        }

        /// <summary>How much to lower the item's priority: 3 for the last 20 picks, 2 for older ones, 0 if never picked.</summary>
        public int Boost(CompletionItem item)
        {
            if (item == null || !Ranked(item.Kind)) return 0;
            lock (_lock)
            {
                if (!_lastUse.TryGetValue(Key(item), out var used)) return 0;
                return _clock - used < 20 ? 3 : 2;
            }
        }

        public void Clear()
        {
            lock (_lock) _lastUse.Clear();
        }

        private static string Key(CompletionItem item) => item.Kind + ":" + item.DisplayText;

        /// <summary>Generated items (join conditions, snippets with dates…) keep their own order.</summary>
        private static bool Ranked(CompletionItemKind kind) =>
            kind != CompletionItemKind.JoinCondition && kind != CompletionItemKind.File;
    }
}
