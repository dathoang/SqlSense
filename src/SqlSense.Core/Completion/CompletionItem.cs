using System;

namespace SqlSense.Core.Completion
{
    public enum CompletionItemKind
    {
        Keyword,
        Snippet,
        JoinCondition,
        Column,
        Alias,
        Table,
        View,
        TableFunction,
        ScalarFunction,
        Procedure,
        Synonym,
        Schema,
        Database,
        Variable,
        Function,
        DataType,
        LocalTable,
    }

    public sealed class CompletionItem
    {
        public CompletionItem(string displayText, string insertText, CompletionItemKind kind, string description = null)
        {
            DisplayText = displayText;
            InsertText = insertText;
            Kind = kind;
            Description = description ?? string.Empty;
        }

        /// <summary>Text shown in the list and used for filtering.</summary>
        public string DisplayText { get; }

        /// <summary>Text that replaces the typed word on commit. May span lines.</summary>
        public string InsertText { get; set; }

        public CompletionItemKind Kind { get; }
        public string Description { get; set; }

        /// <summary>Lower sorts first among equally good matches.</summary>
        public int Priority { get; set; }

        /// <summary>Caret position inside <see cref="InsertText"/> after commit; -1 = end.</summary>
        public int CaretOffset { get; set; } = -1;

        public override string ToString() => $"{Kind}:{DisplayText}";
    }

    /// <summary>Scores how well a typed prefix matches a candidate (higher is better, 0 = no match).</summary>
    public static class CompletionMatcher
    {
        public static int Score(string typed, string candidate)
        {
            if (string.IsNullOrEmpty(typed)) return 1;
            typed = Strip(typed);
            candidate = Strip(candidate);
            if (typed.Length == 0) return 1;
            if (candidate.Equals(typed, StringComparison.OrdinalIgnoreCase)) return 1000;
            if (candidate.StartsWith(typed, StringComparison.Ordinal)) return 900;
            if (candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return 800;
            if (MatchesInitials(typed, candidate)) return 700;
            var idx = candidate.IndexOf(typed, StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return 500 - Math.Min(idx, 100);
            return 0;
        }

        private static string Strip(string s)
        {
            s = s.TrimStart('[');
            var dot = s.LastIndexOf('.');
            return dot >= 0 ? s.Substring(dot + 1) : s;
        }

        /// <summary>"soh" matches "SalesOrderHeader" and "sales_order_header".</summary>
        private static bool MatchesInitials(string typed, string candidate)
        {
            var t = 0;
            for (var i = 0; i < candidate.Length && t < typed.Length; i++)
            {
                var c = candidate[i];
                var isStart = i == 0 || char.IsUpper(c) && !char.IsUpper(candidate[i - 1])
                                     || (candidate[i - 1] == '_' || candidate[i - 1] == ' ') && char.IsLetterOrDigit(c);
                if (isStart && char.ToLowerInvariant(c) == char.ToLowerInvariant(typed[t])) t++;
            }
            return t == typed.Length && typed.Length > 1;
        }
    }
}
