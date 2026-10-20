using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SqlSense.Core.Lexing;

namespace SqlSense.Core.Analysis
{
    /// <summary>A GO-delimited batch, tokenized, with offsets relative to the whole document.</summary>
    public sealed class Batch
    {
        public Batch(int start, string text, List<SqlToken> tokens)
        {
            Start = start;
            Text = text;
            Tokens = tokens;
        }

        public int Start { get; }
        public string Text { get; }
        public int End => Start + Text.Length;

        /// <summary>Tokens with batch-relative offsets.</summary>
        public List<SqlToken> Tokens { get; }
    }

    public static class ScriptText
    {
        private static readonly Regex GoLine = new Regex(@"^[ \t]*GO(?:[ \t]+\d+)?[ \t]*(?:--.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>Returns the batch containing <paramref name="position"/>, tokenized.</summary>
        public static Batch GetBatchAt(string text, int position)
        {
            text = text ?? string.Empty;
            position = Math.Max(0, Math.Min(position, text.Length));
            var start = 0;
            var end = text.Length;
            foreach (Match m in GoLine.Matches(text))
            {
                var lineEnd = m.Index + m.Length;
                if (lineEnd < position)
                {
                    start = lineEnd;
                    continue;
                }
                // Either the GO line is after the caret, or the caret is on the GO line itself;
                // both end the batch the caret belongs to.
                end = m.Index;
                break;
            }
            var batchText = text.Substring(start, end - start);
            return new Batch(start, batchText, SqlLexer.Tokenize(batchText));
        }

        /// <summary>Index of the last token that starts before <paramref name="offset"/>, or -1.</summary>
        public static int TokenIndexBefore(List<SqlToken> tokens, int offset)
        {
            int lo = 0, hi = tokens.Count - 1, found = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (tokens[mid].Start < offset)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return found;
        }

        public static int PrevSignificant(List<SqlToken> tokens, int index)
        {
            for (var i = index - 1; i >= 0; i--)
                if (!tokens[i].IsTrivia) return i;
            return -1;
        }

        public static int NextSignificant(List<SqlToken> tokens, int index)
        {
            for (var i = index + 1; i < tokens.Count; i++)
                if (!tokens[i].IsTrivia) return i;
            return -1;
        }

        public static string LineIndent(string text, int position)
        {
            var lineStart = position;
            while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;
            var i = lineStart;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return text.Substring(lineStart, i - lineStart);
        }

        public static int ColumnOf(string text, int position)
        {
            var col = 0;
            for (var i = position - 1; i >= 0 && text[i] != '\n'; i--) col++;
            return col;
        }

        public static List<SqlToken> Significant(IEnumerable<SqlToken> tokens)
        {
            var list = new List<SqlToken>();
            foreach (var t in tokens) if (!t.IsTrivia) list.Add(t);
            return list;
        }
    }
}
