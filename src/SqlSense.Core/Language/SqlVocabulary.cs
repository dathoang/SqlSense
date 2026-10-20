using System;
using System.Collections.Generic;

namespace SqlSense.Core.Language
{
    public sealed class BuiltInFunction
    {
        public BuiltInFunction(string name, string signature, string description, bool needsParens = true)
        {
            Name = name;
            Signature = signature;
            Description = description;
            NeedsParens = needsParens;
        }

        public string Name { get; }
        public string Signature { get; }
        public string Description { get; }
        public bool NeedsParens { get; }
    }

    /// <summary>Static T-SQL vocabulary: keywords, data types, built-in functions.</summary>
    public static class SqlVocabulary
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        /// <summary>Keywords offered by completion (reserved and common non-reserved).</summary>
        public static readonly string[] Keywords =
        {
            "ADD", "ALL", "ALTER", "AND", "ANY", "APPLY", "AS", "ASC", "AUTHORIZATION", "BACKUP", "BEGIN",
            "BETWEEN", "BREAK", "BY", "CASCADE", "CASE", "CATCH", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED",
            "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT", "CONTINUE", "CREATE", "CROSS", "CURRENT",
            "CURSOR", "DATABASE", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DENY", "DESC", "DISTINCT",
            "DROP", "ELSE", "END", "EXCEPT", "EXEC", "EXECUTE", "EXISTS", "FETCH", "FOR", "FOREIGN", "FROM",
            "FULL", "FUNCTION", "GO", "GOTO", "GRANT", "GROUP", "HAVING", "IDENTITY", "IF", "IN", "INDEX",
            "INNER", "INSERT", "INTERSECT", "INTO", "IS", "JOIN", "KEY", "LEFT", "LIKE", "MERGE", "NOCOUNT",
            "NOLOCK", "NONCLUSTERED", "NOT", "NULL", "OF", "OFF", "OFFSET", "ON", "OPEN", "OPTION", "OR",
            "ORDER", "OUTER", "OUTPUT", "OVER", "PARTITION", "PERCENT", "PIVOT", "PRIMARY", "PRINT", "PROC",
            "PROCEDURE", "RAISERROR", "READONLY", "REFERENCES", "RETURN", "RETURNS", "REVOKE", "RIGHT",
            "ROLLBACK", "ROWS", "SCHEMA", "SELECT", "SET", "TABLE", "THEN", "THROW", "TIES", "TO", "TOP",
            "TRAN", "TRANSACTION", "TRIGGER", "TRUNCATE", "TRY", "UNION", "UNIQUE", "UNPIVOT", "UPDATE",
            "USE", "USING", "VALUES", "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE", "WITH", "XACT_ABORT",
        };

        public static readonly HashSet<string> DataTypes = new HashSet<string>(Ci)
        {
            "bigint", "binary", "bit", "char", "cursor", "date", "datetime", "datetime2", "datetimeoffset",
            "decimal", "float", "geography", "geometry", "hierarchyid", "image", "int", "money", "nchar",
            "ntext", "numeric", "nvarchar", "real", "rowversion", "smalldatetime", "smallint", "smallmoney",
            "sql_variant", "sysname", "table", "text", "time", "timestamp", "tinyint", "uniqueidentifier",
            "varbinary", "varchar", "xml", "json", "vector",
        };

        public static readonly BuiltInFunction[] Functions =
        {
            new BuiltInFunction("ABS", "ABS(numeric_expression)", "Absolute value"),
            new BuiltInFunction("AVG", "AVG([ALL | DISTINCT] expression)", "Average of values"),
            new BuiltInFunction("CAST", "CAST(expression AS data_type)", "Convert to a data type"),
            new BuiltInFunction("CEILING", "CEILING(numeric_expression)", "Smallest integer >= value"),
            new BuiltInFunction("CHARINDEX", "CHARINDEX(find, search [, start])", "Position of a substring"),
            new BuiltInFunction("COALESCE", "COALESCE(expression [, ...n])", "First non-NULL argument"),
            new BuiltInFunction("CONCAT", "CONCAT(value1, value2 [, ...n])", "Concatenate strings"),
            new BuiltInFunction("CONCAT_WS", "CONCAT_WS(separator, value1, value2 [, ...n])", "Concatenate with separator"),
            new BuiltInFunction("CONVERT", "CONVERT(data_type, expression [, style])", "Convert with style"),
            new BuiltInFunction("COUNT", "COUNT({* | [ALL | DISTINCT] expression})", "Number of rows"),
            new BuiltInFunction("COUNT_BIG", "COUNT_BIG({* | [ALL | DISTINCT] expression})", "Number of rows (bigint)"),
            new BuiltInFunction("CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP", "Current date and time", false),
            new BuiltInFunction("DATEADD", "DATEADD(datepart, number, date)", "Add an interval to a date"),
            new BuiltInFunction("DATEDIFF", "DATEDIFF(datepart, startdate, enddate)", "Difference between dates"),
            new BuiltInFunction("DATEFROMPARTS", "DATEFROMPARTS(year, month, day)", "Build a date"),
            new BuiltInFunction("DATENAME", "DATENAME(datepart, date)", "Name of a date part"),
            new BuiltInFunction("DATEPART", "DATEPART(datepart, date)", "Integer date part"),
            new BuiltInFunction("DATETRUNC", "DATETRUNC(datepart, date)", "Truncate a date"),
            new BuiltInFunction("DAY", "DAY(date)", "Day of month"),
            new BuiltInFunction("DB_NAME", "DB_NAME([database_id])", "Database name"),
            new BuiltInFunction("DENSE_RANK", "DENSE_RANK() OVER (...)", "Rank without gaps"),
            new BuiltInFunction("EOMONTH", "EOMONTH(start_date [, month_to_add])", "Last day of month"),
            new BuiltInFunction("ERROR_MESSAGE", "ERROR_MESSAGE()", "Message of the caught error"),
            new BuiltInFunction("ERROR_NUMBER", "ERROR_NUMBER()", "Number of the caught error"),
            new BuiltInFunction("FIRST_VALUE", "FIRST_VALUE(expression) OVER (...)", "First value in window"),
            new BuiltInFunction("FLOOR", "FLOOR(numeric_expression)", "Largest integer <= value"),
            new BuiltInFunction("FORMAT", "FORMAT(value, format [, culture])", "Format a value"),
            new BuiltInFunction("GETDATE", "GETDATE()", "Current date and time"),
            new BuiltInFunction("GETUTCDATE", "GETUTCDATE()", "Current UTC date and time"),
            new BuiltInFunction("GREATEST", "GREATEST(expression1 [, ...n])", "Maximum of arguments"),
            new BuiltInFunction("IIF", "IIF(condition, true_value, false_value)", "Inline IF"),
            new BuiltInFunction("ISNULL", "ISNULL(check_expression, replacement)", "Replace NULL"),
            new BuiltInFunction("ISNUMERIC", "ISNUMERIC(expression)", "1 if numeric"),
            new BuiltInFunction("JSON_QUERY", "JSON_QUERY(expression [, path])", "Extract JSON object/array"),
            new BuiltInFunction("JSON_VALUE", "JSON_VALUE(expression, path)", "Extract JSON scalar"),
            new BuiltInFunction("LAG", "LAG(expression [, offset [, default]]) OVER (...)", "Previous row value"),
            new BuiltInFunction("LEAD", "LEAD(expression [, offset [, default]]) OVER (...)", "Next row value"),
            new BuiltInFunction("LEAST", "LEAST(expression1 [, ...n])", "Minimum of arguments"),
            new BuiltInFunction("LEFT", "LEFT(string, count)", "Leftmost characters"),
            new BuiltInFunction("LEN", "LEN(string)", "String length (no trailing spaces)"),
            new BuiltInFunction("LOWER", "LOWER(string)", "Lower case"),
            new BuiltInFunction("LTRIM", "LTRIM(string)", "Trim leading spaces"),
            new BuiltInFunction("MAX", "MAX(expression)", "Maximum value"),
            new BuiltInFunction("MIN", "MIN(expression)", "Minimum value"),
            new BuiltInFunction("MONTH", "MONTH(date)", "Month number"),
            new BuiltInFunction("NEWID", "NEWID()", "New uniqueidentifier"),
            new BuiltInFunction("NULLIF", "NULLIF(expression1, expression2)", "NULL if equal"),
            new BuiltInFunction("OBJECT_ID", "OBJECT_ID('name' [, 'type'])", "Object id"),
            new BuiltInFunction("OBJECT_NAME", "OBJECT_NAME(object_id [, database_id])", "Object name"),
            new BuiltInFunction("OPENJSON", "OPENJSON(json [, path]) [WITH (...)]", "Rowset from JSON"),
            new BuiltInFunction("PATINDEX", "PATINDEX('%pattern%', expression)", "Position of a pattern"),
            new BuiltInFunction("RANK", "RANK() OVER (...)", "Rank with gaps"),
            new BuiltInFunction("REPLACE", "REPLACE(string, find, replacement)", "Replace substring"),
            new BuiltInFunction("REPLICATE", "REPLICATE(string, count)", "Repeat a string"),
            new BuiltInFunction("REVERSE", "REVERSE(string)", "Reverse a string"),
            new BuiltInFunction("RIGHT", "RIGHT(string, count)", "Rightmost characters"),
            new BuiltInFunction("ROUND", "ROUND(numeric, length [, function])", "Round a number"),
            new BuiltInFunction("ROW_NUMBER", "ROW_NUMBER() OVER ([PARTITION BY ...] ORDER BY ...)", "Sequential row number"),
            new BuiltInFunction("RTRIM", "RTRIM(string)", "Trim trailing spaces"),
            new BuiltInFunction("SCOPE_IDENTITY", "SCOPE_IDENTITY()", "Last identity in scope"),
            new BuiltInFunction("STRING_AGG", "STRING_AGG(expression, separator) [WITHIN GROUP (ORDER BY ...)]", "Aggregate strings"),
            new BuiltInFunction("STRING_SPLIT", "STRING_SPLIT(string, separator [, enable_ordinal])", "Split a string"),
            new BuiltInFunction("STUFF", "STUFF(string, start, length, replace)", "Delete and insert"),
            new BuiltInFunction("SUBSTRING", "SUBSTRING(expression, start, length)", "Part of a string"),
            new BuiltInFunction("SUM", "SUM([ALL | DISTINCT] expression)", "Sum of values"),
            new BuiltInFunction("SYSDATETIME", "SYSDATETIME()", "Current datetime2"),
            new BuiltInFunction("TRIM", "TRIM([characters FROM] string)", "Trim both ends"),
            new BuiltInFunction("TRY_CAST", "TRY_CAST(expression AS data_type)", "Cast or NULL"),
            new BuiltInFunction("TRY_CONVERT", "TRY_CONVERT(data_type, expression [, style])", "Convert or NULL"),
            new BuiltInFunction("UPPER", "UPPER(string)", "Upper case"),
            new BuiltInFunction("YEAR", "YEAR(date)", "Year number"),
            new BuiltInFunction("@@ROWCOUNT", "@@ROWCOUNT", "Rows affected by last statement", false),
            new BuiltInFunction("@@ERROR", "@@ERROR", "Error number of last statement", false),
            new BuiltInFunction("@@IDENTITY", "@@IDENTITY", "Last inserted identity", false),
            new BuiltInFunction("@@TRANCOUNT", "@@TRANCOUNT", "Open transaction count", false),
            new BuiltInFunction("@@SPID", "@@SPID", "Session id", false),
            new BuiltInFunction("@@VERSION", "@@VERSION", "Server version", false),
        };

        public static readonly HashSet<string> FunctionNames = BuildFunctionNames();

        /// <summary>Clause keywords that begin a new line when formatting.</summary>
        public static readonly HashSet<string> ClauseStarters = new HashSet<string>(Ci)
        {
            "SELECT", "FROM", "WHERE", "GROUP", "HAVING", "ORDER", "UNION", "EXCEPT", "INTERSECT",
            "INSERT", "UPDATE", "DELETE", "SET", "VALUES", "OUTPUT", "OPTION", "MERGE", "USING", "WHEN",
            "INTO", "OFFSET", "FETCH", "WINDOW",
        };

        /// <summary>Statement-starting keywords used to find the current statement.</summary>
        public static readonly HashSet<string> StatementStarters = new HashSet<string>(Ci)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "IF", "WHILE", "BEGIN", "END",
            "EXEC", "EXECUTE", "CREATE", "ALTER", "DROP", "TRUNCATE", "USE", "PRINT", "RETURN", "WITH",
            "GRANT", "REVOKE", "DENY", "THROW", "RAISERROR", "COMMIT", "ROLLBACK", "OPEN", "CLOSE", "FETCH",
            "DEALLOCATE", "GOTO", "BREAK", "CONTINUE", "WAITFOR",
        };

        public static bool IsDataType(string word) => DataTypes.Contains(word);

        private static HashSet<string> BuildFunctionNames()
        {
            var set = new HashSet<string>(Ci);
            foreach (var f in Functions) set.Add(f.Name);
            return set;
        }
    }
}
