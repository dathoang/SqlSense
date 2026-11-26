using System;
using System.Collections.Generic;

namespace SqlSense.Core.Completion
{
    /// <summary>Coarse data type families, for ranking values that can be compared with each other.</summary>
    public static class SqlTypes
    {
        private static readonly Dictionary<string, string> Families = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bit"] = "number", ["tinyint"] = "number", ["smallint"] = "number", ["int"] = "number", ["bigint"] = "number",
            ["decimal"] = "number", ["numeric"] = "number", ["money"] = "number", ["smallmoney"] = "number",
            ["float"] = "number", ["real"] = "number",
            ["char"] = "text", ["varchar"] = "text", ["nchar"] = "text", ["nvarchar"] = "text", ["text"] = "text",
            ["ntext"] = "text", ["sysname"] = "text",
            ["date"] = "date", ["datetime"] = "date", ["datetime2"] = "date", ["smalldatetime"] = "date",
            ["datetimeoffset"] = "date", ["time"] = "time",
            ["uniqueidentifier"] = "guid",
            ["binary"] = "binary", ["varbinary"] = "binary", ["image"] = "binary", ["timestamp"] = "binary", ["rowversion"] = "binary",
        };

        /// <summary>"nvarchar(100)" → "text"; unknown types are their own family.</summary>
        public static string Family(string dataType)
        {
            if (string.IsNullOrWhiteSpace(dataType)) return null;
            var t = dataType.Trim();
            var paren = t.IndexOf('(');
            if (paren >= 0) t = t.Substring(0, paren).Trim();
            return Families.TryGetValue(t, out var family) ? family : t.ToLowerInvariant();
        }

        /// <summary>Both types known and of the same family.</summary>
        public static bool Compatible(string a, string b)
        {
            var fa = Family(a);
            return fa != null && fa == Family(b);
        }
    }
}
