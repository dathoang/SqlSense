using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SqlSense.Core.Connections
{
    /// <summary>One line of the rules file: <c>server[/database] = colour</c>.</summary>
    public sealed class ConnectionColorRule
    {
        internal ConnectionColorRule(int line, string server, string database, uint? argb)
        {
            Line = line;
            ServerPattern = server;
            DatabasePattern = database;
            Argb = argb;
            _server = Wildcard(server);
            _database = database == null ? null : Wildcard(database);
        }

        private readonly Regex _server;
        private readonly Regex _database;

        public int Line { get; }
        public string ServerPattern { get; }

        /// <summary>Null when the rule names only a server (it then applies to every database).</summary>
        public string DatabasePattern { get; }

        /// <summary>Null for <c>none</c>: the window gets no colour.</summary>
        public uint? Argb { get; }

        public bool Matches(string server, string database)
            => _server.IsMatch(server ?? "") && (_database == null || _database.IsMatch(database ?? ""));

        private static Regex Wildcard(string pattern)
            => new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Colours for query windows by server and database, read from a text file:
    /// <code>
    /// PROD-SQL01            = Red
    /// PROD-SQL01/Reporting  = #E67E22
    /// *.test.contoso.com/*  = Green
    /// </code>
    /// <c>*</c> and <c>?</c> are wildcards and matching ignores case. A rule that names a database beats
    /// a server-only rule; otherwise the first matching line wins.
    /// </summary>
    public sealed class ConnectionColorRules
    {
        public const string Template =
@"# SqlSense connection colours: query-window tabs and editor borders are painted by server and database.
#
#   server[/database] = colour
#
# * and ? are wildcards; matching ignores case. A rule that names a database wins over a server-only
# rule; otherwise the first matching line wins. Colours: #RRGGBB, #AARRGGBB, a name (Red, Orange,
# Green, ...) or none (no colour, e.g. to exclude a database from its server's colour).
# Windows that no rule matches use the colour picked in SSMS's Connect dialog (Options › Use custom color).
#
# Examples:
# PROD-SQL01            = Red
# PROD-SQL01/Reporting  = Orange
# PROD-SQL01/tempdb     = none
# *.test.contoso.com    = Green
# localhost\*/*_Staging = #8E44AD
";

        private ConnectionColorRules(List<ConnectionColorRule> rules, List<string> errors)
        {
            Rules = rules;
            Errors = errors;
        }

        public static ConnectionColorRules Empty { get; } = new ConnectionColorRules(new List<ConnectionColorRule>(), new List<string>());

        public IReadOnlyList<ConnectionColorRule> Rules { get; }

        /// <summary>"line N: …" for every line that couldn't be read; those lines are skipped.</summary>
        public IReadOnlyList<string> Errors { get; }

        public static ConnectionColorRules Parse(string text)
        {
            var rules = new List<ConnectionColorRule>();
            var errors = new List<string>();
            var lines = (text ?? "").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (IsComment(line)) continue;

                if (!TrySplit(line, out var server, out var database, out var colour))
                {
                    errors.Add($"line {i + 1}: expected 'server[/database] = colour'");
                    continue;
                }
                if (server.Length == 0 || database?.Length == 0)
                {
                    errors.Add($"line {i + 1}: missing server or database name");
                    continue;
                }

                uint? argb = null;
                if (!colour.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ColorText.TryParse(colour, out var value))
                    {
                        errors.Add($"line {i + 1}: unknown colour '{colour}'");
                        continue;
                    }
                    argb = value;
                }
                rules.Add(new ConnectionColorRule(i + 1, server, database, argb));
            }
            return new ConnectionColorRules(rules, errors);
        }

        private static bool IsComment(string line)
            => line.Length == 0 || line[0] == '#' || line.StartsWith("--", StringComparison.Ordinal);

        private static bool TrySplit(string line, out string server, out string database, out string colour)
        {
            server = database = colour = null;
            var eq = line.LastIndexOf('=');
            if (eq < 0) return false;
            var target = line.Substring(0, eq).Trim();
            colour = line.Substring(eq + 1).Trim();
            var slash = target.IndexOf('/');
            server = (slash < 0 ? target : target.Substring(0, slash)).Trim();
            database = slash < 0 ? null : target.Substring(slash + 1).Trim();
            return true;
        }

        private static bool SameTarget(string line, string server, string database)
            => !IsComment(line.Trim()) && TrySplit(line.Trim(), out var s, out var d, out _)
               && string.Equals(s, server, StringComparison.OrdinalIgnoreCase)
               && string.Equals(d, string.IsNullOrEmpty(database) ? null : database, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Sets the colour of exactly this server (database null) or server/database in the rules text:
        /// rewrites the line that names that target, or appends one. <paramref name="argb"/> null writes "none".
        /// </summary>
        public static string SetRule(string text, string server, string database, uint? argb)
        {
            var target = string.IsNullOrEmpty(database) ? server : server + "/" + database;
            var rule = target + " = " + (argb is uint value ? ColorText.ToText(value) : "none");
            var newLine = (text ?? "").Contains("\r\n") ? "\r\n" : "\n";
            var lines = new List<string>((text ?? "").Split('\n'));
            for (var i = 0; i < lines.Count; i++)
            {
                if (!SameTarget(lines[i], server, database)) continue;
                lines[i] = rule + (lines[i].EndsWith("\r", StringComparison.Ordinal) ? "\r" : "");
                return string.Join("\n", lines);
            }
            var result = text ?? "";
            if (result.Length > 0 && !result.EndsWith("\n", StringComparison.Ordinal)) result += newLine;
            return result + rule + newLine;
        }

        /// <summary>Removes the lines that name exactly this server (database null) or server/database.</summary>
        public static string RemoveRule(string text, string server, string database)
        {
            var lines = new List<string>((text ?? "").Split('\n'));
            lines.RemoveAll(line => SameTarget(line, server, database));
            return string.Join("\n", lines);
        }

        /// <summary>The rule written for exactly this server (database null) or server/database, ignoring wildcards.</summary>
        public ConnectionColorRule FindExact(string server, string database)
        {
            if (string.IsNullOrEmpty(database)) database = null;
            foreach (var rule in Rules)
                if (string.Equals(rule.ServerPattern, server, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(rule.DatabasePattern, database, StringComparison.OrdinalIgnoreCase))
                    return rule;
            return null;
        }

        /// <summary>The rule for this server and database, or null when none matches.</summary>
        public ConnectionColorRule Find(string server, string database)
        {
            ConnectionColorRule serverOnly = null;
            foreach (var rule in Rules)
            {
                if (!rule.Matches(server, database)) continue;
                if (rule.DatabasePattern != null) return rule;
                if (serverOnly == null) serverOnly = rule;
            }
            return serverOnly;
        }
    }

    /// <summary>Parses #RGB, #RRGGBB, #AARRGGBB and a few colour names into ARGB.</summary>
    public static class ColorText
    {
        /// <summary>The named colours, in the order the colour picker shows them.</summary>
        public static IReadOnlyList<KeyValuePair<string, uint>> Palette { get; } = new[]
        {
            new KeyValuePair<string, uint>("Red", 0xFFE81123),
            new KeyValuePair<string, uint>("DarkRed", 0xFF8B0000),
            new KeyValuePair<string, uint>("Orange", 0xFFF7630C),
            new KeyValuePair<string, uint>("Yellow", 0xFFFFB900),
            new KeyValuePair<string, uint>("Gold", 0xFFFFD700),
            new KeyValuePair<string, uint>("Green", 0xFF16C60C),
            new KeyValuePair<string, uint>("DarkGreen", 0xFF107C10),
            new KeyValuePair<string, uint>("Teal", 0xFF00B7C3),
            new KeyValuePair<string, uint>("Cyan", 0xFF00BCF2),
            new KeyValuePair<string, uint>("Blue", 0xFF0078D4),
            new KeyValuePair<string, uint>("Navy", 0xFF003A8C),
            new KeyValuePair<string, uint>("Purple", 0xFF8764B8),
            new KeyValuePair<string, uint>("Magenta", 0xFFE3008C),
            new KeyValuePair<string, uint>("Pink", 0xFFFF69B4),
            new KeyValuePair<string, uint>("Brown", 0xFF8E562E),
            new KeyValuePair<string, uint>("Gray", 0xFF7A7574),
            new KeyValuePair<string, uint>("Black", 0xFF000000),
            new KeyValuePair<string, uint>("White", 0xFFFFFFFF),
        };

        private static readonly Dictionary<string, uint> Names = BuildNames();

        private static Dictionary<string, uint> BuildNames()
        {
            var names = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { ["Grey"] = 0xFF7A7574 };
            foreach (var pair in Palette) names[pair.Key] = pair.Value;
            return names;
        }

        /// <summary>A colour's name when it is one of <see cref="Palette"/>, else #RRGGBB (or #AARRGGBB if not opaque).</summary>
        public static string ToText(uint argb)
        {
            foreach (var pair in Palette)
                if (pair.Value == argb) return pair.Key;
            return (argb >> 24) == 0xFF
                ? "#" + (argb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)
                : "#" + argb.ToString("X8", CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string text, out uint argb)
        {
            argb = 0;
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return false;
            if (Names.TryGetValue(text, out argb)) return true;
            if (text[0] != '#') return false;

            var hex = text.Substring(1);
            if (hex.Length == 3) hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            if (hex.Length != 6 && hex.Length != 8) return false;
            if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out argb)) return false;
            if (hex.Length == 6) argb |= 0xFF000000;
            return true;
        }
    }
}
