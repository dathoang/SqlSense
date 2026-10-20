using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlSense.Core.Analysis;
using SqlSense.Core.Completion;
using SqlSense.Diagnostics;
using SqlSense.IntelliSense;
using SqlSense.Schema;
using SqlSense.Settings;

namespace SqlSense.Editor
{
    /// <summary>
    /// Sits at the front of the query window's command chain (IVsTextView.AddCommandFilter), so it
    /// sees every keystroke before the SQL language service: typing drives the suggestion list,
    /// Tab/Enter commit, Tab expands snippets and SELECT *, and Format commands run the formatter.
    /// </summary>
    internal sealed class SqlCommandFilter : IOleCommandTarget
    {
        public static readonly object SessionMarker = new object();

        /// <summary>Set by the completion source when it had no schema yet (still loading).</summary>
        public static readonly object WithoutSchemaMarker = new object();

        private static readonly string[] TriggerWordsBeforeSpace =
        {
            "FROM", "JOIN", "EXEC", "EXECUTE", "INTO", "UPDATE", "USE", "APPLY", "ON",
        };

        private readonly IWpfTextView _view;
        private readonly ICompletionBroker _broker;
        private ICompletionSession _session;

        /// <summary>Where a list opened before the schema arrived: reopen it there when the schema loads.</summary>
        private (int Version, int Caret)? _reopenWhenSchemaLoads;

        public SqlCommandFilter(IWpfTextView view, ICompletionBroker broker)
        {
            _view = view;
            _broker = broker;
            SchemaService.SchemaLoaded += OnSchemaLoaded;
        }

        public void Detach() => SchemaService.SchemaLoaded -= OnSchemaLoaded;

        private void OnSchemaLoaded(object sender, EventArgs e)
        {
            if (_reopenWhenSchemaLoads == null) return;
            // Raised on a background thread; the lambda switches to the UI thread before touching the view.
#pragma warning disable VSTHRD010, VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ReopenWithSchema();
            }).FileAndForget("SqlSense/ReopenWithSchema");
#pragma warning restore VSTHRD010, VSSDK007
        }

        private void ReopenWithSchema()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var pending = _reopenWhenSchemaLoads;
            _reopenWhenSchemaLoads = null;
            if (pending == null || _view.IsClosed) return;
            try
            {
                if (HasSession)
                {
                    // Still typing in the schema-less list: rebuild it with tables, columns, databases.
                    if (!_session.Properties.ContainsProperty(WithoutSchemaMarker)) return;
                    _session.Dismiss();
                }
                else
                {
                    // The list never opened (nothing to show without schema): only if the user hasn't moved on.
                    var caret = _view.Caret.Position.BufferPosition;
                    if (caret.Snapshot.Version.VersionNumber != pending.Value.Version || caret.Position != pending.Value.Caret) return;
                }
                Log.Trace("schema loaded: reopening the list");
                StartSession();
            }
            catch (Exception ex)
            {
                Log.Error("Reopening suggestions after the schema loaded", ex);
            }
        }

        public IOleCommandTarget Next { get; set; }

        private bool Active => SqlSenseSettings.Current.Enabled && SqlDocument.IsSql(_view.TextBuffer);

        private bool HasSession => _session != null && !_session.IsDismissed;

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!Active) return Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            _handled = null;
            try
            {
                if (pguidCmdGroup == VSConstants.VSStd2K)
                {
                    switch ((VSConstants.VSStd2KCmdID)nCmdID)
                    {
                        case VSConstants.VSStd2KCmdID.TYPECHAR:
                            return TypeChar(GetTypedChar(pvaIn), ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

                        case VSConstants.VSStd2KCmdID.RETURN:
                            if (Commit(requireSelection: true)) return VSConstants.S_OK;
                            break;

                        case VSConstants.VSStd2KCmdID.TAB:
                            if (Commit(requireSelection: false)) return VSConstants.S_OK;
                            if (SqlSenseSettings.Current.TabExpandsSnippets && EditorActions.TryExpandSnippet(_view)) return VSConstants.S_OK;
                            if (SqlSenseSettings.Current.TabExpandsWildcard && _view.Selection.IsEmpty && EditorActions.TryExpandWildcard(_view))
                                return VSConstants.S_OK;
                            break;

                        case VSConstants.VSStd2KCmdID.CANCEL:
                            if (HasSession)
                            {
                                _session.Dismiss();
                                return VSConstants.S_OK;
                            }
                            break;

                        case VSConstants.VSStd2KCmdID.UP:
                        case VSConstants.VSStd2KCmdID.DOWN:
                        case VSConstants.VSStd2KCmdID.PAGEUP:
                        case VSConstants.VSStd2KCmdID.PAGEDN:
                            if (HasSession && _session.Presenter is IIntellisenseCommandTarget target)
                            {
                                target.ExecuteKeyboardCommand(Map((VSConstants.VSStd2KCmdID)nCmdID));
                                return VSConstants.S_OK;
                            }
                            break;

                        case VSConstants.VSStd2KCmdID.BACKSPACE:
                        case VSConstants.VSStd2KCmdID.DELETE:
                        {
                            var hr = Forward(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                            Refilter();
                            return hr;
                        }

                        case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                        case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                        case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                            StartSession();
                            return VSConstants.S_OK;

                        case VSConstants.VSStd2KCmdID.FORMATDOCUMENT:
                        case VSConstants.VSStd2KCmdID.FORMATSELECTION:
                            EditorActions.Format(_view);
                            return VSConstants.S_OK;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Handling editor command " + nCmdID, ex);
                // The key was already typed (or the edit made) before the failure: passing it on
                // again would type it twice ("sy" -> "syy").
                if (_handled is int hr) return hr;
            }
            return Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        /// <summary>Set once the current command has reached the buffer, so it is never executed twice.</summary>
        private int? _handled;

        private int Forward(ref Guid group, uint id, uint opt, IntPtr pin, IntPtr pout)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var hr = Next.Exec(ref group, id, opt, pin, pout);
            _handled = hr;
            return hr;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Active && pguidCmdGroup == VSConstants.VSStd2K && cCmds == 1)
            {
                switch ((VSConstants.VSStd2KCmdID)prgCmds[0].cmdID)
                {
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                    case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                    case VSConstants.VSStd2KCmdID.FORMATDOCUMENT:
                    case VSConstants.VSStd2KCmdID.FORMATSELECTION:
                        prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
                        return VSConstants.S_OK;
                }
            }
            return Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        // ------------------------------------------------------------------

        private int TypeChar(char ch, ref Guid group, uint id, uint opt, IntPtr pin, IntPtr pout)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = SqlSenseSettings.Current;
            if (HasSession && !IsWordChar(ch))
            {
                // Punctuation commits a definite selection, then is typed as usual.
                if (ch == '.' || ch == '(' || ch == ',' || ch == ')' || ch == ';' || ch == '=') Commit(requireSelection: true);
                else _session.Dismiss();
            }

            if (settings.AutoCloseBrackets && HandleBrackets(ch)) return VSConstants.S_OK;

            var hr = Forward(ref group, id, opt, pin, pout);
            if (hr != VSConstants.S_OK) return hr;

            if (HasSession)
            {
                Refilter();
            }
            else if (settings.AutoTrigger && ShouldTrigger(ch))
            {
                StartSession();
            }
            return hr;
        }

        /// <summary>Auto-closes ( [ ' and types over the closing character.</summary>
        private bool HandleBrackets(char ch)
        {
            var caret = _view.Caret.Position.BufferPosition;
            var snapshot = caret.Snapshot;
            var next = caret.Position < snapshot.Length ? snapshot[caret.Position] : '\0';

            if ((ch == ')' || ch == ']') && next == ch && _view.Selection.IsEmpty && !InCommentOrString(caret)
                || ch == '\'' && next == '\'' && _view.Selection.IsEmpty && InCommentOrString(caret))
            {
                // Type over the closer (for a quote: only when it closes the string we're in).
                _view.Caret.MoveTo(new SnapshotPoint(snapshot, caret.Position + 1));
                return true;
            }

            string close = ch == '(' ? ")" : ch == '[' ? "]" : ch == '\'' ? "'" : null;
            if (close == null || !_view.Selection.IsEmpty) return false;
            if (!(next == '\0' || char.IsWhiteSpace(next) || next == ')' || next == ',' || next == ';')) return false;
            if (ch == '\'')
            {
                var prev = caret.Position > 0 ? snapshot[caret.Position - 1] : '\0';
                if (char.IsLetterOrDigit(prev) && prev != 'N' && prev != 'n') return false; // don't'
            }
            if (InCommentOrString(caret)) return false;

            EditorActions.Replace(_view, new Span(caret.Position, 0), ch + close, 1);
            return true;
        }

        private static bool InCommentOrString(SnapshotPoint caret) =>
            ContextAnalyzer.Analyze(caret.Snapshot.GetText(), caret.Position).Kind == ContextKind.None;

        private bool ShouldTrigger(char ch)
        {
            var caret = _view.Caret.Position.BufferPosition;
            var snapshot = caret.Snapshot;
            if (ch == '.') return true;
            if (ch == ' ')
            {
                var line = caret.GetContainingLine();
                var before = snapshot.GetText(line.Start.Position, caret.Position - line.Start.Position).TrimEnd();
                foreach (var w in TriggerWordsBeforeSpace)
                {
                    if (before.EndsWith(w, StringComparison.OrdinalIgnoreCase)
                        && (before.Length == w.Length || !IsWordChar(before[before.Length - w.Length - 1])))
                        return true;
                }
                return false;
            }
            if (!(char.IsLetter(ch) || ch == '_' || ch == '@' || ch == '#' || ch == '[')) return false;
            // Only at the start of a word, so the list doesn't reopen in the middle of one.
            var p = caret.Position - 2;
            return p < 0 || !IsWordChar(snapshot[p]) && snapshot[p] != '.';
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';

        private void StartSession()
        {
            if (HasSession)
            {
                Refilter();
                return;
            }
            var caret = _view.Caret.Position.BufferPosition;
            var point = caret.Snapshot.CreateTrackingPoint(caret.Position, PointTrackingMode.Positive);
            var session = _broker.CreateCompletionSession(_view, point, true);
            _session = session;
            session.Properties.AddProperty(SessionMarker, true);
            session.Dismissed += OnSessionDismissed;
            session.Start();
            _reopenWhenSchemaLoads = session.Properties.ContainsProperty(WithoutSchemaMarker)
                ? (caret.Snapshot.Version.VersionNumber, caret.Position)
                : ((int, int)?)null;
            if (!HasSession)
            {
                Log.Trace($"start at {caret.Position}: no list (no suggestions, or the editor closed it)");
                return;
            }
            var set = SqlSet();
            if (set != null && !ReferenceEquals(_session.SelectedCompletionSet, set))
            {
                try
                {
                    _session.SelectedCompletionSet = set;
                }
                catch (Exception ex)
                {
                    Log.Error("Selecting the SqlSense completion set", ex);
                }
            }
            Refilter();
        }

        /// <summary>
        /// Our own set. Other completion sources can add sets to the session; committing one of those
        /// would use its span (often empty at the caret) and re-insert what was already typed.
        /// </summary>
        private SqlCompletionSet SqlSet() => _session.CompletionSets.OfType<SqlCompletionSet>().FirstOrDefault();

        private void Refilter()
        {
            if (!HasSession) return;
            // Filter() can dismiss the session, which clears _session (OnSessionDismissed): work on a local.
            var session = _session;
            try
            {
                var set = SqlSet();
                var caret = _view.Caret.Position.BufferPosition;
                if (set == null || caret.Position < set.ApplicableTo.GetStartPoint(caret.Snapshot).Position)
                {
                    session.Dismiss();
                    return;
                }
                // Filter with SqlSense matching first: with no match we close the list ourselves (and know
                // why); otherwise the session's own Filter() keeps the presenter in sync (it reuses our result).
                set.Filter();
                var typed = set.ApplicableTo.GetText(caret.Snapshot);
                var matches = set.Completions.Count;
                Log.Trace($"refilter typed='{typed}' caret={caret.Position} span={set.ApplicableTo.GetSpan(caret.Snapshot).Span} " +
                          $"matches={matches} of {set.TotalCount}");
                if (matches == 0)
                {
                    session.Dismiss();
                    return;
                }
                session.Filter();
                if (session.IsDismissed)
                {
                    Log.Trace($"editor closed the list after filtering '{typed}' ({matches} matches)");
                    return;
                }
                set.SelectBestMatch();
            }
            catch (Exception ex)
            {
                // A broken list must not cost the keystroke: close it and keep typing.
                Log.Error("Filtering suggestions", ex);
                if (!session.IsDismissed) session.Dismiss();
            }
        }

        private bool Commit(bool requireSelection)
        {
            if (!HasSession) return false;
            var set = SqlSet();
            var status = set?.SelectionStatus;
            if (status?.Completion == null || requireSelection && !status.IsSelected)
            {
                _session.Dismiss();
                return false;
            }

            var completion = status.Completion;
            completion.Properties.TryGetProperty(typeof(CompletionItem), out CompletionItem item);
            var insert = item?.InsertText ?? completion.InsertionText;
            var caretOffset = item?.CaretOffset ?? -1;
            var span = ReplacementSpan(set.ApplicableTo.GetSpan(_view.TextBuffer.CurrentSnapshot));
            _session.Dismiss();
            EditorActions.Replace(_view, span, insert, caretOffset);
            _handled = VSConstants.S_OK;
            return true;
        }

        /// <summary>
        /// The typed prefix plus the rest of the word after the caret, so committing before or inside
        /// an existing word ("n|ame", "[Cu|]" with an auto-closed bracket) replaces it instead of
        /// leaving the old tail behind ("namename", "[Customers]]").
        /// </summary>
        private static Span ReplacementSpan(SnapshotSpan typed)
        {
            var snapshot = typed.Snapshot;
            var end = typed.End.Position;
            while (end < snapshot.Length && IsWordChar(snapshot[end])) end++;
            var text = typed.GetText();
            if (text.StartsWith("[", StringComparison.Ordinal) && text.IndexOf(']') < 0
                && end < snapshot.Length && snapshot[end] == ']')
                end++;
            return Span.FromBounds(typed.Start.Position, end);
        }

        private void OnSessionDismissed(object sender, EventArgs e)
        {
            if (sender is ICompletionSession s) s.Dismissed -= OnSessionDismissed;
            // Who closed the list: the editor (EnsureSessionValid, buffer change) or SqlSense.
            var frames = Environment.StackTrace.Split('\n')
                .Select(f => f.Trim())
                .Where(f => f.StartsWith("at ", StringComparison.Ordinal)
                            && (f.Contains("Intellisense") || f.Contains("SqlSense")) && !f.Contains("OnSessionDismissed"))
                .Take(6)
                .Select(f => f.Length > 120 ? f.Substring(0, 120) : f);
            Log.Trace("dismissed by: " + string.Join(" <- ", frames));
            if (ReferenceEquals(sender, _session)) _session = null;
        }

        private static IntellisenseKeyboardCommand Map(VSConstants.VSStd2KCmdID id)
        {
            switch (id)
            {
                case VSConstants.VSStd2KCmdID.UP: return IntellisenseKeyboardCommand.Up;
                case VSConstants.VSStd2KCmdID.DOWN: return IntellisenseKeyboardCommand.Down;
                case VSConstants.VSStd2KCmdID.PAGEUP: return IntellisenseKeyboardCommand.PageUp;
                default: return IntellisenseKeyboardCommand.PageDown;
            }
        }

        private static char GetTypedChar(IntPtr pvaIn) => (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
    }
}
