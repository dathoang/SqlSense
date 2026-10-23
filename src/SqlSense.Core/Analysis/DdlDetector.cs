using System;
using System.Collections.Generic;
using System.Linq;
using SqlSense.Core.Lexing;

namespace SqlSense.Core.Analysis
{
    /// <summary>
    /// Finds the databases whose metadata a script changes when executed: CREATE/ALTER/DROP of objects, schemas,
    /// users, roles, indexes …, SELECT … INTO a permanent table, and sp_rename. Follows USE and three-part names;
    /// temp tables (#t) and table variables don't count.
    /// </summary>
    public static class DdlDetector
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        /// <summary>Databases changed by <paramref name="script"/>, in order; empty when it has no DDL.</summary>
        public static List<string> ChangedDatabases(string script, string currentDatabase)
        {
            var result = new List<string>();
            var sig = ScriptText.Significant(SqlLexer.Tokenize(script ?? string.Empty));
            var database = currentDatabase;
            foreach (var (start, end) in StatementAnalysis.SplitStatements(sig))
            {
                if (start > end) continue;
                var first = sig[start];
                if (first.Is("USE") && start + 1 <= end && sig[start + 1].IsName)
                {
                    database = sig[start + 1].Unquoted;
                    continue;
                }
                string target;
                if (first.Is("CREATE") || first.Is("ALTER") || first.Is("DROP")) target = DdlTarget(sig, start, end, database);
                else if (first.Is("SELECT")) target = SelectIntoTarget(sig, start, end, database);
                else if ((first.Is("EXEC") || first.Is("EXECUTE")) && start + 1 <= end && sig[start + 1].IsName
                         && Ci.Equals(sig[start + 1].Unquoted, "sp_rename"))
                    target = database;
                else continue;
                if (!string.IsNullOrEmpty(target) && !result.Contains(target, Ci)) result.Add(target);
            }
            return result;
        }

        private static string DdlTarget(List<SqlToken> sig, int start, int end, string database)
        {
            var i = start + 1;
            if (sig[start].Is("CREATE") && i + 1 <= end && sig[i].Is("OR") && sig[i + 1].Is("ALTER")) i += 2;
            // CREATE UNIQUE CLUSTERED INDEX …: skip the modifiers before the object word.
            while (i <= end && (sig[i].Is("UNIQUE") || sig[i].Is("CLUSTERED") || sig[i].Is("NONCLUSTERED") || sig[i].Is("COLUMNSTORE")
                                || sig[i].Is("PRIMARY") || sig[i].Is("XML") || sig[i].Is("SPATIAL")))
                i++;
            if (i > end) return null;
            var kind = sig[i];
            // Statements that read like DDL but don't change metadata SqlSense keeps.
            if (kind.Is("EVENT") || kind.Is("CREDENTIAL") || kind.Is("SERVER") && sig[start].Is("ALTER") && i + 1 <= end && sig[i + 1].Is("CONFIGURATION"))
                return null;
            // ALTER DATABASE / CREATE DATABASE change the server's database list, kept with the current database.
            if (kind.Is("DATABASE")) return database;

            var name = i + 1;
            if (name <= end && sig[name].Is("IF") && name + 2 <= end && sig[name + 1].Is("EXISTS")) name += 2;
            if (name > end) return database;
            if (sig[name].Kind == TokenKind.Variable || sig[name].Text.StartsWith("#", StringComparison.Ordinal)) return null;
            // CREATE INDEX ix ON db.schema.t, DROP INDEX ix ON t: the table decides the database.
            if (kind.Is("INDEX") || kind.Is("STATISTICS") || kind.Is("TRIGGER"))
            {
                var on = StatementAnalysis.FindWordAtDepth(sig, name, end, "ON");
                if (on >= 0 && on + 1 <= end) name = on + 1;
                if (name <= end && sig[name].Text.StartsWith("#", StringComparison.Ordinal)) return null;
            }
            return DatabaseOf(sig, name, end, database);
        }

        private static string SelectIntoTarget(List<SqlToken> sig, int start, int end, string database)
        {
            var into = StatementAnalysis.FindWordAtDepth(sig, start, end, "INTO");
            if (into < 0 || into + 1 > end || !sig[into + 1].IsName || sig[into + 1].Text.StartsWith("#", StringComparison.Ordinal)) return null;
            return DatabaseOf(sig, into + 1, end, database);
        }

        /// <summary>The database part of the (dotted) name starting at sig[j], else <paramref name="database"/>.</summary>
        private static string DatabaseOf(List<SqlToken> sig, int j, int end, string database)
        {
            var parts = new List<string>();
            var k = j;
            while (k <= end && sig[k].IsName)
            {
                parts.Add(sig[k].Unquoted);
                if (k + 1 > end || sig[k + 1].Text != ".") break;
                k += 2;
                // db..name: an empty schema part.
                while (k <= end && sig[k].Text == ".") { parts.Add(string.Empty); k++; }
            }
            return parts.Count >= 3 && parts[parts.Count - 3].Length > 0 ? parts[parts.Count - 3] : database;
        }
    }
}
