using System;
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

        private static readonly string[] TriggerWordsBeforeSpace =
        {
            "FROM", "JOIN", "EXEC", "EXECUTE", "INTO", "UPDATE", "USE", "APPLY", "ON",
        };

        private readonly IWpfTextView _view;
        private readonly ICompletionBroker _broker;
        private ICompletionSession _session;

        public SqlCommandFilter(IWpfTextView view, ICompletionBroker broker)
        {
            _view = view;
            _broker = broker;
        }

        public IOleCommandTarget Next { get; set; }

        private bool Active => SqlSenseSettings.Current.Enabled && SqlDocument.IsSql(_view.TextBuffer);

        private bool HasSession => _session != null && !_session.IsDismissed;

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!Active) return Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

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
                            var hr = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
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
            }
            return Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
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

            var hr = Next.Exec(ref group, id, opt, pin, pout);
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
            _session = _broker.CreateCompletionSession(_view, point, true);
            _session.Properties.AddProperty(SessionMarker, true);
            _session.Dismissed += OnSessionDismissed;
            _session.Start();
            if (HasSession) Refilter();
        }

        private void Refilter()
        {
            if (!HasSession) return;
            var set = _session.SelectedCompletionSet;
            var caret = _view.Caret.Position.BufferPosition;
            if (set == null || caret.Position < set.ApplicableTo.GetStartPoint(caret.Snapshot).Position)
            {
                _session.Dismiss();
                return;
            }
            _session.Filter();
            if (set.Completions.Count == 0) _session.Dismiss();
            else set.SelectBestMatch();
        }

        private bool Commit(bool requireSelection)
        {
            if (!HasSession) return false;
            var set = _session.SelectedCompletionSet;
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
            var span = set.ApplicableTo.GetSpan(_view.TextBuffer.CurrentSnapshot);
            _session.Dismiss();
            EditorActions.Replace(_view, span, insert, caretOffset);
            return true;
        }

        private void OnSessionDismissed(object sender, EventArgs e)
        {
            if (sender is ICompletionSession s) s.Dismissed -= OnSessionDismissed;
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
