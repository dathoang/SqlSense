using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlSense.Core.Lexing
{
    public enum TokenKind
    {
        Keyword,
        Identifier,
        QuotedIdentifier,
        Variable,
        StringLiteral,
        NumberLiteral,
        Comment,
        WhiteSpace,
        Punctuation,
        Operator,
        Go,
        Other,
    }

    public sealed class SqlToken
    {
        public SqlToken(TSqlTokenType type, TokenKind kind, string text, int start)
        {
            Type = type;
            Kind = kind;
            Text = text;
            Start = start;
        }

        public TSqlTokenType Type { get; }
        public TokenKind Kind { get; }
        public string Text { get; }
        public int Start { get; }
        public int End => Start + Text.Length;
        public string Upper => Text.ToUpperInvariant();

        public bool IsTrivia => Kind == TokenKind.WhiteSpace || Kind == TokenKind.Comment;
        public bool IsWord => Kind == TokenKind.Keyword || Kind == TokenKind.Identifier;
        public bool IsName => Kind == TokenKind.Identifier || Kind == TokenKind.QuotedIdentifier;

        public bool Is(string upperWord) => IsWord && string.Equals(Text, upperWord, StringComparison.OrdinalIgnoreCase);

        /// <summary>Identifier text without [] or "" delimiters.</summary>
        public string Unquoted
        {
            get
            {
                if (Kind != TokenKind.QuotedIdentifier || Text.Length < 2) return Text;
                var inner = Text.Substring(1, Text.Length - 2);
                return Text[0] == '[' ? inner.Replace("]]", "]") : inner.Replace("\"\"", "\"");
            }
        }

        public override string ToString() => $"{Kind}:{Text}@{Start}";
    }

    /// <summary>
    /// Tokenizes T-SQL with the ScriptDom lexer. The lexer tolerates incomplete scripts, which is
    /// what an editor has while the user is typing, so this is used instead of a full parse.
    /// </summary>
    public static class SqlLexer
    {
        public static List<SqlToken> Tokenize(string text)
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            IList<ParseError> errors;
            var raw = parser.GetTokenStream(new StringReader(text ?? string.Empty), out errors);
            var result = new List<SqlToken>(raw.Count);
            foreach (var t in raw)
            {
                if (t.TokenType == TSqlTokenType.EndOfFile || string.IsNullOrEmpty(t.Text)) continue;
                result.Add(new SqlToken(t.TokenType, Classify(t.TokenType), t.Text, t.Offset));
            }
            return result;
        }

        /// <summary>True when the lexer could not tokenize the whole script (e.g. unterminated string).</summary>
        public static bool HasLexErrors(string text)
        {
            var parser = new TSql170Parser(true);
            IList<ParseError> errors;
            var raw = parser.GetTokenStream(new StringReader(text ?? string.Empty), out errors);
            if (errors != null && errors.Count > 0) return true;
            var covered = 0;
            foreach (var t in raw) if (t.Text != null) covered += t.Text.Length;
            return covered != (text ?? string.Empty).Length;
        }

        private static TokenKind Classify(TSqlTokenType type)
        {
            switch (type)
            {
                case TSqlTokenType.WhiteSpace:
                    return TokenKind.WhiteSpace;
                case TSqlTokenType.SingleLineComment:
                case TSqlTokenType.MultilineComment:
                    return TokenKind.Comment;
                case TSqlTokenType.Identifier:
                    return TokenKind.Identifier;
                case TSqlTokenType.QuotedIdentifier:
                    return TokenKind.QuotedIdentifier;
                case TSqlTokenType.Variable:
                    return TokenKind.Variable;
                case TSqlTokenType.AsciiStringLiteral:
                case TSqlTokenType.UnicodeStringLiteral:
                case TSqlTokenType.AsciiStringOrQuotedIdentifier:
                    return TokenKind.StringLiteral;
                case TSqlTokenType.Integer:
                case TSqlTokenType.Numeric:
                case TSqlTokenType.Real:
                case TSqlTokenType.Money:
                case TSqlTokenType.HexLiteral:
                    return TokenKind.NumberLiteral;
                case TSqlTokenType.Go:
                    return TokenKind.Go;
                case TSqlTokenType.Dot:
                case TSqlTokenType.Comma:
                case TSqlTokenType.LeftParenthesis:
                case TSqlTokenType.RightParenthesis:
                case TSqlTokenType.Semicolon:
                case TSqlTokenType.LeftCurly:
                case TSqlTokenType.RightCurly:
                case TSqlTokenType.DoubleColon:
                    return TokenKind.Punctuation;
                case TSqlTokenType.Star:
                case TSqlTokenType.EqualsSign:
                case TSqlTokenType.Plus:
                case TSqlTokenType.Minus:
                case TSqlTokenType.Divide:
                case TSqlTokenType.PercentSign:
                case TSqlTokenType.GreaterThan:
                case TSqlTokenType.LessThan:
                case TSqlTokenType.Bang:
                case TSqlTokenType.Ampersand:
                case TSqlTokenType.VerticalLine:
                case TSqlTokenType.Circumflex:
                case TSqlTokenType.Tilde:
                case TSqlTokenType.AddEquals:
                case TSqlTokenType.SubtractEquals:
                case TSqlTokenType.MultiplyEquals:
                case TSqlTokenType.DivideEquals:
                case TSqlTokenType.ModEquals:
                case TSqlTokenType.BitwiseAndEquals:
                case TSqlTokenType.BitwiseOrEquals:
                case TSqlTokenType.BitwiseXorEquals:
                case TSqlTokenType.Concat:
                case TSqlTokenType.ConcatEquals:
                case TSqlTokenType.RightOuterJoin:
                    return TokenKind.Operator;
                case TSqlTokenType.Colon:
                case TSqlTokenType.ProcNameSemicolon:
                    return TokenKind.Punctuation;
                case TSqlTokenType.SqlCommandIdentifier:
                case TSqlTokenType.Label:
                    return TokenKind.Other;
                default:
                    return IsKeywordType(type) ? TokenKind.Keyword : TokenKind.Other;
            }
        }

        private static bool IsKeywordType(TSqlTokenType type)
        {
            // Reserved keywords have their own enum members, named after the keyword.
            var name = type.ToString();
            return name.Length > 0 && char.IsLetter(name[0]) && type != TSqlTokenType.Label
                && type != TSqlTokenType.Variable && type != TSqlTokenType.None;
        }
    }
}
