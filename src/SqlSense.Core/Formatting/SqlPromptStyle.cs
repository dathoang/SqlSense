using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SqlSense.Core.Formatting
{
    public sealed class StyleImportResult
    {
        public FormatOptions Options { get; set; }

        /// <summary>"path = value → what it set" for every setting that was used.</summary>
        public List<string> Applied { get; } = new List<string>();

        /// <summary>"path = value" for settings SqlSense has no equivalent for.</summary>
        public List<string> Ignored { get; } = new List<string>();
    }

    /// <summary>
    /// Reads a SQL Prompt formatting style (.json, as saved under %LOCALAPPDATA%\Red Gate\SQL Prompt N\Styles)
    /// and maps the settings SqlSense supports onto <see cref="FormatOptions"/>. A style file only lists the
    /// settings that differ from SQL Prompt's defaults, so settings it doesn't mention keep their current value.
    /// </summary>
    public static class SqlPromptStyle
    {
        private const int DefaultCollapseLength = 75;

        public static StyleImportResult Import(string json, FormatOptions current)
        {
            var root = new JsonReader(json).ReadValue() as Dictionary<string, object>
                       ?? throw new FormatException("The file is not a SQL Prompt style (expected a JSON object).");
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Flatten(root, null, values);

            var o = (current ?? new FormatOptions()).Clone();
            var result = new StyleImportResult { Options = o };
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Use(string path, string what)
            {
                used.Add(path);
                result.Applied.Add($"{path} = {Show(values[path])} → {what}");
            }

            // Leave the value as is: SqlSense already lays it out this way.
            void Same(string path, params string[] accepted)
            {
                if (!values.TryGetValue(path, out var v)) return;
                foreach (var a in accepted)
                    if (string.Equals(v as string, a, StringComparison.OrdinalIgnoreCase) || v is bool b && a == (b ? "true" : "false"))
                    {
                        Use(path, "already SqlSense's layout");
                        return;
                    }
            }

            foreach (var key in new[] { "metadata.id", "metadata.name" })
                if (values.ContainsKey(key)) used.Add(key);

            Casing(values, "casing.reservedKeywords", c => o.KeywordCase = c, "Keyword case", Use);
            Casing(values, "casing.builtInFunctions", c => o.FunctionCase = c, "Function case", Use);
            Casing(values, "casing.builtInDataTypes", c => o.DataTypeCase = c, "Data type case", Use);

            if (Bool(values, "lists.placeCommasBeforeItems") is bool commas)
            {
                o.LeadingCommas = commas;
                Use("lists.placeCommasBeforeItems", "Leading commas = " + commas);
            }
            if (Bool(values, "lists.alignAliases") is bool aliases)
            {
                o.AlignAliases = aliases;
                Use("lists.alignAliases", "Align aliases = " + aliases);
            }
            if (Bool(values, "lists.alignComments") is bool comments)
            {
                o.AlignComments = comments;
                Use("lists.alignComments", "Align comments = " + comments);
            }
            Same("lists.alignItemsAcrossClauses", "false");

            Collapse(values, "dml.collapseShortStatements", "dml.collapseStatementsShorterThan", o.CollapseStatementsShorterThan,
                n => o.CollapseStatementsShorterThan = n, "Collapse statements shorter than", Use);
            Collapse(values, "dml.collapseShortSubqueries", "dml.collapseSubqueriesShorterThan", o.CollapseSubqueriesShorterThan,
                n => o.CollapseSubqueriesShorterThan = n, "Collapse subqueries shorter than", Use);
            Collapse(values, "caseExpressions.collapseShortCaseExpressions", "caseExpressions.collapseCaseExpressionsShorterThan", o.CollapseCaseShorterThan,
                n => o.CollapseCaseShorterThan = n, "Collapse CASE shorter than", Use);
            Collapse(values, "ddl.collapseShortStatements", "ddl.collapseStatementsShorterThan", o.CollapseDdlShorterThan,
                n => o.CollapseDdlShorterThan = n, "Collapse CREATE TABLE shorter than", Use);

            Paren(values, "parentheses.parenthesisStyle", s => o.SubqueryParenStyle = s, "Subquery parentheses", Use);
            Paren(values, "ddl.parenthesisStyle", s => o.DdlParenStyle = s, "CREATE TABLE parentheses", Use);
            Same("parentheses.indentParenthesesContents", "true");
            Same("ddl.indentParenthesesContents", "true");

            if (values.TryGetValue("joinStatements.join.keywordAlignment", out var join) && join is string joinAlign)
            {
                o.IndentJoins = !joinAlign.Equals("toFrom", StringComparison.OrdinalIgnoreCase)
                                && !joinAlign.Equals("leftAligned", StringComparison.OrdinalIgnoreCase);
                Use("joinStatements.join.keywordAlignment", "Indent JOINs = " + o.IndentJoins);
            }
            Same("joinStatements.join.indentJoinTable", "false");
            Same("joinStatements.on.keywordAlignment", "indented");

            if (values.TryGetValue("operators.andOr.alignment", out var andOr) && andOr is string)
            {
                o.BreakOnAndOr = true;
                Use("operators.andOr.alignment", "New line before AND / OR = True");
            }
            Same("operators.between.placeOnNewLine", "false");
            Same("operators.in.placeFirstValueOnNewLine", "never");
            Same("operators.in.placeSubsequentValuesOnNewLines", "never");
            if (Bool(values, "operators.in.addSpaceAroundInContents") is bool inSpace)
            {
                o.SpaceInsideInLists = inSpace;
                Use("operators.in.addSpaceAroundInContents", "Spaces inside IN ( … ) = " + inSpace);
            }

            Same("insertStatements.columns.placeSubsequentColumnsOnNewLines", "never");
            Same("insertStatements.values.parenthesisStyle", "compactIndented", "compactSimple");
            Same("insertStatements.values.indentContents", "true");

            foreach (var kv in values)
                if (!used.Contains(kv.Key))
                    result.Ignored.Add($"{kv.Key} = {Show(kv.Value)}");
            return result;
        }

        private static void Casing(Dictionary<string, object> values, string path, Action<KeywordCasing> set, string name, Action<string, string> use)
        {
            if (!values.TryGetValue(path, out var v) || !(v is string s)) return;
            var lower = s.ToLowerInvariant();
            KeywordCasing casing;
            if (lower.StartsWith("lower", StringComparison.Ordinal)) casing = KeywordCasing.Lower;
            else if (lower.StartsWith("upper", StringComparison.Ordinal)) casing = KeywordCasing.Upper;
            else if (lower.Contains("asis") || lower.Contains("leave")) casing = KeywordCasing.AsIs;
            else return; // camel/Pascal case: no equivalent, reported as ignored
            set(casing);
            use(path, name + " = " + casing);
        }

        private static void Collapse(Dictionary<string, object> values, string flagPath, string lengthPath, int current,
                                     Action<int> set, string name, Action<string, string> use)
        {
            var flag = Bool(values, flagPath);
            var length = values.TryGetValue(lengthPath, out var l) && l is double d ? (int)d : (int?)null;
            if (flag == null && length == null) return;
            var enabled = flag ?? current > 0;
            var value = enabled ? length ?? (current > 0 ? current : DefaultCollapseLength) : 0;
            set(value);
            if (flag != null) use(flagPath, name + " = " + value);
            if (length != null) use(lengthPath, name + " = " + value);
        }

        private static void Paren(Dictionary<string, object> values, string path, Action<ParenthesisStyle> set, string name, Action<string, string> use)
        {
            if (!values.TryGetValue(path, out var v) || !(v is string s)) return;
            if (s.Equals("expandedSplit", StringComparison.OrdinalIgnoreCase)) set(ParenthesisStyle.ExpandedSplit);
            else if (s.Equals("expandedToStatement", StringComparison.OrdinalIgnoreCase)) set(ParenthesisStyle.ExpandedToStatement);
            else return;
            use(path, name + " = " + s);
        }

        private static bool? Bool(Dictionary<string, object> values, string path) =>
            values.TryGetValue(path, out var v) && v is bool b ? b : (bool?)null;

        private static string Show(object v) =>
            v is string s ? s : v is bool b ? (b ? "true" : "false") : v is double d ? d.ToString(CultureInfo.InvariantCulture) : v == null ? "null" : "[…]";

        private static void Flatten(Dictionary<string, object> obj, string prefix, Dictionary<string, object> into)
        {
            foreach (var kv in obj)
            {
                var path = prefix == null ? kv.Key : prefix + "." + kv.Key;
                if (kv.Value is Dictionary<string, object> child) Flatten(child, path, into);
                else into[path] = kv.Value;
            }
        }

        /// <summary>Just enough JSON for style files: objects, arrays, strings, numbers, booleans, null.</summary>
        private sealed class JsonReader
        {
            private readonly string _s;
            private int _i;

            public JsonReader(string s) => _s = s ?? string.Empty;

            public object ReadValue()
            {
                Skip();
                if (_i >= _s.Length) throw Error("unexpected end");
                var c = _s[_i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                return ReadNumber();
            }

            private Dictionary<string, object> ReadObject()
            {
                var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                _i++;
                Skip();
                if (Peek('}')) { _i++; return obj; }
                while (true)
                {
                    Skip();
                    var key = ReadString();
                    Skip();
                    Expect(':');
                    obj[key] = ReadValue();
                    Skip();
                    if (Peek(',')) { _i++; continue; }
                    Expect('}');
                    return obj;
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                _i++;
                Skip();
                if (Peek(']')) { _i++; return list; }
                while (true)
                {
                    list.Add(ReadValue());
                    Skip();
                    if (Peek(',')) { _i++; continue; }
                    Expect(']');
                    return list;
                }
            }

            private string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) break;
                    var e = _s[_i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                throw Error("unterminated string");
            }

            private double ReadNumber()
            {
                var start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                if (_i == start || !double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw Error("unexpected character");
                return d;
            }

            private bool Word(string w)
            {
                if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) return false;
                _i += w.Length;
                return true;
            }

            private void Skip()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

            private void Expect(char c)
            {
                if (!Peek(c)) throw Error("expected '" + c + "'");
                _i++;
            }

            private FormatException Error(string what) => new FormatException($"Invalid JSON at position {_i}: {what}.");
        }
    }
}
