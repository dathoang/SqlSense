using System;
using System.Data.SqlTypes;
using System.Reflection;

namespace SqlSense.Core.Export
{
    /// <summary>Normalizes grid cell values: DBNull and null SqlTypes become null, SqlTypes become their CLR value.</summary>
    public static class CellValue
    {
        public static object Normalize(object value)
        {
            if (value == null || value is DBNull) return null;
            if (value is INullable nullable)
            {
                if (nullable.IsNull) return null;
                switch (value)
                {
                    case SqlBinary b: return b.Value;
                    case SqlBytes b: return b.Value;
                    case SqlChars c: return new string(c.Value);
                }
                // SqlString, SqlInt32, SqlDecimal, … all expose Value; UDTs (geography, hierarchyid) don't.
                var prop = value.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        return prop.GetValue(value);
                    }
                    catch (TargetInvocationException)
                    {
                        // SqlDecimal.Value throws past decimal's range; fall through to the text form.
                    }
                }
                return value.ToString();
            }
            return value;
        }

        public static bool IsBinaryType(string sqlType) =>
            Is(sqlType, "binary", "varbinary", "image", "timestamp", "rowversion");

        public static bool IsNumericType(string sqlType) =>
            Is(sqlType, "bit", "tinyint", "smallint", "int", "bigint", "decimal", "numeric", "money", "smallmoney", "float", "real");

        public static bool IsUnicodeTextType(string sqlType) => Is(sqlType, "nchar", "nvarchar", "ntext", "xml", "sysname");

        public static bool IsAnsiTextType(string sqlType) => Is(sqlType, "char", "varchar", "text");

        public static bool Is(string sqlType, params string[] names)
        {
            if (string.IsNullOrEmpty(sqlType)) return false;
            var t = BaseType(sqlType);
            foreach (var n in names)
                if (string.Equals(t, n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>"nvarchar(50)" → "nvarchar"; "sys.geography" → "geography".</summary>
        public static string BaseType(string sqlType)
        {
            if (string.IsNullOrEmpty(sqlType)) return sqlType;
            var t = sqlType.Trim();
            var paren = t.IndexOf('(');
            if (paren >= 0) t = t.Substring(0, paren).TrimEnd();
            var dot = t.LastIndexOf('.');
            if (dot >= 0) t = t.Substring(dot + 1);
            return t.Trim('[', ']');
        }
    }
}
