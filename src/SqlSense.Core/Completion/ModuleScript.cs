using System.Collections.Generic;
using SqlSense.Core.Lexing;

namespace SqlSense.Core.Completion
{
    /// <summary>Turns a stored module definition (OBJECT_DEFINITION) into an ALTER / CREATE OR ALTER script.</summary>
    public static class ModuleScript
    {
        /// <summary>
        /// Replaces the definition's leading CREATE (or CREATE OR ALTER) with <paramref name="verb"/>, keeping
        /// any header comments and the body byte for byte. Returns null when the text doesn't start with CREATE
        /// (no definition, or something unexpected).
        /// </summary>
        public static string Build(string definition, string verb)
        {
            if (string.IsNullOrWhiteSpace(definition) || string.IsNullOrEmpty(verb)) return null;
            var tokens = SqlLexer.Tokenize(definition);

            var create = Next(tokens, 0);
            if (create < 0 || !tokens[create].Is("CREATE")) return null;
            var end = tokens[create].End;
            var or = Next(tokens, create + 1);
            if (or >= 0 && tokens[or].Is("OR"))
            {
                var alter = Next(tokens, or + 1);
                if (alter >= 0 && tokens[alter].Is("ALTER")) end = tokens[alter].End;
            }

            // Follow the case the object was written in.
            var word = tokens[create].Text;
            var text = word == word.ToLowerInvariant() ? verb.ToLowerInvariant() : verb.ToUpperInvariant();
            var start = tokens[create].Start;
            return (definition.Substring(0, start) + text + definition.Substring(end)).TrimEnd();
        }

        private static int Next(List<SqlToken> tokens, int from)
        {
            for (var i = from; i < tokens.Count; i++)
                if (!tokens[i].IsTrivia) return i;
            return -1;
        }
    }
}
