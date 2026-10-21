using System;
using System.Collections.Generic;
using SqlSense.Core.Schema;

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
        Trigger,
    }

    /// <summary>
    /// Attached to an object picked after ALTER (or CREATE OR ALTER) PROCEDURE/FUNCTION/VIEW/TRIGGER in an
    /// otherwise empty batch: the host loads the object's definition and replaces the statement with
    /// <see cref="ModuleScript.Build"/>'s output.
    /// </summary>
    public sealed class ObjectScriptRequest
    {
        public ObjectScriptRequest(DbObject obj, string verb, int statementStart)
        {
            Object = obj;
            Verb = verb;
            StatementStart = statementStart;
        }

        public DbObject Object { get; }

        /// <summary>"ALTER" or "CREATE OR ALTER".</summary>
        public string Verb { get; }

        /// <summary>Document offset of the statement's first keyword; the script replaces from here to the picked name.</summary>
        public int StatementStart { get; }
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

        /// <summary>Set when committing this item should also script the object's definition.</summary>
        public ObjectScriptRequest ScriptRequest { get; set; }

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
            var segments = SegmentScore(typed, candidate);
            if (segments > 0) return segments;
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

        /// <summary>
        /// Word-segment match: the typed text is split into prefixes of the candidate's words, in
        /// order, so "soh" matches "SalesOrderHeader", "isms" matches "is_ms_shipped" and "custid"
        /// matches "CustomerID". Returns 600-700 (fewer skipped words scores higher) or 0.
        /// </summary>
        private static int SegmentScore(string typed, string candidate)
        {
            typed = typed.Replace("_", string.Empty).Replace(" ", string.Empty);
            if (typed.Length < 2) return 0;
            var starts = SegmentStarts(candidate);
            if (starts.Count < 2) return 0;
            var memo = new int?[typed.Length, starts.Count];
            var penalty = BestSegmentMatch(typed, 0, candidate, starts, 0, memo);
            return penalty < 0 ? 0 : 700 - Math.Min(penalty, 100);
        }

        /// <summary>Lowest penalty for matching typed[t..] using segments s.. onwards; -1 = no match.</summary>
        private static int BestSegmentMatch(string typed, int t, string candidate, List<int> starts, int s, int?[,] memo)
        {
            if (t == typed.Length) return 0;
            if (s >= starts.Count) return -1;
            if (memo[t, s] is int cached) return cached;

            var best = -1;
            for (var seg = s; seg < starts.Count; seg++)
            {
                var segEnd = seg + 1 < starts.Count ? starts[seg + 1] : candidate.Length;
                // Skipping words costs; skipping the leading word costs most.
                var skipped = (seg - s) * (t == 0 ? 20 : 5);
                for (var k = 0;
                     t + k < typed.Length && starts[seg] + k < segEnd
                     && char.ToLowerInvariant(candidate[starts[seg] + k]) == char.ToLowerInvariant(typed[t + k]);
                     k++)
                {
                    var rest = BestSegmentMatch(typed, t + k + 1, candidate, starts, seg + 1, memo);
                    if (rest >= 0 && (best < 0 || skipped + rest < best)) best = skipped + rest;
                }
            }
            memo[t, s] = best;
            return best;
        }

        /// <summary>Word starts: after _ or space, lower→upper, the last capital of an acronym (XMLData), digits.</summary>
        private static List<int> SegmentStarts(string candidate)
        {
            var starts = new List<int>();
            for (var i = 0; i < candidate.Length; i++)
            {
                var c = candidate[i];
                if (!char.IsLetterOrDigit(c)) continue;
                var prev = i > 0 ? candidate[i - 1] : '\0';
                var next = i + 1 < candidate.Length ? candidate[i + 1] : '\0';
                if (i == 0 || !char.IsLetterOrDigit(prev)
                    || char.IsUpper(c) && !char.IsUpper(prev)
                    || char.IsUpper(c) && char.IsUpper(prev) && char.IsLower(next)
                    || char.IsDigit(c) && !char.IsDigit(prev))
                    starts.Add(i);
            }
            return starts;
        }
    }
}
