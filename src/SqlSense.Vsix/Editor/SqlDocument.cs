using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using SqlSense.Diagnostics;

namespace SqlSense.Editor
{
    /// <summary>Decides whether a buffer holds T-SQL and extracts per-document facts.</summary>
    internal static class SqlDocument
    {
        private static readonly ConcurrentDictionary<string, bool> SqlContentTypes = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex UseStatement = new Regex(@"\bUSE\s+(\[(?<db>[^\]]+)\]|(?<db>[A-Za-z_][\w@#$]*))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// SSMS query windows use a legacy language service whose content type name contains "SQL";
        /// .sql files opened from disk are accepted too. Decided lazily because SSMS may assign the
        /// language service after the view is created.
        /// </summary>
        public static bool IsSql(ITextBuffer buffer)
        {
            if (buffer == null) return false;
            var ct = buffer.ContentType;
            var isSql = SqlContentTypes.GetOrAdd(ct.TypeName, _ =>
            {
                var result = LooksLikeSql(ct);
                Log.Info($"Content type '{ct.TypeName}' → {(result ? "SQL" : "not SQL")}.");
                return result;
            });
            if (isSql) return true;
            return buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc)
                   && doc?.FilePath != null && Path.GetExtension(doc.FilePath).Equals(".sql", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeSql(IContentType ct)
        {
            if (ct.TypeName.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var b in ct.BaseTypes)
                if (LooksLikeSql(b)) return true;
            return false;
        }

        /// <summary>Database named by the last USE statement before the caret, if any.</summary>
        public static string DatabaseFromUse(string text, int caret)
        {
            string db = null;
            foreach (Match m in UseStatement.Matches(text.Substring(0, Math.Min(caret, text.Length))))
                db = m.Groups["db"].Value;
            return db;
        }

        public static string LineBreak(ITextSnapshot snapshot)
        {
            if (snapshot.LineCount > 1)
            {
                var lb = snapshot.GetLineFromLineNumber(0).GetLineBreakText();
                if (!string.IsNullOrEmpty(lb)) return lb;
            }
            return "\r\n";
        }
    }
}
