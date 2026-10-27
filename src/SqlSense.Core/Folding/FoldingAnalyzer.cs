using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SqlSense.Core.Analysis;
using SqlSense.Core.Lexing;

namespace SqlSense.Core.Folding
{
    public enum FoldKind
    {
        /// <summary>BEGIN … END (TRY/CATCH included) and CASE … END.</summary>
        Block,

        /// <summary><c>-- #region</c> … <c>-- #endregion</c>.</summary>
        Region,

        /// <summary>A data or DDL statement: SELECT, INSERT, …, CREATE TABLE.</summary>
        Statement,

        /// <summary>CREATE/ALTER PROCEDURE, FUNCTION, VIEW or TRIGGER, up to the end of its batch.</summary>
        Module,

        /// <summary>( … ): subqueries, column and value lists.</summary>
        Parentheses,

        /// <summary>A /* */ comment, or consecutive lines that hold only -- comments.</summary>
        Comment,
    }

    /// <summary>A collapsible range of the script; <see cref="Level"/> is 1 for outermost regions.</summary>
    public sealed class FoldRegion
    {
        public FoldRegion(FoldKind kind, int start, int end, string header)
        {
            Kind = kind;
            Start = start;
            End = end;
            Header = header;
        }

        public FoldKind Kind { get; }
        public int Start { get; }
        public int End { get; }
        public int Length => End - Start;

        /// <summary>The region's text up to the end of its first line, trimmed.</summary>
        public string Header { get; }

        /// <summary>Nesting depth among all returned regions: 1 = not inside another region.</summary>
        public int Level { get; internal set; }

        public override string ToString() => $"{Kind}@{Start}..{End} L{Level}";
    }

    /// <summary>
    /// Folding regions from the token stream (no parse, so it works on incomplete scripts), like VS Code's:
    /// each region has a nesting level for the Fold Level N commands. Only regions spanning several lines are returned.
    /// </summary>
    public static class FoldingAnalyzer
    {
        private static readonly Regex RegionStart = new Regex(@"^--\s*#region\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RegionEnd = new Regex(@"^--\s*#endregion\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // BEGIN followed by these is a statement, not a block with a matching END.
        private static readonly HashSet<string> NonBlockBegin = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TRAN", "TRANSACTION", "DISTRIBUTED", "DIALOG", "CONVERSATION",
        };

        // Statements folded on their own; control flow (IF, WHILE, BEGIN) folds through its blocks.
        private static readonly HashSet<string> FoldedStatements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "EXEC", "EXECUTE", "DECLARE", "CREATE", "ALTER",
        };

        private static readonly HashSet<string> ModuleKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PROC", "PROCEDURE", "FUNCTION", "VIEW", "TRIGGER",
        };

        /// <param name="statements">Also fold multi-line statements (SELECT, INSERT, …).</param>
        public static List<FoldRegion> GetRegions(string text, bool statements = true)
        {
            var result = new List<FoldRegion>();
            if (string.IsNullOrEmpty(text)) return result;

            var tokens = SqlLexer.Tokenize(text);
            AddBlocksAndRegions(result, text, tokens);
            AddComments(result, text, tokens);
            foreach (var batch in Batches(tokens))
            {
                var module = AddModule(result, text, batch);
                if (statements) AddStatements(result, text, batch, module);
            }

            return Arrange(result);
        }

        /// <summary>BEGIN/CASE … END, ( … ) and #region comments, matched with stacks.</summary>
        private static void AddBlocksAndRegions(List<FoldRegion> result, string text, List<SqlToken> tokens)
        {
            // Code blocks: true = BEGIN, false = CASE (its END must not close a BEGIN).
            var blocks = new Stack<(bool IsBegin, int Start)>();
            var parens = new Stack<int>();
            var regions = new Stack<int>();

            for (var i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                switch (t.Kind)
                {
                    case TokenKind.Comment:
                        if (RegionStart.IsMatch(t.Text)) regions.Push(t.Start);
                        else if (RegionEnd.IsMatch(t.Text) && regions.Count > 0)
                            Add(result, text, FoldKind.Region, regions.Pop(), EndOf(t));
                        continue;
                    case TokenKind.Go:
                        // Anything left open at the end of a batch never closes.
                        blocks.Clear();
                        parens.Clear();
                        continue;
                    case TokenKind.Punctuation:
                        if (t.Text == "(") parens.Push(t.Start);
                        else if (t.Text == ")" && parens.Count > 0)
                            Add(result, text, FoldKind.Parentheses, parens.Pop(), t.End);
                        continue;
                }
                if (!t.IsWord) continue;

                if (t.Is("BEGIN"))
                {
                    var next = NextWord(tokens, i);
                    if (next < 0 || !NonBlockBegin.Contains(tokens[next].Text)) blocks.Push((true, t.Start));
                }
                else if (t.Is("CASE"))
                {
                    blocks.Push((false, t.Start));
                }
                else if (t.Is("END") && blocks.Count > 0)
                {
                    var next = NextWord(tokens, i);
                    if (next >= 0 && tokens[next].Is("CONVERSATION")) continue;
                    var open = blocks.Pop();
                    var end = open.IsBegin && next >= 0 && (tokens[next].Is("TRY") || tokens[next].Is("CATCH")) ? tokens[next].End : t.End;
                    Add(result, text, FoldKind.Block, open.Start, end);
                }
            }
        }

        /// <summary>Multi-line /* */ comments, and runs of lines holding nothing but a -- comment.</summary>
        private static void AddComments(List<FoldRegion> result, string text, List<SqlToken> tokens)
        {
            int runStart = -1, runEnd = -1;
            foreach (var t in tokens)
            {
                if (t.Kind == TokenKind.WhiteSpace) continue;
                var lineComment = t.Kind == TokenKind.Comment && t.Text.StartsWith("--", StringComparison.Ordinal)
                                  && !RegionStart.IsMatch(t.Text) && !RegionEnd.IsMatch(t.Text) && StartsLine(text, t.Start);
                if (lineComment)
                {
                    // A blank line ends the run.
                    if (runStart >= 0 && LineBreaks(text, runEnd, t.Start) > 1) Flush();
                    if (runStart < 0) runStart = t.Start;
                    runEnd = EndOf(t);
                    continue;
                }
                Flush();
                if (t.Kind == TokenKind.Comment && t.Text.StartsWith("/*", StringComparison.Ordinal))
                    Add(result, text, FoldKind.Comment, t.Start, t.End);
            }
            Flush();

            void Flush()
            {
                if (runStart >= 0) Add(result, text, FoldKind.Comment, runStart, runEnd);
                runStart = runEnd = -1;
            }
        }

        /// <summary>CREATE/ALTER of a procedure, function, view or trigger: the whole batch. Returns its first token's index, or -1.</summary>
        private static int AddModule(List<FoldRegion> result, string text, List<SqlToken> sig)
        {
            if (sig.Count < 2 || !(sig[0].Is("CREATE") || sig[0].Is("ALTER"))) return -1;
            var kind = 1;
            if (sig[1].Is("OR") && sig.Count > 3 && sig[2].Is("ALTER")) kind = 3;
            if (kind >= sig.Count || !ModuleKinds.Contains(sig[kind].Text)) return -1;
            Add(result, text, FoldKind.Module, sig[0].Start, sig[sig.Count - 1].End);
            return 0;
        }

        private static void AddStatements(List<FoldRegion> result, string text, List<SqlToken> sig, int module)
        {
            foreach (var (start, statementEnd) in StatementAnalysis.SplitStatements(sig))
            {
                // The module region already covers CREATE PROCEDURE's header.
                if (start == module || !sig[start].IsWord || !FoldedStatements.Contains(sig[start].Text)) continue;
                // ELSE isn't a statement starter, so "IF … SELECT 1 ELSE SELECT 2" leaves it on the first SELECT.
                var end = statementEnd;
                while (end > start && sig[end].Is("ELSE")) end--;
                Add(result, text, FoldKind.Statement, sig[start].Start, sig[end].End);
            }
        }

        /// <summary>Significant tokens of each GO-separated batch.</summary>
        private static IEnumerable<List<SqlToken>> Batches(List<SqlToken> tokens)
        {
            var sig = new List<SqlToken>();
            foreach (var t in tokens)
            {
                if (t.Kind == TokenKind.Go)
                {
                    if (sig.Count > 0) yield return sig;
                    sig = new List<SqlToken>();
                }
                else if (!t.IsTrivia)
                {
                    sig.Add(t);
                }
            }
            if (sig.Count > 0) yield return sig;
        }

        private static void Add(List<FoldRegion> result, string text, FoldKind kind, int start, int end)
        {
            var newline = text.IndexOf('\n', start, end - start);
            if (newline < 0) return;
            result.Add(new FoldRegion(kind, start, end, text.Substring(start, newline - start).Trim()));
        }

        /// <summary>End of a token without the line break a -- comment token carries, so folding it keeps the next line visible.</summary>
        private static int EndOf(SqlToken t) => t.Start + t.Text.TrimEnd('\r', '\n').Length;

        private static int LineBreaks(string text, int from, int to)
        {
            var count = 0;
            for (var i = from; i < to; i++)
                if (text[i] == '\n') count++;
            return count;
        }

        private static bool StartsLine(string text, int position)
        {
            for (var i = position - 1; i >= 0 && text[i] != '\n'; i--)
                if (text[i] != ' ' && text[i] != '\t' && text[i] != '\r') return false;
            return true;
        }

        /// <summary>Index of the next word token after <paramref name="i"/>, skipping trivia; -1 if another token comes first.</summary>
        private static int NextWord(List<SqlToken> tokens, int i)
        {
            for (var j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].IsTrivia) continue;
                return tokens[j].IsWord ? j : -1;
            }
            return -1;
        }

        /// <summary>Document order (outer before inner), one region per span, and each region's nesting depth.</summary>
        private static List<FoldRegion> Arrange(List<FoldRegion> regions)
        {
            var sorted = new List<FoldRegion>();
            foreach (var r in regions.OrderBy(r => r.Start).ThenByDescending(r => r.End))
            {
                if (sorted.Count > 0 && sorted[sorted.Count - 1].Start == r.Start && sorted[sorted.Count - 1].End == r.End) continue;
                sorted.Add(r);
            }
            var open = new Stack<FoldRegion>();
            foreach (var r in sorted)
            {
                while (open.Count > 0 && open.Peek().End < r.End) open.Pop();
                r.Level = open.Count + 1;
                open.Push(r);
            }
            return sorted;
        }
    }
}
