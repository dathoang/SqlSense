using System;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlSense.Core.Analysis;
using SqlSense.Core.Completion;
using SqlSense.Core.Formatting;
using SqlSense.Diagnostics;
using SqlSense.IntelliSense;
using SqlSense.Schema;
using SqlSense.Settings;

namespace SqlSense.Editor
{
    /// <summary>Text edits behind the commands: commit, snippet, wildcard expansion, format.</summary>
    internal static class EditorActions
    {
        /// <summary>Replaces a span and moves the caret to <paramref name="caretOffset"/> inside the new text (-1 = end).</summary>
        public static void Replace(ITextView view, Span span, string newText, int caretOffset = -1)
        {
            var buffer = view.TextBuffer;
            using (var edit = buffer.CreateEdit())
            {
                edit.Replace(span, newText);
                var snapshot = edit.Apply();
                var pos = span.Start + (caretOffset >= 0 ? Math.Min(caretOffset, newText.Length) : newText.Length);
                view.Caret.MoveTo(new SnapshotPoint(snapshot, Math.Min(pos, snapshot.Length)));
                view.Selection.Clear();
            }
        }

        /// <summary>Tab after a snippet shortcut (ssf, st100 …) expands it.</summary>
        public static bool TryExpandSnippet(ITextView view)
        {
            if (!view.Selection.IsEmpty) return false;
            var caret = view.Caret.Position.BufferPosition;
            var line = caret.GetContainingLine();
            var start = caret.Position;
            while (start > line.Start.Position && IsSnippetChar(caret.Snapshot[start - 1])) start--;
            if (start == caret.Position) return false;
            if (start > line.Start.Position && (caret.Snapshot[start - 1] == '.' || caret.Snapshot[start - 1] == '@')) return false;

            var word = caret.Snapshot.GetText(start, caret.Position - start);
            var snippet = SnippetCache.Get(SqlSenseSettings.Current).Find(word);
            if (snippet == null) return false;

            var text = caret.Snapshot.GetText();
            var ctx = ContextAnalyzer.Analyze(text, caret.Position);
            if (ctx.Kind == ContextKind.None) return false;

            var indent = ScriptText.LineIndent(text, caret.Position);
            var exp = SnippetLibrary.Expand(snippet, indent, SqlDocument.LineBreak(caret.Snapshot));
            Replace(view, new Span(start, caret.Position - start), exp.Text, exp.CaretOffset);
            return true;
        }

        private static bool IsSnippetChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>Replaces * (or alias.*) before the caret with the column list.</summary>
        public static bool TryExpandWildcard(ITextView view, bool searchLine = false)
        {
            var snapshot = view.TextBuffer.CurrentSnapshot;
            var caret = view.Caret.Position.BufferPosition.Position;
            var text = snapshot.GetText();
            if (searchLine)
            {
                // From the menu: find a * on the caret line.
                var line = snapshot.GetLineFromPosition(caret);
                var idx = line.GetText().IndexOf('*');
                if (idx < 0) return false;
                caret = line.Start.Position + idx + 1;
            }
            var schema = SchemaService.Get(SsmsConnection.GetActive(), SqlDocument.DatabaseFromUse(text, caret));
            if (schema == null) return false;
            var edit = StarExpander.TryExpand(text, caret, schema, SqlDocument.LineBreak(snapshot));
            if (edit == null) return false;
            Replace(view, new Span(edit.Start, edit.Length), edit.NewText);
            return true;
        }

        /// <summary>Formats the selection (whole lines) or the whole document.</summary>
        public static void Format(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = view.TextBuffer.CurrentSnapshot;
            var settings = SqlSenseSettings.Current;
            var options = settings.Format;
            options.NewLine = SqlDocument.LineBreak(snapshot);

            Span span;
            string baseIndent = string.Empty;
            if (view.Selection.IsEmpty)
            {
                span = new Span(0, snapshot.Length);
            }
            else
            {
                var sel = view.Selection.StreamSelectionSpan.SnapshotSpan;
                var first = sel.Start.GetContainingLine();
                // A selection ending at column 0 doesn't include that line.
                var last = sel.End > sel.Start && sel.End == sel.End.GetContainingLine().Start
                    ? snapshot.GetLineFromPosition(sel.End.Position - 1)
                    : sel.End.GetContainingLine();
                span = Span.FromBounds(first.Start.Position, last.End.Position);
                var firstText = first.GetText();
                baseIndent = firstText.Substring(0, firstText.Length - firstText.TrimStart(' ', '\t').Length).Replace("\t", new string(' ', options.IndentSize));
            }

            var original = snapshot.GetText(span);
            // Only an already-cached schema: formatting never waits for the database.
            var schema = options.UseObjectDefinitionCase
                ? SchemaService.Get(SsmsConnection.GetActive(), SqlDocument.DatabaseFromUse(snapshot.GetText(), span.Start))
                : null;
            if (options.UseObjectDefinitionCase && schema == null) Log.Info("Format: object definition case skipped (schema not loaded yet).");
            var result = new SqlFormatter(options, schema).Format(original, baseIndent);
            if (!result.Success)
            {
                Status("SqlSense: " + result.Error);
                Log.Info("Format skipped: " + result.Error);
                return;
            }
            if (result.Text == original)
            {
                Status("SqlSense: already formatted.");
                return;
            }

            var caretLine = snapshot.GetLineNumberFromPosition(view.Caret.Position.BufferPosition.Position);
            using (var edit = view.TextBuffer.CreateEdit())
            {
                edit.Replace(span, result.Text);
                var after = edit.Apply();
                var line = after.GetLineFromLineNumber(Math.Min(caretLine, after.LineCount - 1));
                view.Caret.MoveTo(line.End);
                view.Selection.Clear();
            }
            Status("SqlSense: formatted.");
        }

        public static void Status(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar bar)
            {
                bar.IsFrozen(out var frozen);
                if (frozen != 0) bar.FreezeOutput(0);
                bar.SetText(message);
            }
        }
    }
}
