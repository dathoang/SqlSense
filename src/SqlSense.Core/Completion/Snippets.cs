using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SqlSense.Core.Completion
{
    public sealed class Snippet
    {
        public Snippet(string shortcut, string description, string body)
        {
            Shortcut = shortcut;
            Description = description;
            Body = body;
        }

        public string Shortcut { get; }
        public string Description { get; }

        /// <summary>Body text. $CURSOR$ marks the caret; $SELECTEDTEXT$ is replaced by the selection.</summary>
        public string Body { get; }
    }

    public sealed class SnippetExpansion
    {
        public SnippetExpansion(string text, int caretOffset)
        {
            Text = text;
            CaretOffset = caretOffset;
        }

        public string Text { get; }
        public int CaretOffset { get; }
    }

    /// <summary>Shortcut → template snippets (ssf → SELECT * FROM …). User snippets override built-ins.</summary>
    public sealed class SnippetLibrary
    {
        public const string CursorToken = "$CURSOR$";
        public const string SelectedTextToken = "$SELECTEDTEXT$";

        private readonly Dictionary<string, Snippet> _snippets = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase);

        public SnippetLibrary(bool includeDefaults = true)
        {
            if (!includeDefaults) return;
            foreach (var s in Defaults()) _snippets[s.Shortcut] = s;
        }

        public IEnumerable<Snippet> All => _snippets.Values.OrderBy(s => s.Shortcut, StringComparer.OrdinalIgnoreCase);

        public void Add(Snippet snippet) => _snippets[snippet.Shortcut] = snippet;

        public Snippet Find(string shortcut) =>
            shortcut != null && _snippets.TryGetValue(shortcut, out var s) ? s : null;

        /// <summary>
        /// Loads *.sql files from a folder: the file name is the shortcut, an optional first line
        /// "-- description" is the description, and the rest is the body.
        /// </summary>
        public int LoadFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
            var count = 0;
            foreach (var file in Directory.GetFiles(folder, "*.sql"))
            {
                var text = File.ReadAllText(file).Replace("\r\n", "\n");
                var description = string.Empty;
                if (text.StartsWith("--", StringComparison.Ordinal))
                {
                    var nl = text.IndexOf('\n');
                    description = (nl < 0 ? text : text.Substring(0, nl)).Substring(2).Trim();
                    text = nl < 0 ? string.Empty : text.Substring(nl + 1);
                }
                Add(new Snippet(Path.GetFileNameWithoutExtension(file), description, text.TrimEnd('\n')));
                count++;
            }
            return count;
        }

        /// <summary>Expands a snippet for insertion at a line with the given indentation.</summary>
        public static SnippetExpansion Expand(Snippet snippet, string lineIndent, string newLine, string selectedText = "")
        {
            var body = snippet.Body.Replace("\r\n", "\n").Replace(SelectedTextToken, selectedText ?? string.Empty);
            var lines = body.Split('\n');
            var sb = new StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append(newLine).Append(lineIndent);
                sb.Append(lines[i]);
            }
            var text = sb.ToString();
            var caret = text.IndexOf(CursorToken, StringComparison.Ordinal);
            if (caret >= 0) text = text.Remove(caret, CursorToken.Length);
            else caret = text.Length;
            return new SnippetExpansion(text, caret);
        }

        private static IEnumerable<Snippet> Defaults()
        {
            yield return new Snippet("ssf", "SELECT * FROM", "SELECT * FROM $CURSOR$");
            yield return new Snippet("st100", "SELECT TOP 100 * FROM", "SELECT TOP 100 * FROM $CURSOR$");
            yield return new Snippet("scf", "SELECT COUNT(*) FROM", "SELECT COUNT(*) FROM $CURSOR$");
            yield return new Snippet("sdf", "SELECT DISTINCT … FROM", "SELECT DISTINCT $CURSOR$\nFROM ");
            yield return new Snippet("ii", "INSERT INTO", "INSERT INTO $CURSOR$");
            yield return new Snippet("iis", "INSERT INTO … SELECT", "INSERT INTO $CURSOR$\nSELECT ");
            yield return new Snippet("uu", "UPDATE … SET", "UPDATE $CURSOR$\nSET ");
            yield return new Snippet("df", "DELETE FROM", "DELETE FROM $CURSOR$");
            yield return new Snippet("ij", "INNER JOIN", "INNER JOIN $CURSOR$ ON ");
            yield return new Snippet("lj", "LEFT JOIN", "LEFT JOIN $CURSOR$ ON ");
            yield return new Snippet("w2", "WHERE … = …", "WHERE $CURSOR$ = ");
            yield return new Snippet("ob", "ORDER BY", "ORDER BY $CURSOR$");
            yield return new Snippet("gb", "GROUP BY", "GROUP BY $CURSOR$");
            yield return new Snippet("be", "BEGIN … END", "BEGIN\n    $CURSOR$\nEND");
            yield return new Snippet("bt", "Transaction with TRY/CATCH",
                "BEGIN TRY\n    BEGIN TRANSACTION;\n\n    $CURSOR$\n\n    COMMIT TRANSACTION;\nEND TRY\nBEGIN CATCH\n    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;\n    THROW;\nEND CATCH;");
            yield return new Snippet("tc", "TRY/CATCH", "BEGIN TRY\n    $CURSOR$\nEND TRY\nBEGIN CATCH\n    THROW;\nEND CATCH;");
            yield return new Snippet("ifex", "IF EXISTS (SELECT …)", "IF EXISTS (SELECT 1 FROM $CURSOR$)\nBEGIN\n\nEND");
            yield return new Snippet("cte", "Common table expression", "WITH cte AS\n(\n    $CURSOR$\n)\nSELECT * FROM cte;");
            yield return new Snippet("cur", "Cursor loop",
                "DECLARE @id int;\nDECLARE cur CURSOR LOCAL FAST_FORWARD FOR\n    SELECT $CURSOR$;\nOPEN cur;\nFETCH NEXT FROM cur INTO @id;\nWHILE @@FETCH_STATUS = 0\nBEGIN\n\n    FETCH NEXT FROM cur INTO @id;\nEND\nCLOSE cur;\nDEALLOCATE cur;");
            yield return new Snippet("cp", "CREATE PROCEDURE",
                "CREATE OR ALTER PROCEDURE dbo.$CURSOR$\nAS\nBEGIN\n    SET NOCOUNT ON;\n\nEND\nGO");
            yield return new Snippet("cv", "CREATE VIEW", "CREATE OR ALTER VIEW dbo.$CURSOR$\nAS\nSELECT\nGO");
            yield return new Snippet("ct", "CREATE TABLE",
                "CREATE TABLE dbo.$CURSOR$\n(\n    Id int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ PRIMARY KEY,\n)");
            yield return new Snippet("tt", "Temp table", "CREATE TABLE #$CURSOR$\n(\n    Id int NOT NULL\n);");
            yield return new Snippet("dt", "DROP TABLE IF EXISTS", "DROP TABLE IF EXISTS $CURSOR$;");
            yield return new Snippet("sn", "SET NOCOUNT ON", "SET NOCOUNT ON;$CURSOR$");
            yield return new Snippet("rn", "ROW_NUMBER() OVER", "ROW_NUMBER() OVER (PARTITION BY $CURSOR$ ORDER BY )");
            yield return new Snippet("sp_who", "Active sessions",
                "SELECT s.session_id, s.login_name, s.host_name, r.status, r.command, r.wait_type, r.blocking_session_id, t.text\nFROM sys.dm_exec_sessions AS s\n    LEFT JOIN sys.dm_exec_requests AS r ON r.session_id = s.session_id\n    OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) AS t\nWHERE s.is_user_process = 1;$CURSOR$");
        }
    }
}
