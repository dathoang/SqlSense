using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using SqlSense.Core.Schema;
using SqlSense.Diagnostics;

namespace SqlSense.Schema
{
    /// <summary>Reads one database's metadata: objects in one round trip, then database and server principals.</summary>
    internal static class SchemaLoader
    {
        private const string Query = @"
SET NOCOUNT ON;
SELECT DB_NAME(), SCHEMA_NAME();

SELECT name FROM sys.schemas
WHERE schema_id < 16384 AND name NOT IN ('guest', 'INFORMATION_SCHEMA', 'sys')
   OR (@sys = 1 AND name IN ('sys', 'INFORMATION_SCHEMA'))
ORDER BY name;

SELECT name, state, state_desc, recovery_model_desc, compatibility_level FROM sys.databases ORDER BY name;

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
ORDER BY 1, 9;

SELECT i.object_id, i.index_id, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_disabled
FROM sys.indexes AS i
JOIN sys.objects AS o ON o.object_id = i.object_id
WHERE i.name IS NOT NULL AND o.is_ms_shipped = 0 AND o.type IN ('U', 'V')
ORDER BY 1, 2;

SELECT ic.object_id, ic.index_id, c.name, ic.is_included_column
FROM sys.index_columns AS ic
JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
JOIN sys.objects AS o ON o.object_id = ic.object_id
WHERE o.is_ms_shipped = 0 AND o.type IN ('U', 'V')
ORDER BY 1, 2, ic.is_included_column, ic.key_ordinal, ic.index_column_id;

SELECT name, type_desc, physical_name, CAST(size AS bigint) * 8 / 1024 FROM sys.database_files ORDER BY file_id;";

        private const string BackupHistoryQuery = @"
SELECT TOP (300) bs.database_name, bmf.physical_device_name, bs.type, bs.backup_finish_date
FROM msdb.dbo.backupset AS bs
JOIN msdb.dbo.backupmediafamily AS bmf ON bmf.media_set_id = bs.media_set_id
WHERE bs.backup_finish_date IS NOT NULL
ORDER BY bs.backup_finish_date DESC;";

        public static DatabaseSchema Load(string connectionString, bool includeSystemObjects)
        {
            using (var cn = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(Query, cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@sys", includeSystemObjects ? 1 : 0);
                cn.Open();
                DatabaseSchema schema;
                using (var r = cmd.ExecuteReader())
                {
                    r.Read();
                    schema = new DatabaseSchema(r.GetString(0), r.IsDBNull(1) ? "dbo" : r.GetString(1));

                    r.NextResult();
                    while (r.Read()) schema.Schemas.Add(r.GetString(0));

                    r.NextResult();
                    while (r.Read())
                    {
                        var db = r.GetString(0);
                        schema.Databases.Add(new DbDatabase(db, r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3), r.GetByte(4)));
                        if (r.GetByte(1) == 0 && !string.Equals(db, schema.DatabaseName, StringComparison.OrdinalIgnoreCase)) schema.OtherDatabases.Add(db);
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

                    r.NextResult();
                    var indexes = new Dictionary<(int, int), DbIndex>();
                    while (r.Read())
                    {
                        if (!byId.TryGetValue(r.GetInt32(0), out var obj)) continue;
                        var ix = new DbIndex(r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetBoolean(5)) { IsDisabled = r.GetBoolean(6) };
                        obj.Indexes.Add(ix);
                        indexes[(r.GetInt32(0), r.GetInt32(1))] = ix;
                    }
                    r.NextResult();
                    while (r.Read())
                    {
                        if (!indexes.TryGetValue((r.GetInt32(0), r.GetInt32(1)), out var ix)) continue;
                        (r.GetBoolean(3) ? ix.IncludedColumns : ix.KeyColumns).Add(r.GetString(2));
                    }

                    r.NextResult();
                    while (r.Read())
                        schema.Files.Add(new DbFile(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3)));
                }
                LoadBackupHistory(cn, schema);
                LoadLinkedServers(cn, schema);
                // Separate round trips: missing permissions (or Azure SQL Database's server view) only lose principals.
                LoadPrincipals(cn, schema, DatabasePrincipalsQuery, serverLevel: false);
                LoadPrincipals(cn, schema, ServerPrincipalsQuery, serverLevel: true);
                return schema;
            }
        }

        private const string DatabasePrincipalsQuery = @"
SET NOCOUNT ON;
SELECT dp.name, dp.type, dp.type_desc, dp.is_fixed_role, CAST(0 AS bit), dp.default_schema_name, sp.name
FROM sys.database_principals AS dp
LEFT JOIN sys.server_principals AS sp ON sp.sid = dp.sid AND dp.type IN ('S', 'U', 'G', 'E', 'X')
WHERE dp.type IN ('S', 'U', 'G', 'E', 'X', 'C', 'K', 'R', 'A') AND dp.name NOT IN ('sys', 'INFORMATION_SCHEMA')
ORDER BY dp.name;

SELECT r.name, m.name
FROM sys.database_role_members AS rm
JOIN sys.database_principals AS r ON r.principal_id = rm.role_principal_id
JOIN sys.database_principals AS m ON m.principal_id = rm.member_principal_id
ORDER BY 1, 2;";

        private const string ServerPrincipalsQuery = @"
SET NOCOUNT ON;
SELECT name, type, type_desc, is_fixed_role, is_disabled, default_database_name, NULL
FROM sys.server_principals
WHERE type IN ('S', 'U', 'G', 'E', 'X', 'R') AND name NOT LIKE '##%'
ORDER BY name;

SELECT r.name, m.name
FROM sys.server_role_members AS rm
JOIN sys.server_principals AS r ON r.principal_id = rm.role_principal_id
JOIN sys.server_principals AS m ON m.principal_id = rm.member_principal_id
ORDER BY 1, 2;";

        private static void LoadLinkedServers(SqlConnection cn, DatabaseSchema schema)
        {
            try
            {
                using (var cmd = new SqlCommand("SELECT name FROM sys.servers WHERE is_linked = 1 ORDER BY name;", cn) { CommandTimeout = 15 })
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) schema.LinkedServers.Add(r.GetString(0));
            }
            catch (Exception ex)
            {
                Log.Info($"No linked servers for {schema.DatabaseName}: {ex.Message}");
            }
        }

        private static string Bracket(string name) => "[" + name.Replace("]", "]]") + "]";

        /// <summary>Databases of a linked server (four-part query through the local connection).</summary>
        public static DatabaseSchema LoadLinkedCatalog(string connectionString, string server)
        {
            var schema = new DatabaseSchema("master") { Server = server };
            using (var cn = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand($"SELECT name FROM {Bracket(server)}.master.sys.databases WHERE state = 0 ORDER BY name;", cn) { CommandTimeout = 30 })
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) schema.OtherDatabases.Add(r.GetString(0));
            }
            return schema;
        }

        /// <summary>Schemas, tables, views, routines and columns of a database on a linked server.</summary>
        public static DatabaseSchema LoadLinkedDatabase(string connectionString, string server, string database)
        {
            var prefix = Bracket(server) + "." + Bracket(database) + ".sys.";
            var query = $@"
SELECT name FROM {prefix}schemas
WHERE schema_id < 16384 AND name NOT IN ('guest', 'INFORMATION_SCHEMA', 'sys')
ORDER BY name;

SELECT o.object_id, s.name, o.name, o.type
FROM {prefix}objects AS o
JOIN {prefix}schemas AS s ON s.schema_id = o.schema_id
WHERE o.type IN ('U', 'V', 'P', 'FN', 'IF', 'TF', 'SN') AND o.is_ms_shipped = 0;

SELECT c.object_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed, c.column_id
FROM {prefix}columns AS c
JOIN {prefix}types AS t ON t.user_type_id = c.user_type_id
JOIN {prefix}objects AS o ON o.object_id = c.object_id
WHERE o.type IN ('U', 'V', 'IF', 'TF') AND o.is_ms_shipped = 0
ORDER BY 1, 10;";
            var schema = new DatabaseSchema(database) { Server = server };
            using (var cn = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(query, cn) { CommandTimeout = 60 })
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) schema.Schemas.Add(r.GetString(0));
                    r.NextResult();
                    var byId = new Dictionary<int, DbObject>();
                    while (r.Read())
                    {
                        var kind = Kind(r.GetString(3).Trim());
                        if (kind != null) byId[r.GetInt32(0)] = schema.Add(new DbObject(r.GetString(1), r.GetString(2), kind.Value));
                    }
                    r.NextResult();
                    while (r.Read())
                    {
                        if (!byId.TryGetValue(r.GetInt32(0), out var obj)) continue;
                        obj.Columns.Add(new DbColumn(r.GetString(1),
                            TypeText(r.GetString(2), r.GetInt16(3), r.GetByte(4), r.GetByte(5)),
                            r.GetBoolean(6), r.GetBoolean(7), r.GetBoolean(8), r.GetInt32(9)));
                    }
                }
            }
            return schema;
        }

        /// <summary>Recent backups from msdb, for RESTORE … FROM and hover; skipped without access to msdb.</summary>
        private static void LoadBackupHistory(SqlConnection cn, DatabaseSchema schema)
        {
            try
            {
                using (var cmd = new SqlCommand(BackupHistoryQuery, cn) { CommandTimeout = 15 })
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
                        var type = r.IsDBNull(2) ? ' ' : r.GetString(2).Trim().FirstOrDefault();
                        schema.Backups.Add(new DbBackup(r.GetString(0), r.GetString(1), type, r.GetDateTime(3)));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Info($"No backup history for {schema.DatabaseName}: {ex.Message}");
            }
        }

        private static void LoadPrincipals(SqlConnection cn, DatabaseSchema schema, string query, bool serverLevel)
        {
            try
            {
                var byName = new Dictionary<string, DbPrincipal>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = new SqlCommand(query, cn) { CommandTimeout = 30 })
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var type = r.GetString(1).Trim();
                        var kind = type == "R" ? (serverLevel ? DbPrincipalKind.ServerRole : DbPrincipalKind.DatabaseRole)
                            : type == "A" ? DbPrincipalKind.ApplicationRole
                            : serverLevel ? DbPrincipalKind.Login : DbPrincipalKind.User;
                        var p = new DbPrincipal(r.GetString(0), kind, r.IsDBNull(2) ? null : r.GetString(2))
                        {
                            IsFixedRole = !r.IsDBNull(3) && r.GetBoolean(3),
                            IsDisabled = !r.IsDBNull(4) && r.GetBoolean(4),
                            Default = r.IsDBNull(5) ? null : r.GetString(5),
                            Login = r.IsDBNull(6) ? null : r.GetString(6),
                        };
                        // public is a role in both catalogs but never fixed; treat it as built-in.
                        if (string.Equals(p.Name, "public", StringComparison.OrdinalIgnoreCase)) p.IsFixedRole = true;
                        byName[p.Name] = p;
                    }
                    r.NextResult();
                    while (r.Read())
                    {
                        if (byName.TryGetValue(r.GetString(0), out var role)) role.Members.Add(r.GetString(1));
                        if (byName.TryGetValue(r.GetString(1), out var member)) member.MemberOf.Add(r.GetString(0));
                    }
                }
                schema.Principals.AddRange(byName.Values);
            }
            catch (Exception ex)
            {
                Log.Info($"No {(serverLevel ? "server" : "database")} principals for {schema.DatabaseName}: {ex.Message}");
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
