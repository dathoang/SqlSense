using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlSense.Core.Analysis;
using SqlSense.Core.Language;
using SqlSense.Core.Lexing;
using SqlSense.Core.Schema;

namespace SqlSense.Core.Formatting
{
    public sealed class FormatResult
    {
        private FormatResult(bool success, string text, string error)
        {
            Success = success;
            Text = text;
            Error = error;
        }

        public bool Success { get; }
        public string Text { get; }
        public string Error { get; }

        public static FormatResult Ok(string text) => new FormatResult(true, text, null);
        public static FormatResult Fail(string original, string error) => new FormatResult(false, original, error);
    }

    /// <summary>
    /// Token-stream formatter. It only changes whitespace and the case of keywords, built-in
    /// function names and data types, and it verifies that before returning, so a formatting bug
    /// can never change what a script does. Comments are kept.
    /// </summary>
    public sealed class SqlFormatter
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        private static readonly HashSet<string> JoinWords = new HashSet<string>(Ci)
        {
            "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "JOIN", "APPLY", "HASH", "LOOP", "MERGE", "REMOTE",
        };

        private static readonly HashSet<string> NonBlockBeginFollowers = new HashSet<string>(Ci)
        {
            "TRAN", "TRANSACTION", "DISTRIBUTED", "DIALOG", "CONVERSATION",
        };

        private static readonly HashSet<string> CollapsibleStatements = new HashSet<string>(Ci)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH",
        };

        private static readonly HashSet<string> AliasFollowers = new HashSet<string>(Ci)
        {
            "FROM", "INTO", "WHERE", "UNION", "EXCEPT", "INTERSECT", "ORDER", "GROUP", "HAVING", "OPTION",
        };

        // Alignment markers live in the Private Use Area while formatting and are replaced by padding at the end.
        private const char AliasMark = '';
        private const char CommentMark = '';
        private const char GroupBase = '';
        private const int GroupCount = 0xE00;

        private static bool IsMarker(char c) => c >= '' && c <= '';

        private static readonly HashSet<string> ListClauses = new HashSet<string>(Ci)
        {
            "SELECT", "GROUP BY", "ORDER BY", "SET", "FROM", "VALUES", "OUTPUT", "DECLARE", "PARTITION BY",
        };

        // System functions spelled like variables; T-SQL resolves them case-insensitively.
        private static readonly HashSet<string> GlobalVariables = new HashSet<string>(Ci)
        {
            "@@CONNECTIONS", "@@CPU_BUSY", "@@CURSOR_ROWS", "@@DATEFIRST", "@@DBTS", "@@ERROR", "@@FETCH_STATUS", "@@IDENTITY",
            "@@IDLE", "@@IO_BUSY", "@@LANGID", "@@LANGUAGE", "@@LOCK_TIMEOUT", "@@MAX_CONNECTIONS", "@@MAX_PRECISION",
            "@@NESTLEVEL", "@@OPTIONS", "@@PACK_RECEIVED", "@@PACK_SENT", "@@PACKET_ERRORS", "@@PROCID", "@@REMSERVER",
            "@@ROWCOUNT", "@@SERVERNAME", "@@SERVICENAME", "@@SPID", "@@TEXTSIZE", "@@TIMETICKS", "@@TOTAL_ERRORS",
            "@@TOTAL_READ", "@@TOTAL_WRITE", "@@TRANCOUNT", "@@VERSION",
        };

        private static readonly HashSet<string> ConstraintStarters = new HashSet<string>(Ci)
        {
            "CONSTRAINT", "PRIMARY", "UNIQUE", "CHECK", "DEFAULT", "REFERENCES", "FOREIGN", "INDEX",
        };

        private readonly FormatOptions _o;
        private readonly Dictionary<string, string> _names;

        /// <param name="schema">Optional; supplies the defined spelling of names for <see cref="FormatOptions.UseObjectDefinitionCase"/>.</param>
        public SqlFormatter(FormatOptions options = null, DatabaseSchema schema = null)
        {
            _o = options ?? new FormatOptions();
            if (_o.UseObjectDefinitionCase && schema != null) _names = DefinedNames(schema);
        }

        /// <summary>Case-insensitive name → defined spelling; names defined with two spellings are left out.</summary>
        private static Dictionary<string, string> DefinedNames(DatabaseSchema schema)
        {
            var names = new Dictionary<string, string>(Ci);
            var clash = new HashSet<string>(Ci);
            void Add(string name)
            {
                if (string.IsNullOrEmpty(name) || clash.Contains(name)) return;
                if (!names.TryGetValue(name, out var existing)) names[name] = name;
                else if (existing != name) { names.Remove(name); clash.Add(name); }
            }
            foreach (var s in schema.Schemas) Add(s);
            foreach (var o in schema.Objects)
            {
                Add(o.Schema);
                Add(o.Name);
                foreach (var c in o.Columns) Add(c.Name);
            }
            return names;
        }

        public FormatResult Format(string sql, string baseIndent = "")
        {
            if (string.IsNullOrWhiteSpace(sql)) return FormatResult.Ok(sql ?? string.Empty);
            if (SqlLexer.HasLexErrors(sql))
                return FormatResult.Fail(sql, "The script contains text the T-SQL lexer can't read (an unterminated string, comment or bracket?).");

            string output;
            try
            {
                output = new Run(_o, sql, baseIndent ?? string.Empty, _names).Execute();
            }
            catch (Exception ex)
            {
                return FormatResult.Fail(sql, "Formatter error: " + ex.Message);
            }

            var problem = Verify(sql, output);
            return problem == null ? FormatResult.Ok(output) : FormatResult.Fail(sql, "Formatting was cancelled because it would have changed the script: " + problem);
        }

        /// <summary>Returns null when both scripts have the same tokens (ignoring whitespace and word case).</summary>
        public static string Verify(string original, string formatted)
        {
            var a = SqlLexer.Tokenize(original).Where(t => t.Kind != TokenKind.WhiteSpace).ToList();
            var b = SqlLexer.Tokenize(formatted).Where(t => t.Kind != TokenKind.WhiteSpace).ToList();
            var n = Math.Min(a.Count, b.Count);
            for (var i = 0; i < n; i++)
            {
                var x = a[i];
                var y = b[i];
                bool same;
                if (x.Kind == TokenKind.Keyword || x.Kind == TokenKind.Identifier || x.Kind == TokenKind.Go)
                    same = y.Kind == x.Kind && string.Equals(x.Text, y.Text, StringComparison.OrdinalIgnoreCase);
                else if (x.Kind == TokenKind.Comment)
                    same = y.Kind == TokenKind.Comment && CommentContent(x.Text) == CommentContent(y.Text);
                else if (x.Kind == TokenKind.Variable && GlobalVariables.Contains(x.Text))
                    same = y.Kind == x.Kind && string.Equals(x.Text, y.Text, StringComparison.OrdinalIgnoreCase);
                else
                    same = x.Text == y.Text;
                if (!same) return $"token '{x.Text}' became '{y.Text}'";
            }
            return a.Count == b.Count ? null : $"token count changed from {a.Count} to {b.Count}";
        }

        /// <summary>A comment's text without the indentation of its lines, which the formatter may change.</summary>
        private static string CommentContent(string text) =>
            string.Join("\n", text.Split('\n').Select(l => l.Trim(' ', '\t', '\r'))).TrimEnd('\n');

        // =====================================================================

        private enum FrameKind
        {
            Root,
            Block,
            Subquery,
            ColumnList,
            Paren,
            Case,
        }

        private sealed class Frame
        {
            public FrameKind Kind;
            public int Indent;           // statement indent for Root/Block/Subquery; item indent for ColumnList
            public int OpenLineIndent;   // where the closing paren goes
            public int StatementIndent;
            public string FirstWord;
            public string Clause;
            public int ItemColumn = -1;
            public int JoinIndent;
            public bool InBetween;
            public int IfIndent = -1;
            public bool PendingBody;
            public bool AwaitStatement = true;
            public int CaseColumn;
            public int StatementStartSig;
            public int AlignGroup;
            public bool SpaceInside;
            public int BreakBeforeSig = -1; // first select item after DISTINCT / TOP
            public int WhenColumn;
            public bool FirstWhenInline;
            public bool SeenWhen;

            public bool IsStatementLevel => Kind == FrameKind.Root || Kind == FrameKind.Block || Kind == FrameKind.Subquery;
        }

        private sealed class Run
        {
            private readonly FormatOptions _o;
            private readonly string _sql;
            private readonly string _baseIndent;
            private readonly List<SqlToken> _all;     // non-whitespace tokens (comments included)
            private readonly List<int> _nlBefore;     // newlines in the whitespace before each _all token
            private readonly List<SqlToken> _sig;     // significant tokens
            private readonly Dictionary<SqlToken, int> _sigIndex = new Dictionary<SqlToken, int>();
            private readonly Dictionary<SqlToken, int> _allIndex = new Dictionary<SqlToken, int>();
            private readonly bool _markersAllowed;
            private int _cur;             // significant index of the token being written
            private int _compactEnd = -1; // last significant index of the region kept on one line
            private int _groups;
            private readonly Dictionary<string, string> _names;
            private bool _afterGo;
            private readonly HashSet<int> _statementStarts = new HashSet<int>();
            private readonly Stack<Frame> _frames = new Stack<Frame>();
            private readonly StringBuilder _sb = new StringBuilder();
            private int _col;
            private int _lineIndent;
            private bool _atLineStart = true;
            private bool _forceNewLine;
            private bool _prevUnary;
            private SqlToken _prevSig;
            private SqlToken _prevWritten;

            public Run(FormatOptions o, string sql, string baseIndent, Dictionary<string, string> names)
            {
                _o = o;
                _names = names;
                _sql = sql;
                _baseIndent = baseIndent;
                _all = new List<SqlToken>();
                _nlBefore = new List<int>();
                var nl = 0;
                foreach (var t in SqlLexer.Tokenize(sql))
                {
                    if (t.Kind == TokenKind.WhiteSpace)
                    {
                        foreach (var c in t.Text) if (c == '\n') nl++;
                        continue;
                    }
                    _all.Add(t);
                    _nlBefore.Add(nl);
                    nl = 0;
                    // A single-line comment owns its line break.
                    if (t.Kind == TokenKind.Comment && t.Text.EndsWith("\n", StringComparison.Ordinal)) nl = 1;
                }
                _sig = _all.Where(t => !t.IsTrivia).ToList();
                for (var i = 0; i < _all.Count; i++) _allIndex[_all[i]] = i;
                _markersAllowed = (o.AlignAliases || o.AlignComments) && !sql.Any(IsMarker);
                for (var i = 0; i < _sig.Count; i++) _sigIndex[_sig[i]] = i;
                foreach (var (start, _) in StatementAnalysis.SplitStatements(_sig)) _statementStarts.Add(start);
            }

            public string Execute()
            {
                var root = new Frame { Kind = FrameKind.Root, Indent = _baseIndent.Length, StatementIndent = _baseIndent.Length };
                _frames.Push(root);
                _sb.Append(_baseIndent);
                _col = _lineIndent = _baseIndent.Length;

                for (var k = 0; k < _all.Count; k++)
                {
                    var t = _all[k];
                    if (t.Kind == TokenKind.Comment) { WriteComment(t, _nlBefore[k]); continue; }
                    var s = _sigIndex[t];
                    _cur = s;
                    if (!ShouldSkip(t, s)) Token(t, s, _nlBefore[k]);
                    if (_compactEnd >= 0 && s >= _compactEnd) _compactEnd = -1;
                    _afterGo = t.Kind == TokenKind.Go;
                }

                var text = TrimTrailingSpaces(ResolveAlignment(_sb.ToString()));
                var trailing = _sql.Length - _sql.TrimEnd().Length;
                if (trailing > 0 && _sql.TrimEnd(' ', '\t').EndsWith("\n", StringComparison.Ordinal)) text += _o.NewLine;
                return text;
            }

            private Frame Top => _frames.Peek();

            private bool InCompact => _compactEnd >= 0 && _cur <= _compactEnd;

            private SqlToken Sig(int i) => i >= 0 && i < _sig.Count ? _sig[i] : null;

            // ------------------------------------------------------------------

            private void Token(SqlToken t, int s, int nlBefore)
            {
                var u = t.IsWord ? t.Upper : t.Text;
                var top = Top;

                if (t.Kind == TokenKind.Go)
                {
                    while (_frames.Count > 1) _frames.Pop();
                    var root = Top;
                    root.Clause = null;
                    root.AwaitStatement = true;
                    root.PendingBody = false;
                    NewLineAt(root.Indent, blank: nlBefore >= 2);
                    Write(Case(t, s));
                    _forceNewLine = true;
                    Done(t);
                    return;
                }

                if (_forceNewLine && !_atLineStart)
                {
                    // After a line comment: continue on a new line (statement starts handle their own).
                    if (!(top.IsStatementLevel && (_statementStarts.Contains(s) || top.AwaitStatement)))
                        NewLineAt(_lineIndent, false);
                }
                _forceNewLine = false;

                // ----- statements -----
                if (top.IsStatementLevel && (u == "END" && top.Kind == FrameKind.Block && !IsCaseOpen()))
                {
                    _frames.Pop();
                    var outer = Top;
                    NewLineAt(outer.StatementIndent, nlBefore >= 2);
                    Write(Case(t, s));
                    var n = Sig(s + 1);
                    if (n != null && (n.Is("TRY") || n.Is("CATCH")))
                    {
                        Write(" " + KeywordCase(n.Text));
                        _skipNext = n;
                    }
                    outer.AwaitStatement = true;
                    outer.Clause = null;
                    Done(t);
                    return;
                }

                if (top.IsStatementLevel && u == "ELSE" && !IsCaseOpen())
                {
                    NewLineAt(top.IfIndent >= 0 ? top.IfIndent : top.StatementIndent, nlBefore >= 2);
                    Write(Case(t, s));
                    top.PendingBody = true;
                    top.AwaitStatement = true;
                    top.Clause = null;
                    Done(t);
                    return;
                }

                if (top.IsStatementLevel && (_statementStarts.Contains(s) || top.AwaitStatement) && t.Text != ";" && t.Text != ")")
                {
                    StartStatement(t, s, u, nlBefore);
                    return;
                }

                if (top.IsStatementLevel && s == top.BreakBeforeSig)
                {
                    // SELECT DISTINCT / TOP (n) ⏎ first item
                    top.BreakBeforeSig = -1;
                    NewLineAt(top.ItemColumn, false);
                }

                // ----- punctuation -----
                if (t.Text == ";")
                {
                    Write(";");
                    if (top.IsStatementLevel) { top.AwaitStatement = true; top.Clause = null; }
                    Done(t);
                    return;
                }
                if (t.Text == "(") { OpenParen(t, s); return; }
                if (t.Text == ")") { CloseParen(t); return; }
                if (t.Text == ",") { Comma(t, s); return; }

                // ----- CASE -----
                if (u == "CASE" && t.Kind == TokenKind.Keyword)
                {
                    SpaceIfNeeded(t, s);
                    if (!InCompact && _o.CollapseCaseShorterThan > 0)
                    {
                        var end = MatchingCaseEnd(s);
                        if (end > s && Measure(s, end) < _o.CollapseCaseShorterThan) _compactEnd = end;
                    }
                    var column = _col;
                    Write(Case(t, s));
                    var next = Sig(s + 1);
                    var hasInput = next != null && !next.Is("WHEN");
                    _frames.Push(new Frame
                    {
                        Kind = FrameKind.Case,
                        CaseColumn = column,
                        WhenColumn = column + _o.IndentSize,
                        FirstWhenInline = _o.FirstWhenOnNewLine == CaseWhenPlacement.Never
                                          || _o.FirstWhenOnNewLine == CaseWhenPlacement.IfInputExpression && !hasInput,
                    });
                    Done(t);
                    return;
                }
                if (top.Kind == FrameKind.Case && (u == "WHEN" || u == "ELSE"))
                {
                    if (!top.SeenWhen && u == "WHEN" && top.FirstWhenInline)
                    {
                        // CASE WHEN … : later WHEN / ELSE line up under this one.
                        SpaceIfNeeded(t, s);
                        top.WhenColumn = _col;
                    }
                    else
                    {
                        NewLineAt(top.WhenColumn, false);
                    }
                    top.SeenWhen = true;
                    Write(Case(t, s));
                    Done(t);
                    return;
                }
                if (top.Kind == FrameKind.Case && u == "END")
                {
                    _frames.Pop();
                    NewLineAt(top.CaseColumn, false);
                    Write(Case(t, s));
                    Done(t);
                    return;
                }

                // ----- CREATE TABLE column constraints -----
                if (top.Kind == FrameKind.ColumnList && _o.DdlConstraintsOnNewLines && t.Kind == TokenKind.Keyword && StartsColumnConstraint(s))
                {
                    NewLineAt(top.Indent + _o.IndentSize, false);
                    Write(Case(t, s));
                    Done(t);
                    return;
                }

                // ----- clauses -----
                if (top.IsStatementLevel && t.IsWord && Clause(t, s, u, top)) return;

                if (t.IsWord && u == "BETWEEN") top.InBetween = true;

                if (_o.AlignAliases && IsSelectAlias(t, s, top)) Mark(AliasMark, top.AlignGroup);
                SpaceIfNeeded(t, s);
                Write(Case(t, s));
                Done(t);
            }

            private void StartStatement(SqlToken t, int s, string u, int nlBefore)
            {
                var top = Top;
                top.AwaitStatement = false;
                var indent = top.Indent;
                if (top.PendingBody)
                {
                    indent = (top.IfIndent >= 0 ? top.IfIndent : top.Indent) + (u == "BEGIN" ? 0 : _o.IndentSize);
                    top.PendingBody = false;
                }

                var afterElse = _prevSig != null && _prevSig.Is("ELSE") && u == "IF";
                if (afterElse) SpaceIfNeeded(t, s);
                else NewLineAt(indent, nlBefore >= 2);
                if (afterElse) indent = top.IfIndent >= 0 ? top.IfIndent : top.Indent;

                top.StatementIndent = indent;
                top.StatementStartSig = s;
                top.AlignGroup = ++_groups;
                if (!InCompact && _o.CollapseStatementsShorterThan > 0 && top.Kind != FrameKind.Subquery && CollapsibleStatements.Contains(u))
                {
                    var end = StatementEnd(s);
                    if (Measure(s, end) < _o.CollapseStatementsShorterThan) _compactEnd = end;
                }
                top.FirstWord = u;
                top.Clause = u == "SELECT" || u == "SET" || u == "DECLARE" || u == "UPDATE" || u == "DELETE" || u == "INSERT" || u == "WITH" ? u : null;
                top.InBetween = false;
                // Non-reserved statement words (THROW, …) are identifiers to the lexer.
                Write(t.Kind == TokenKind.Identifier && SqlVocabulary.StatementStarters.Contains(t.Text) ? KeywordCase(t.Text) : Case(t, s));
                if (top.Clause == "SELECT" || top.Clause == "SET" || top.Clause == "DECLARE")
                    top.ItemColumn = _col + 1;
                top.BreakBeforeSig = u == "SELECT" && _o.BreakAfterDistinctTop ? FirstSelectItem(s) : -1;

                if (u == "IF" || u == "WHILE")
                {
                    top.IfIndent = indent;
                    // The body begins at the next statement start; mark it pending once the condition is written.
                    _pendingBodyFor = top;
                    _pendingBodyDepth = _frames.Count;
                }
                else if (u == "BEGIN")
                {
                    var n = Sig(s + 1);
                    if (n == null || !NonBlockBeginFollowers.Contains(n.Text))
                    {
                        if (n != null && (n.Is("TRY") || n.Is("CATCH")))
                        {
                            Write(" " + KeywordCase(n.Text));
                            _skipNext = n;
                        }
                        _frames.Push(new Frame
                        {
                            Kind = FrameKind.Block,
                            Indent = indent + _o.IndentSize,
                            StatementIndent = indent + _o.IndentSize,
                        });
                    }
                }
                Done(t);
            }

            private Frame _pendingBodyFor;
            private int _pendingBodyDepth;
            private SqlToken _skipNext;

            /// <summary>Handles clause keywords. Returns true when the token was written.</summary>
            private bool Clause(SqlToken t, int s, string u, Frame top)
            {
                var prev = Sig(s - 1);
                var next = Sig(s + 1);
                string clause = null;
                var words = 1;

                switch (u)
                {
                    case "SELECT":
                        clause = "SELECT";
                        break;
                    case "FROM":
                        if (prev != null && prev.Is("DELETE")) return false;
                        clause = "FROM";
                        break;
                    case "WHERE":
                    case "HAVING":
                    case "OUTPUT":
                    case "OPTION":
                    case "VALUES":
                    case "WINDOW":
                        if (u == "OUTPUT" && !(top.FirstWord == "INSERT" || top.FirstWord == "UPDATE" || top.FirstWord == "DELETE" || top.FirstWord == "MERGE"))
                            return false;
                        if (u == "OUTPUT" && prev != null && (prev.Kind == TokenKind.Variable || prev.Is("NULL"))) return false;
                        if (u == "VALUES" && top.FirstWord == "MERGE") return false;
                        clause = u;
                        break;
                    case "AS":
                        // CREATE PROCEDURE … AS ⏎ BEGIN / CREATE VIEW … AS ⏎ SELECT
                        if ((top.FirstWord != "CREATE" && top.FirstWord != "ALTER") || next == null || !next.IsWord
                            || !(SqlVocabulary.StatementStarters.Contains(next.Text)))
                            return false;
                        NewLineAt(top.StatementIndent, false);
                        Write(Case(t, s));
                        top.Clause = null;
                        top.AwaitStatement = !next.Is("SELECT") && !next.Is("WITH");
                        Done(t);
                        return true;
                    case "USING":
                        if (top.FirstWord != "MERGE") return false;
                        clause = "USING";
                        break;
                    case "WHEN":
                        if (top.FirstWord != "MERGE") return false;
                        clause = "WHEN";
                        break;
                    case "GROUP":
                    case "ORDER":
                        if (next == null || !next.Is("BY")) return false;
                        clause = u + " BY";
                        words = 2;
                        break;
                    case "UNION":
                    case "EXCEPT":
                    case "INTERSECT":
                        NewLineAt(top.StatementIndent, false);
                        Write(Case(t, s));
                        if (next != null && next.Is("ALL")) { Write(" " + KeywordCase(next.Text)); _skipNext = next; }
                        top.Clause = null;
                        Done(t);
                        return true;
                    case "INTO":
                        if (top.FirstWord != "SELECT" || prev != null && prev.Is("INSERT")) return false;
                        clause = "INTO";
                        break;
                    case "SET":
                        if (top.FirstWord != "UPDATE") return false;
                        clause = "SET";
                        break;
                    case "ON":
                        if (top.Clause == "USING")
                        {
                            clause = "ON";
                            break;
                        }
                        if (top.Clause != "JOIN") return false;
                        NewLineAt(top.JoinIndent + _o.IndentSize, false);
                        Write(Case(t, s));
                        top.Clause = "ON";
                        top.AlignGroup = ++_groups;
                        top.ItemColumn = _col + 1;
                        top.InBetween = false;
                        Done(t);
                        return true;
                    case "AND":
                    case "OR":
                        if (top.InBetween && u == "AND") { top.InBetween = false; return false; }
                        if (!_o.BreakOnAndOr || (top.Clause != "WHERE" && top.Clause != "ON" && top.Clause != "HAVING")) return false;
                        NewLineAt(Math.Max(top.StatementIndent, top.ItemColumn), false);
                        Write(Case(t, s));
                        Done(t);
                        return true;
                    default:
                        if (JoinWords.Contains(u) && IsJoinPhrase(s) && (top.Clause == "FROM" || top.Clause == "JOIN" || top.Clause == "ON"))
                        {
                            var indent = top.StatementIndent + (_o.IndentJoins ? _o.IndentSize : 0);
                            NewLineAt(indent, false);
                            Write(Case(t, s));
                            var k = s + 1;
                            var last = t;
                            while (!last.Is("JOIN") && !last.Is("APPLY") && k < _sig.Count && _sig[k].IsWord && JoinWords.Contains(_sig[k].Text))
                            {
                                Write(" " + KeywordCase(_sig[k].Text));
                                last = _sig[k];
                                _skipUntil = k;
                                k++;
                            }
                            top.Clause = "JOIN";
                            top.AlignGroup = ++_groups;
                            top.JoinIndent = indent;
                            top.ItemColumn = -1;
                            Done(t);
                            return true;
                        }
                        return false;
                }

                NewLineAt(top.StatementIndent, false);
                Write(t.Kind == TokenKind.Identifier ? KeywordCase(t.Text) : Case(t, s));
                if (words == 2) { Write(" " + KeywordCase(next.Text)); _skipNext = next; }
                top.Clause = clause;
                top.AlignGroup = ++_groups;
                top.ItemColumn = _col + 1;
                top.BreakBeforeSig = clause == "SELECT" && _o.BreakAfterDistinctTop ? FirstSelectItem(s) : -1;
                top.InBetween = false;
                Done(t);
                return true;
            }

            private int _skipUntil = -1;

            private bool IsJoinPhrase(int s)
            {
                for (var k = s; k < _sig.Count && k < s + 5; k++)
                {
                    if (_sig[k].Is("JOIN") || _sig[k].Is("APPLY")) return true;
                    if (!_sig[k].IsWord || !JoinWords.Contains(_sig[k].Text)) return false;
                }
                return false;
            }

            private bool IsCaseOpen() => _frames.Any(f => f.Kind == FrameKind.Case) && Top.Kind == FrameKind.Case;

            private void OpenParen(SqlToken t, int s)
            {
                var next = Sig(s + 1);
                var prev = Sig(s - 1);
                var isSubquery = next != null && (next.Is("SELECT") || next.Is("WITH") && Sig(s + 2) != null && Sig(s + 2).IsName);
                var isTableBody = !isSubquery && IsColumnDefinitionList(s);
                var isColumnList = isTableBody || !isSubquery && Top.Kind == FrameKind.ColumnList && WrapConstraintColumns(s);
                if (!InCompact && (isSubquery || isTableBody))
                {
                    var limit = isSubquery ? _o.CollapseSubqueriesShorterThan : _o.CollapseDdlShorterThan;
                    var close = MatchingParen(s);
                    var from = isSubquery ? s : Math.Min(s, StatementFrame().StatementStartSig);
                    if (limit > 0 && close > s && Measure(from, close) < limit) _compactEnd = close;
                }
                var style = isSubquery ? _o.SubqueryParenStyle : _o.DdlParenStyle;
                if ((isSubquery || isColumnList) && style == ParenthesisStyle.ExpandedToStatement && !InCompact)
                    NewLineAt(Top.Kind == FrameKind.ColumnList ? Math.Max(_lineIndent, Top.Indent) : _lineIndent, false); // not at a leading comma
                else
                    SpaceIfNeeded(t, s);
                Write("(");
                var lineIndent = _lineIndent;
                if (isSubquery)
                {
                    var f = new Frame
                    {
                        Kind = FrameKind.Subquery,
                        Indent = lineIndent + _o.IndentSize,
                        StatementIndent = lineIndent + _o.IndentSize,
                        OpenLineIndent = lineIndent,
                        AwaitStatement = true,
                    };
                    _frames.Push(f);
                }
                else if (isColumnList)
                {
                    _frames.Push(new Frame
                    {
                        Kind = FrameKind.ColumnList,
                        Indent = lineIndent + _o.IndentSize,
                        OpenLineIndent = lineIndent,
                        AwaitStatement = false,
                        AlignGroup = ++_groups,
                    });
                    NewLineAt(lineIndent + _o.IndentSize, false);
                }
                else
                {
                    var f = new Frame { Kind = FrameKind.Paren, AwaitStatement = false };
                    if (_o.SpaceInsideInLists && prev != null && prev.Is("IN"))
                    {
                        Write(" ");
                        f.SpaceInside = true;
                    }
                    _frames.Push(f);
                }
                Done(t);
            }

            private void CloseParen(SqlToken t)
            {
                Frame popped = null;
                while (_frames.Count > 1)
                {
                    popped = _frames.Pop();
                    if (popped.Kind == FrameKind.Subquery || popped.Kind == FrameKind.ColumnList || popped.Kind == FrameKind.Paren) break;
                    popped = null;
                }
                if (popped != null && (popped.Kind == FrameKind.Subquery || popped.Kind == FrameKind.ColumnList) && !InCompact)
                    NewLineAt(popped.OpenLineIndent, false);
                if (popped != null && popped.SpaceInside && !_atLineStart) Write(" ");
                Write(")");
                Done(t);
            }

            private void Comma(SqlToken t, int s)
            {
                var top = Top;
                if (top.Kind == FrameKind.ColumnList)
                {
                    PlaceComma(top.Indent);
                    Done(t);
                    return;
                }
                if (top.IsStatementLevel && _o.OneItemPerLine && top.Clause != null && ListClauses.Contains(top.Clause) && top.ItemColumn > 0)
                {
                    PlaceComma(top.ItemColumn);
                    Done(t);
                    return;
                }
                Write(",");
                Done(t);
            }

            private void PlaceComma(int itemColumn)
            {
                if (InCompact)
                {
                    Write(",");
                    return;
                }
                if (_o.LeadingCommas)
                {
                    NewLineAt(Math.Max(0, itemColumn - 2), false);
                    Write(",");
                    _pendingSpaceAfterLeadingComma = true;
                }
                else
                {
                    Write(",");
                    NewLineAt(itemColumn, false);
                }
            }

            private bool _pendingSpaceAfterLeadingComma;

            private bool IsColumnDefinitionList(int s)
            {
                // CREATE TABLE name ( | DECLARE @t TABLE ( | RETURNS @t TABLE (
                var p = Sig(s - 1);
                if (p == null) return false;
                if (p.Is("TABLE")) return true;
                var k = s - 1;
                while (k >= 0 && (_sig[k].IsName || _sig[k].Text == ".")) k--;
                return k >= 1 && k < s - 1 && _sig[k].Is("TABLE") && _sig[k - 1].Is("CREATE");
            }

            // ------------------------------------------------------------------

            private void WriteComment(SqlToken c, int nlBefore)
            {
                var text = c.Text.TrimEnd('\r', '\n');
                var isLineComment = c.Type == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.SingleLineComment;

                if (nlBefore == 0 && !_forceNewLine && _atLineStart && _prevWritten != null && TryUndoLineBreak(out var pendingIndent))
                {
                    // A trailing comment ("a, -- note") stays at the end of the line it was on.
                    if (_o.AlignComments) Mark(CommentMark, CommentGroup());
                    Write(" " + ShiftComment(c, text, _col + 1));
                    NewLineAt(pendingIndent, false);
                    _forceNewLine = false;
                    _prevWritten = c;
                    _afterGo = false;
                    return;
                }

                if (nlBefore > 0 || _forceNewLine)
                {
                    var top = Top;
                    var indent = _lineIndent;
                    if (top.IsStatementLevel && top.AwaitStatement)
                        indent = top.PendingBody ? (top.IfIndent >= 0 ? top.IfIndent : top.Indent) + _o.IndentSize : top.Indent;
                    NewLineAt(indent, nlBefore >= 2);
                }
                else if (!_atLineStart)
                {
                    if (isLineComment && _o.AlignComments) Mark(CommentMark, CommentGroup());
                    Write(" ");
                }
                Write(ShiftComment(c, text, _col));
                _forceNewLine = isLineComment;
                _prevWritten = c;
                _afterGo = false;
            }

            /// <summary>
            /// Moves the continuation lines of a multi-line block comment by as much as its first line moved,
            /// so "/* ⏎  * text ⏎  */" keeps its shape.
            /// </summary>
            private string ShiftComment(SqlToken c, string text, int newColumn)
            {
                if (!_o.AlignMultilineComments || text.IndexOf('\n') < 0) return text;
                var lineStart = _sql.LastIndexOf('\n', Math.Max(0, c.Start - 1)) + 1;
                var oldColumn = 0;
                for (var i = lineStart; i < c.Start; i++) oldColumn += _sql[i] == '\t' ? _o.IndentSize : 1;
                var delta = newColumn - oldColumn;
                if (delta == 0) return text;

                var lines = text.Split('\n');
                for (var i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var body = line.TrimStart(' ', '\t');
                    if (body.Length == 0 || body == "\r") continue;
                    var indent = 0;
                    foreach (var ch in line.Substring(0, line.Length - body.Length)) indent += ch == '\t' ? _o.IndentSize : 1;
                    lines[i] = new string(' ', Math.Max(0, indent + delta)) + body;
                }
                return string.Join("\n", lines);
            }

            /// <summary>Removes the line break just emitted, returning the indent it had.</summary>
            private bool TryUndoLineBreak(out int indent)
            {
                indent = _lineIndent;
                var tail = _o.NewLine + new string(' ', _col);
                if (_sb.Length < tail.Length || _sb.ToString(_sb.Length - tail.Length, tail.Length) != tail) return false;
                _sb.Length -= tail.Length;
                _col = CurrentColumn();
                _lineIndent = CurrentLineIndent();
                _atLineStart = false;
                return true;
            }

            private int CurrentColumn()
            {
                var i = _sb.Length - 1;
                var n = 0;
                while (i >= 0 && _sb[i] != '\n')
                {
                    if (!IsMarker(_sb[i])) n++;
                    i--;
                }
                return n;
            }

            private int CurrentLineIndent()
            {
                var i = _sb.Length;
                while (i > 0 && _sb[i - 1] != '\n') i--;
                var n = 0;
                while (i + n < _sb.Length && _sb[i + n] == ' ') n++;
                return n;
            }

            private void Done(SqlToken t)
            {
                var before = _prevSig;
                _prevUnary = (t.Text == "-" || t.Text == "+" || t.Text == "~")
                             && (before == null || before.Kind == TokenKind.Operator || before.Text == "(" || before.Text == ","
                                 || before.Kind == TokenKind.Keyword && !before.Is("END") && !before.Is("NULL"));
                _prevSig = t;
                _prevWritten = t;
                if (_pendingBodyFor != null && _frames.Count == _pendingBodyDepth && t != null)
                {
                    // IF/WHILE condition ends when the next token at this level begins a statement.
                    var s = _sigIndex[t];
                    if (_statementStarts.Contains(s + 1) || Sig(s + 1) != null && Sig(s + 1).Is("BEGIN"))
                    {
                        _pendingBodyFor.PendingBody = true;
                        _pendingBodyFor.AwaitStatement = true;
                        _pendingBodyFor = null;
                    }
                }
            }

            // ------------------------------------------------------------------

            private void SpaceIfNeeded(SqlToken t, int s)
            {
                if (_atLineStart) return;
                if (_sb.Length > 0 && _sb[_sb.Length - 1] == ' ') return;
                if (_pendingSpaceAfterLeadingComma) { _pendingSpaceAfterLeadingComma = false; Write(" "); return; }
                var p = _prevWritten;
                if (p == null) return;
                if (p.Kind == TokenKind.Comment) { Write(" "); return; }
                var pt = p.Text;
                var tt = t.Text;
                if (tt == "," || tt == ";" || tt == ")" || tt == "." || tt == "::") return;
                if (pt == "(" || pt == "." || pt == "::") return;
                if (_prevUnary) return;
                if (tt == "(")
                {
                    if (p.IsName && !IsObjectNameBeforeParen(s) && !(p.Kind == TokenKind.Identifier && IsSoftKeyword(p, _sigIndex[p]))) return;
                    if (p.Kind == TokenKind.Keyword && SqlVocabulary.FunctionNames.Contains(pt)) return;
                    if (p.Kind == TokenKind.Keyword && (p.Is("LEFT") || p.Is("RIGHT") || p.Is("CONVERT") || p.Is("COALESCE")
                                                        || p.Is("NULLIF") || p.Is("USER_NAME") || p.Is("SESSION_USER") || p.Is("IDENTITY")))
                        return;
                }
                // Comparison operators the lexer splits into two tokens: <>, >=, <=, !=, !<, !>.
                if ((pt == "<" || pt == ">" || pt == "!") && (tt == ">" || tt == "=" || tt == "<") && Adjacent(p, t)) return;
                Write(" ");
            }

            private bool IsObjectNameBeforeParen(int s)
            {
                // INSERT INTO t (…), CREATE TABLE t (…), WITH cte (…) AS, table-valued aliases: keep a space.
                var k = s - 1;
                while (k >= 0 && (_sig[k].IsName || _sig[k].Text == ".")) k--;
                if (k < 0) return false;
                var w = _sig[k];
                if (w.Is("INTO") || w.Is("TABLE") || w.Is("WITH") || w.Is("INSERT") || w.Is("VIEW") || w.Is("ON")
                    || w.Is("REFERENCES") || w.Is("INDEX"))
                    return true;
                if (w.Text == "," && _frames.Any(f => f.FirstWord == "WITH")) return true;
                return w.Is("AS") && k + 1 < s - 1; // "AS alias (cols)" in derived tables
            }

            private static bool Adjacent(SqlToken a, SqlToken b) => a.End == b.Start;

            // ------------------------------------------------------------------

            private string Case(SqlToken t, int s)
            {
                if (t.Kind == TokenKind.Keyword || t.Kind == TokenKind.Go)
                {
                    var next = Sig(s + 1);
                    if (SqlVocabulary.FunctionNames.Contains(t.Text) && next != null && next.Text == "(") return Apply(t.Text, _o.FunctionCase);
                    return KeywordCase(t.Text);
                }
                if (t.Kind == TokenKind.Identifier)
                {
                    var next = Sig(s + 1);
                    var prev = Sig(s - 1);
                    if (next != null && next.Text == "(" && SqlVocabulary.FunctionNames.Contains(t.Text) && (prev == null || prev.Text != "."))
                        return Apply(t.Text, _o.FunctionCase);
                    if (SqlVocabulary.IsDataType(t.Text) && IsTypePosition(s)) return Apply(t.Text, _o.DataTypeCase);
                    if (IsSoftKeyword(t, s)) return KeywordCase(t.Text);
                    if (_names != null && _names.TryGetValue(t.Text, out var defined)) return defined;
                }
                if (t.Kind == TokenKind.Variable && GlobalVariables.Contains(t.Text)) return Apply(t.Text, _o.GlobalVariableCase);
                return t.Text;
            }

            private string KeywordCase(string text) => Apply(text, _o.KeywordCase);

            private static string Apply(string text, KeywordCasing casing)
            {
                switch (casing)
                {
                    case KeywordCasing.Upper: return text.ToUpperInvariant();
                    case KeywordCasing.Lower: return text.ToLowerInvariant();
                    default: return text;
                }
            }

            private bool IsTypePosition(int s)
            {
                var prev = Sig(s - 1);
                var next = Sig(s + 1);
                if (prev == null) return false;
                if (next != null && next.Text == ".") return false;
                if (prev.Text == ".") return false;
                if (prev.Kind == TokenKind.Variable) return true;
                if (prev.Is("RETURNS")) return true;
                if (prev.Is("AS"))
                {
                    var pp = Sig(s - 2);
                    return pp != null && (pp.Kind == TokenKind.Variable || InsideCall(s, "CAST") || InsideCall(s, "TRY_CAST"));
                }
                if (prev.Text == "(" && (InsideCall(s, "CONVERT") || InsideCall(s, "TRY_CONVERT"))) return true;
                if (Top.Kind == FrameKind.ColumnList && prev.IsName)
                {
                    var pp = Sig(s - 2);
                    return pp != null && (pp.Text == "(" || pp.Text == ",");
                }
                return false;
            }

            private bool InsideCall(int s, string fn)
            {
                var depth = 0;
                for (var k = s - 1; k >= 0; k--)
                {
                    if (_sig[k].Text == ")") depth++;
                    else if (_sig[k].Text == "(")
                    {
                        if (depth == 0) return k > 0 && _sig[k - 1].Is(fn);
                        depth--;
                    }
                }
                return false;
            }

            private bool IsSoftKeyword(SqlToken t, int s)
            {
                var prev = Sig(s - 1);
                var next = Sig(s + 1);
                var u = t.Upper;
                if (prev != null && prev.Text == ".") return false;
                if (next != null && next.Text == ".") return false;
                switch (u)
                {
                    case "TRY":
                    case "CATCH":
                        return prev != null && (prev.Is("BEGIN") || prev.Is("END"));
                    case "NOCOUNT":
                    case "XACT_ABORT":
                    case "ANSI_NULLS":
                    case "QUOTED_IDENTIFIER":
                    case "ARITHABORT":
                    case "ANSI_WARNINGS":
                    case "DEADLOCK_PRIORITY":
                    case "LOCK_TIMEOUT":
                    case "STATISTICS":
                        return prev != null && prev.Is("SET");
                    case "APPLY":
                        return prev != null && (prev.Is("CROSS") || prev.Is("OUTER"));
                    case "PARTITION":
                        return next != null && next.Is("BY");
                    case "ROWS":
                    case "ROW":
                        return prev != null && (prev.Kind == TokenKind.NumberLiteral || prev.Kind == TokenKind.Variable || prev.Text == ")");
                    case "ONLY":
                        return prev != null && (prev.Is("ROWS") || prev.Is("ROW"));
                    case "NEXT":
                    case "FIRST":
                        return prev != null && prev.Is("FETCH");
                    case "TIES":
                        return prev != null && prev.Is("WITH");
                    case "NOLOCK":
                    case "READPAST":
                    case "UPDLOCK":
                    case "ROWLOCK":
                    case "HOLDLOCK":
                    case "TABLOCK":
                    case "TABLOCKX":
                    case "READUNCOMMITTED":
                        return prev != null && (prev.Text == "(" || prev.Text == ",") && _frames.Count > 1;
                    case "MATCHED":
                    case "TARGET":
                    case "SOURCE":
                        return prev != null && (prev.Is("NOT") || prev.Is("WHEN") || prev.Is("BY"));
                    case "OUTPUT":
                    case "OUT":
                    case "READONLY":
                        return prev != null && (next == null || next.Text == "," || next.Text == ")" || next.Text == ";"
                                                || next.Is("AS") || next.Kind == TokenKind.Variable || next.Is("WITH"));
                    case "OFFSET":
                        var after = Sig(s + 2);
                        return after != null && (after.Is("ROWS") || after.Is("ROW"));
                    case "USING":
                        return _frames.Any(f => f.FirstWord == "MERGE");
                    case "THROW":
                        return Top.IsStatementLevel && (prev == null || prev.Text == ";" || prev.IsWord);
                    default:
                        return false;
                }
            }

            // ------------------------------------------------------------------

            private void NewLineAt(int indent, bool blank, bool allowAtStart = false)
            {
                if (_afterGo && !_o.KeepBlankLinesAfterGo) blank = false;
                if (_sb.Length == _baseIndent.Length && _prevWritten == null && !allowAtStart)
                {
                    // Very first token: stay on the first line.
                    return;
                }
                if (InCompact)
                {
                    // Inside a collapsed region a line break becomes a single space.
                    if (_atLineStart) return;
                    TrimLineEnd();
                    _col = CurrentColumn();
                    if (_sb.Length > 0 && _sb[_sb.Length - 1] != '(') Write(" ");
                    return;
                }
                if (_atLineStart)
                {
                    // Re-indent the empty current line.
                    var lineStart = _sb.Length - _col;
                    _sb.Length = lineStart;
                    if (blank && _o.MaxBlankLines > 0 && !EndsWithBlankLine()) _sb.Append(_o.NewLine);
                    _sb.Append(' ', indent);
                    _col = _lineIndent = indent;
                    return;
                }
                TrimLineEnd();
                _sb.Append(_o.NewLine);
                if (blank && _o.MaxBlankLines > 0) _sb.Append(_o.NewLine);
                _sb.Append(' ', indent);
                _col = _lineIndent = indent;
                _atLineStart = true;
            }

            private bool EndsWithBlankLine()
            {
                var s = _sb.ToString();
                var nl = _o.NewLine;
                return s.EndsWith(nl + nl, StringComparison.Ordinal) || s.Length == 0;
            }

            private void TrimLineEnd()
            {
                while (_sb.Length > 0 && _sb[_sb.Length - 1] == ' ') _sb.Length--;
            }

            private void Write(string text)
            {
                _sb.Append(text);
                var nl = text.LastIndexOf('\n');
                _col = nl >= 0 ? text.Length - nl - 1 : _col + text.Length;
                if (text.Length > 0 && text != " ") _atLineStart = false;
            }

            private static string TrimTrailingSpaces(string s)
            {
                var lines = s.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var l = lines[i];
                    var cr = l.EndsWith("\r", StringComparison.Ordinal);
                    l = l.TrimEnd('\r').TrimEnd(' ', '\t');
                    lines[i] = cr ? l + "\r" : l;
                }
                return string.Join("\n", lines).TrimEnd(' ', '\t', '\r', '\n');
            }

            // ----- SELECT modifiers and DDL constraints -----

            /// <summary>Index of the first select item when DISTINCT/ALL/TOP (n) [PERCENT] [WITH TIES] precede it, else -1.</summary>
            private int FirstSelectItem(int select)
            {
                var k = select + 1;
                if (Sig(k) != null && (Sig(k).Is("DISTINCT") || Sig(k).Is("ALL"))) k++;
                if (Sig(k) != null && Sig(k).Is("TOP"))
                {
                    k++;
                    if (Sig(k) != null && Sig(k).Text == "(")
                    {
                        k = MatchingParen(k);
                        if (k < 0) return -1;
                    }
                    k++;
                    if (Sig(k) != null && Sig(k).Is("PERCENT")) k++;
                    if (Sig(k) != null && Sig(k).Is("WITH") && Sig(k + 1) != null && Sig(k + 1).Is("TIES")) k += 2;
                }
                return k > select + 1 && k < _sig.Count ? k : -1;
            }

            /// <summary>A keyword that begins a column constraint inside a CREATE TABLE column definition.</summary>
            private bool StartsColumnConstraint(int s)
            {
                var t = _sig[s];
                if (!ConstraintStarters.Contains(t.Text)) return false;
                var prev = Sig(s - 1);
                if (prev == null || prev.Text == "(" || prev.Text == ",") return false; // a table constraint is its own item
                if (t.Is("CONSTRAINT")) return true;
                var before = Sig(s - 2);
                if (before != null && before.Is("CONSTRAINT")) return false;            // CONSTRAINT name PRIMARY KEY
                // FOREIGN KEY (a) REFERENCES t (b) is one constraint.
                var depth = 0;
                for (var k = s - 1; k >= 0; k--)
                {
                    var w = _sig[k];
                    if (w.Text == ")") depth++;
                    else if (w.Text == "(")
                    {
                        if (depth == 0) break;
                        depth--;
                    }
                    else if (depth == 0 && w.Text == ",") break;
                    else if (depth == 0 && w.Is("FOREIGN")) return false;
                }
                return true;
            }

            /// <summary>PRIMARY KEY / UNIQUE / REFERENCES t / INDEX ix ( … ) inside CREATE TABLE that should go one column per line.</summary>
            private bool WrapConstraintColumns(int s)
            {
                if (_o.ConstraintColumnsOnNewLines == ListWrap.Never) return false;
                var prev = Sig(s - 1);
                if (prev == null) return false;
                var owner = prev.Is("KEY") || prev.Is("UNIQUE") || prev.Is("CLUSTERED") || prev.Is("NONCLUSTERED");
                if (!owner && prev.IsName)
                {
                    var k = s - 1;
                    while (k >= 0 && (_sig[k].IsName || _sig[k].Text == ".")) k--;
                    owner = k >= 0 && (_sig[k].Is("REFERENCES") || _sig[k].Is("INDEX"));
                }
                if (!owner) return false;
                if (_o.ConstraintColumnsOnNewLines == ListWrap.Always) return true;
                var depth = 0;
                for (var k = s; k < _sig.Count; k++)
                {
                    var w = _sig[k].Text;
                    if (w == "(") depth++;
                    else if (w == ")" && --depth == 0) return false;
                    else if (w == "," && depth == 1) return true;
                }
                return false;
            }

            // ----- collapsing -----

            private Frame StatementFrame() => _frames.First(f => f.IsStatementLevel);

            /// <summary>Last significant index of the statement starting at <paramref name="s"/>.</summary>
            private int StatementEnd(int s)
            {
                var depth = 0;
                var caseDepth = 0;
                for (var k = s; k < _sig.Count; k++)
                {
                    var t = _sig[k];
                    if (k > s && depth == 0 && (_statementStarts.Contains(k) || t.Kind == TokenKind.Go)) return k - 1;
                    if (t.Text == "(") depth++;
                    else if (t.Text == ")")
                    {
                        if (depth == 0) return k - 1;
                        depth--;
                    }
                    else if (t.Text == ";" && depth == 0) return k;
                    else if (t.Kind == TokenKind.Keyword)
                    {
                        if (t.Is("CASE")) caseDepth++;
                        else if (t.Is("END") && caseDepth > 0) caseDepth--;
                        else if (k > s && depth == 0 && caseDepth == 0 && (t.Is("END") || t.Is("ELSE") || t.Is("BEGIN"))) return k - 1;
                    }
                }
                return _sig.Count - 1;
            }

            private int MatchingParen(int s)
            {
                var depth = 0;
                for (var k = s; k < _sig.Count; k++)
                {
                    if (_sig[k].Text == "(") depth++;
                    else if (_sig[k].Text == ")" && --depth == 0) return k;
                }
                return -1;
            }

            private int MatchingCaseEnd(int s)
            {
                var depth = 0;
                for (var k = s; k < _sig.Count; k++)
                {
                    if (_sig[k].Kind != TokenKind.Keyword) continue;
                    if (_sig[k].Is("CASE")) depth++;
                    else if (_sig[k].Is("END") && --depth == 0) return k;
                }
                return -1;
            }

            /// <summary>Length of tokens a..b written on one line; int.MaxValue when a comment is in between.</summary>
            private int Measure(int a, int b)
            {
                if (b < a) return int.MaxValue;
                for (var k = _allIndex[_sig[a]]; k <= _allIndex[_sig[b]]; k++)
                    if (_all[k].Kind == TokenKind.Comment) return int.MaxValue;
                var n = 0;
                for (var k = a; k <= b; k++)
                {
                    if (k > a && NeedsSpace(_sig[k - 1], _sig[k])) n++;
                    n += _sig[k].Text.Length;
                }
                return n;
            }

            private static bool NeedsSpace(SqlToken p, SqlToken t)
            {
                var tt = t.Text;
                var pt = p.Text;
                if (tt == "," || tt == ";" || tt == ")" || tt == "." || tt == "::") return false;
                if (pt == "(" || pt == "." || pt == "::") return false;
                return !(tt == "(" && (p.IsName || p.Kind == TokenKind.Keyword && SqlVocabulary.FunctionNames.Contains(pt)));
            }

            // ----- alignment -----

            private int CommentGroup() => Top.AlignGroup != 0 ? Top.AlignGroup : StatementFrame().AlignGroup;

            private void Mark(char kind, int group)
            {
                if (!_markersAllowed || InCompact || _atLineStart) return;
                _sb.Append(kind).Append((char)(GroupBase + group % GroupCount));
            }

            /// <summary>An AS or bare alias after an item of a one-item-per-line select list.</summary>
            private bool IsSelectAlias(SqlToken t, int s, Frame top)
            {
                if (!top.IsStatementLevel || top.Clause != "SELECT" || !_o.OneItemPerLine || InCompact) return false;
                var prev = Sig(s - 1);
                if (prev == null || prev.Is("SELECT")) return false;
                if (t.Kind == TokenKind.Keyword) return t.Is("AS");
                if (!t.IsName) return false;
                var next = Sig(s + 1);
                if (next != null && next.Text != "," && next.Text != ";" && !(next.IsWord && AliasFollowers.Contains(next.Text))
                    && !_statementStarts.Contains(s + 1))
                    return false;
                if (prev.Is("END")) return true;
                if (prev.Kind == TokenKind.Keyword || prev.Kind == TokenKind.Operator || prev.Text == "," || prev.Text == ".") return false;
                var before = Sig(s - 2);
                if (prev.Kind == TokenKind.NumberLiteral && before != null && before.Is("TOP")) return false;
                if (prev.Text == ")")
                {
                    // TOP (10) col is not an alias.
                    var depth = 0;
                    for (var k = s - 1; k >= 0; k--)
                    {
                        if (_sig[k].Text == ")") depth++;
                        else if (_sig[k].Text == "(" && --depth == 0) return !(k > 0 && _sig[k - 1].Is("TOP"));
                    }
                    return false;
                }
                return prev.IsName || prev.Kind == TokenKind.Variable || prev.Kind == TokenKind.StringLiteral || prev.Kind == TokenKind.NumberLiteral;
            }

            private static string ResolveAlignment(string text)
            {
                if (text.IndexOf(AliasMark) < 0 && text.IndexOf(CommentMark) < 0) return text;
                var lines = text.Split('\n');
                Align(lines, AliasMark);
                Align(lines, CommentMark);
                return string.Join("\n", lines);
            }

            /// <summary>Replaces each marker of <paramref name="kind"/> by the padding that lines up its group.</summary>
            private static void Align(string[] lines, char kind)
            {
                var max = new Dictionary<char, int>();
                var count = new Dictionary<char, int>();
                foreach (var line in lines)
                {
                    for (var i = line.IndexOf(kind); i >= 0 && i + 1 < line.Length; i = line.IndexOf(kind, i + 2))
                    {
                        var g = line[i + 1];
                        var col = VisibleLength(line, i);
                        max[g] = max.TryGetValue(g, out var m) ? Math.Max(m, col) : col;
                        count[g] = count.TryGetValue(g, out var c) ? c + 1 : 1;
                    }
                }
                for (var l = 0; l < lines.Length; l++)
                {
                    var line = lines[l];
                    for (var i = line.IndexOf(kind); i >= 0 && i + 1 < line.Length; i = line.IndexOf(kind))
                    {
                        var g = line[i + 1];
                        var pad = count[g] > 1 ? max[g] - VisibleLength(line, i) : 0;
                        line = line.Substring(0, i) + new string(' ', pad) + line.Substring(i + 2);
                    }
                    lines[l] = line;
                }
            }

            private static int VisibleLength(string line, int end)
            {
                var n = 0;
                for (var i = 0; i < end; i++)
                    if (!IsMarker(line[i]) && line[i] != '\r') n++;
                return n;
            }

            // Tokens consumed as part of a multi-word keyword are skipped by the main loop.
            public bool ShouldSkip(SqlToken t, int s)
            {
                if (_skipNext == t) { _skipNext = null; _prevSig = t; _prevWritten = t; return true; }
                if (s <= _skipUntil) { _prevSig = t; _prevWritten = t; return true; }
                return false;
            }
        }
    }
}
