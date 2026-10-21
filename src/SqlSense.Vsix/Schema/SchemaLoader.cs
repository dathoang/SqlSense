using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using SqlSense.Core.Schema;

namespace SqlSense.Schema
{
    /// <summary>Reads one database's metadata in a single round trip.</summary>
    internal static class SchemaLoader
    {
        private const string Query = @"
SET NOCOUNT ON;
SELECT DB_NAME(), SCHEMA_NAME();

SELECT name FROM sys.schemas
WHERE schema_id < 16384 AND name NOT IN ('guest', 'INFORMATION_SCHEMA', 'sys')
   OR (@sys = 1 AND name IN ('sys', 'INFORMATION_SCHEMA'))
ORDER BY name;

SELECT name FROM sys.databases WHERE state = 0 ORDER BY name;

SELECT o.object_id, s.name, o.name, o.type, o.parent_object_id
FROM sys.all_objects AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE o.type IN ('U', 'V', 'P', 'PC', 'FN', 'FS', 'IF', 'TF', 'FT', 'SN', 'TR')
  AND (o.is_ms_shipped = 0 OR (@sys = 1 AND s.name IN ('sys', 'INFORMATION_SCHEMA') AND o.type IN ('V', 'IF', 'TF')))
UNION ALL
SELECT tt.type_table_object_id, s.name, tt.name, 'TT', 0
FROM sys.table_types AS tt
JOIN sys.schemas AS s ON s.schema_id = tt.schema_id;

SELECT c.object_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed, c.column_id
FROM sys.all_columns AS c
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
JOIN sys.all_objects AS o ON o.object_id = c.object_id
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE o.type IN ('U', 'V', 'IF', 'TF', 'FT')
  AND (o.is_ms_shipped = 0 OR (@sys = 1 AND s.name IN ('sys', 'INFORMATION_SCHEMA')))
UNION ALL
SELECT c.object_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed, c.column_id
FROM sys.columns AS c
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
JOIN sys.table_types AS tt ON tt.type_table_object_id = c.object_id
ORDER BY 1, 10;

SELECT p.object_id, p.name, t.name, p.max_length, p.precision, p.scale, p.is_output, p.has_default_value, p.parameter_id
FROM sys.parameters AS p
JOIN sys.types AS t ON t.user_type_id = p.user_type_id
JOIN sys.objects AS o ON o.object_id = p.object_id
WHERE p.parameter_id > 0 AND o.is_ms_shipped = 0
ORDER BY 1, 9;

SELECT fk.object_id, fk.name, ps.name, pt.name, rs.name, rt.name, pc.name, rc.name, fkc.constraint_column_id
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables AS pt ON pt.object_id = fk.parent_object_id
JOIN sys.schemas AS ps ON ps.schema_id = pt.schema_id
JOIN sys.tables AS rt ON rt.object_id = fk.referenced_object_id
JOIN sys.schemas AS rs ON rs.schema_id = rt.schema_id
JOIN sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns AS rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
ORDER BY 1, 9;";

        public static DatabaseSchema Load(string connectionString, bool includeSystemObjects)
        {
            using (var cn = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(Query, cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@sys", includeSystemObjects ? 1 : 0);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    r.Read();
                    var schema = new DatabaseSchema(r.GetString(0), r.IsDBNull(1) ? "dbo" : r.GetString(1));

                    r.NextResult();
                    while (r.Read()) schema.Schemas.Add(r.GetString(0));

                    r.NextResult();
                    while (r.Read())
                    {
                        var db = r.GetString(0);
                        if (!string.Equals(db, schema.DatabaseName, StringComparison.OrdinalIgnoreCase)) schema.OtherDatabases.Add(db);
                    }

                    var byId = new Dictionary<int, DbObject>();
                    var triggerParents = new List<(DbObject Trigger, int ParentId)>();
                    r.NextResult();
                    while (r.Read())
                    {
                        var kind = Kind(r.GetString(3).Trim());
                        if (kind == null) continue;
                        var obj = schema.Add(new DbObject(r.GetString(1), r.GetString(2), kind.Value));
                        byId[r.GetInt32(0)] = obj;
                        if (kind == DbObjectKind.Trigger) triggerParents.Add((obj, r.GetInt32(4)));
                    }
                    foreach (var (trigger, parentId) in triggerParents)
                        if (byId.TryGetValue(parentId, out var parent)) trigger.Parent = parent.QualifiedName;

                    r.NextResult();
                    while (r.Read())
                    {
                        if (!byId.TryGetValue(r.GetInt32(0), out var obj)) continue;
                        obj.Columns.Add(new DbColumn(r.GetString(1),
                            TypeText(r.GetString(2), r.GetInt16(3), r.GetByte(4), r.GetByte(5)),
                            r.GetBoolean(6), r.GetBoolean(7), r.GetBoolean(8), r.GetInt32(9)));
                    }

                    r.NextResult();
                    while (r.Read())
                    {
                        if (!byId.TryGetValue(r.GetInt32(0), out var obj)) continue;
                        obj.Parameters.Add(new DbParameter(r.GetString(1),
                            TypeText(r.GetString(2), r.GetInt16(3), r.GetByte(4), r.GetByte(5)),
                            r.GetBoolean(6), r.GetBoolean(7), r.GetInt32(8)));
                    }

                    r.NextResult();
                    var fks = new List<(int Id, string Name, string FS, string FT, string TS, string TT, string FC, string TC)>();
                    while (r.Read())
                        fks.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7)));
                    foreach (var g in fks.GroupBy(f => f.Id))
                    {
                        var first = g.First();
                        schema.ForeignKeys.Add(new DbForeignKey(first.Name, first.FS, first.FT, first.TS, first.TT,
                            g.Select(x => x.FC).ToList(), g.Select(x => x.TC).ToList()));
                    }
                    return schema;
                }
            }
        }

        /// <summary>
        /// The module's T-SQL source, or null when there is none to read (CLR, encrypted, dropped, or no
        /// VIEW DEFINITION permission). Call off the UI thread.
        /// </summary>
        public static string LoadDefinition(string connectionString, string schema, string name)
        {
            using (var cn = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand("SELECT OBJECT_DEFINITION(OBJECT_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@name)));", cn) { CommandTimeout = 15 })
            {
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@name", name);
                cn.Open();
                return cmd.ExecuteScalar() as string;
            }
        }

        private static DbObjectKind? Kind(string type)
        {
            switch (type)
            {
                case "U": return DbObjectKind.Table;
                case "V": return DbObjectKind.View;
                case "P":
                case "PC": return DbObjectKind.Procedure;
                case "FN":
                case "FS": return DbObjectKind.ScalarFunction;
                case "IF":
                case "TF":
                case "FT": return DbObjectKind.TableFunction;
                case "SN": return DbObjectKind.Synonym;
                case "TT": return DbObjectKind.TableType;
                case "TR": return DbObjectKind.Trigger;
                default: return null;
            }
        }

        public static string TypeText(string name, short maxLength, byte precision, byte scale)
        {
            switch (name.ToLowerInvariant())
            {
                case "varchar":
                case "char":
                case "varbinary":
                case "binary":
                    return $"{name}({(maxLength == -1 ? "max" : maxLength.ToString())})";
                case "nvarchar":
                case "nchar":
                    return $"{name}({(maxLength == -1 ? "max" : (maxLength / 2).ToString())})";
                case "decimal":
                case "numeric":
                    return $"{name}({precision}, {scale})";
                case "datetime2":
                case "time":
                case "datetimeoffset":
                    return scale == 7 ? name : $"{name}({scale})";
                default:
                    return name;
            }
        }
    }
}
