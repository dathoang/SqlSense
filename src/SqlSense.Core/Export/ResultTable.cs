using System.Collections.Generic;

namespace SqlSense.Core.Export
{
    /// <summary>A column of a query result: its header text and server type name (e.g. "nvarchar"; may be null).</summary>
    public sealed class ResultColumn
    {
        public ResultColumn(string name, string sqlType = null)
        {
            Name = name;
            SqlType = sqlType;
        }

        public string Name { get; }
        public string SqlType { get; }

        public override string ToString() => SqlType == null ? Name : Name + " " + SqlType;
    }

    /// <summary>
    /// A snapshot of (part of) a result grid. Cell values are whatever the grid storage returned:
    /// CLR values, SqlTypes, DBNull or plain strings.
    /// </summary>
    public sealed class ResultTable
    {
        public ResultTable(IReadOnlyList<ResultColumn> columns, IReadOnlyList<object[]> rows)
        {
            Columns = columns;
            Rows = rows;
        }

        public IReadOnlyList<ResultColumn> Columns { get; }
        public IReadOnlyList<object[]> Rows { get; }
    }
}
