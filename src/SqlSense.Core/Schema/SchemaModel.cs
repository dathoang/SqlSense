using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlSense.Core.Schema
{
    public enum DbObjectKind
    {
        Table,
        View,
        Procedure,
        ScalarFunction,
        TableFunction,
        Synonym,
        TableType,
        Trigger,
    }

    public sealed class DbColumn
    {
        public DbColumn(string name, string dataType, bool nullable, bool isIdentity, bool isComputed, int ordinal)
        {
            Name = name;
            DataType = dataType;
            Nullable = nullable;
            IsIdentity = isIdentity;
            IsComputed = isComputed;
            Ordinal = ordinal;
        }

        public string Name { get; }
        public string DataType { get; }
        public bool Nullable { get; }
        public bool IsIdentity { get; }
        public bool IsComputed { get; }
        public int Ordinal { get; }

        public string Describe() => $"{Name} ({DataType}, {(Nullable ? "null" : "not null")}{(IsIdentity ? ", identity" : "")})";
    }

    public sealed class DbParameter
    {
        public DbParameter(string name, string dataType, bool isOutput, bool hasDefault, int ordinal)
        {
            Name = name;
            DataType = dataType;
            IsOutput = isOutput;
            HasDefault = hasDefault;
            Ordinal = ordinal;
        }

        public string Name { get; }
        public string DataType { get; }
        public bool IsOutput { get; }
        public bool HasDefault { get; }
        public int Ordinal { get; }
    }

    public sealed class DbIndex
    {
        public DbIndex(string name, string typeDescription, bool isUnique, bool isPrimaryKey)
        {
            Name = name;
            TypeDescription = typeDescription;
            IsUnique = isUnique;
            IsPrimaryKey = isPrimaryKey;
        }

        public string Name { get; }

        /// <summary>CLUSTERED, NONCLUSTERED, CLUSTERED COLUMNSTORE … (sys.indexes.type_desc).</summary>
        public string TypeDescription { get; }
        public bool IsUnique { get; }
        public bool IsPrimaryKey { get; }
        public bool IsDisabled { get; set; }
        public List<string> KeyColumns { get; } = new List<string>();
        public List<string> IncludedColumns { get; } = new List<string>();

        public string Describe()
        {
            var kind = (IsPrimaryKey ? "primary key " : IsUnique ? "unique " : "") + (TypeDescription ?? "").ToLowerInvariant().Replace('_', ' ') + " index";
            var cols = KeyColumns.Count > 0 ? " (" + string.Join(", ", KeyColumns) + ")" : "";
            var include = IncludedColumns.Count > 0 ? " include (" + string.Join(", ", IncludedColumns) + ")" : "";
            return kind.Trim() + cols + include + (IsDisabled ? ", disabled" : "");
        }
    }

    /// <summary>A file of the current database (sys.database_files).</summary>
    public sealed class DbFile
    {
        public DbFile(string name, string typeDescription, string physicalName, long sizeMb)
        {
            Name = name;
            TypeDescription = typeDescription;
            PhysicalName = physicalName;
            SizeMb = sizeMb;
        }

        /// <summary>Logical name.</summary>
        public string Name { get; }

        /// <summary>ROWS, LOG, FILESTREAM …</summary>
        public string TypeDescription { get; }
        public string PhysicalName { get; }
        public long SizeMb { get; }

        public string Describe() => $"{(TypeDescription ?? "").ToLowerInvariant()} file, {SizeMb:N0} MB — {PhysicalName}";
    }

    /// <summary>A database on the server (sys.databases).</summary>
    public sealed class DbDatabase
    {
        public DbDatabase(string name, string state, string recoveryModel, int compatibilityLevel)
        {
            Name = name;
            State = state;
            RecoveryModel = recoveryModel;
            CompatibilityLevel = compatibilityLevel;
        }

        public string Name { get; }

        /// <summary>ONLINE, RESTORING, OFFLINE …</summary>
        public string State { get; }

        /// <summary>FULL, BULK_LOGGED or SIMPLE.</summary>
        public string RecoveryModel { get; }
        public int CompatibilityLevel { get; }
    }

    /// <summary>One backup file from msdb's history.</summary>
    public sealed class DbBackup
    {
        public DbBackup(string database, string device, char type, DateTime finished)
        {
            Database = database;
            Device = device;
            Type = type;
            Finished = finished;
        }

        public string Database { get; }

        /// <summary>Physical device name: a file path or URL.</summary>
        public string Device { get; }

        /// <summary>D full, I differential, L log (msdb backupset.type).</summary>
        public char Type { get; }
        public DateTime Finished { get; }

        public string TypeText => Type == 'D' ? "full" : Type == 'I' ? "differential" : Type == 'L' ? "log" : "other";
    }

    public sealed class DbObject
    {
        public DbObject(string schema, string name, DbObjectKind kind)
        {
            Schema = schema;
            Name = name;
            Kind = kind;
        }

        public string Schema { get; }
        public string Name { get; }
        public DbObjectKind Kind { get; }
        public List<DbColumn> Columns { get; } = new List<DbColumn>();
        public List<DbParameter> Parameters { get; } = new List<DbParameter>();

        /// <summary>For a table or view: its named indexes.</summary>
        public List<DbIndex> Indexes { get; } = new List<DbIndex>();

        /// <summary>For a trigger: the table or view it is on (schema.name).</summary>
        public string Parent { get; set; }

        public bool HasColumns => Kind == DbObjectKind.Table || Kind == DbObjectKind.View
                                  || Kind == DbObjectKind.TableFunction || Kind == DbObjectKind.TableType;

        public string QualifiedName => $"{Schema}.{Name}";

        public override string ToString() => QualifiedName;
    }

    public sealed class DbForeignKey
    {
        public DbForeignKey(string name, string fromSchema, string fromTable, string toSchema, string toTable,
            IReadOnlyList<string> fromColumns, IReadOnlyList<string> toColumns)
        {
            Name = name;
            FromSchema = fromSchema;
            FromTable = fromTable;
            ToSchema = toSchema;
            ToTable = toTable;
            FromColumns = fromColumns;
            ToColumns = toColumns;
        }

        public string Name { get; }
        public string FromSchema { get; }
        public string FromTable { get; }
        public string ToSchema { get; }
        public string ToTable { get; }
        public IReadOnlyList<string> FromColumns { get; }
        public IReadOnlyList<string> ToColumns { get; }
    }

    public enum DbPrincipalKind
    {
        User,
        DatabaseRole,
        ApplicationRole,
        Login,
        ServerRole,
    }

    /// <summary>A database principal (user, role) or a server principal (login, server role).</summary>
    public sealed class DbPrincipal
    {
        public DbPrincipal(string name, DbPrincipalKind kind, string typeDescription = null)
        {
            Name = name;
            Kind = kind;
            TypeDescription = typeDescription;
        }

        public string Name { get; }
        public DbPrincipalKind Kind { get; }

        /// <summary>The catalog's type_desc (SQL_USER, WINDOWS_GROUP …), if known.</summary>
        public string TypeDescription { get; }

        /// <summary>Built-in role such as db_owner or sysadmin.</summary>
        public bool IsFixedRole { get; set; }

        public bool IsDisabled { get; set; }

        /// <summary>User: its default schema. Login: its default database.</summary>
        public string Default { get; set; }

        /// <summary>User: the login it maps to.</summary>
        public string Login { get; set; }

        /// <summary>Role: names of its members.</summary>
        public List<string> Members { get; } = new List<string>();

        /// <summary>Names of the roles this principal belongs to.</summary>
        public List<string> MemberOf { get; } = new List<string>();

        public bool IsServerLevel => Kind == DbPrincipalKind.Login || Kind == DbPrincipalKind.ServerRole;

        public bool IsRole => Kind == DbPrincipalKind.DatabaseRole || Kind == DbPrincipalKind.ApplicationRole || Kind == DbPrincipalKind.ServerRole;

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case DbPrincipalKind.User: return "user";
                    case DbPrincipalKind.DatabaseRole: return "database role";
                    case DbPrincipalKind.ApplicationRole: return "application role";
                    case DbPrincipalKind.Login: return "login";
                    default: return "server role";
                }
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>Snapshot of one database's metadata.</summary>
    public sealed class DatabaseSchema
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
        private readonly Dictionary<string, DbObject> _byQualified = new Dictionary<string, DbObject>(Ci);
        private readonly Dictionary<string, List<DbObject>> _byName = new Dictionary<string, List<DbObject>>(Ci);

        public DatabaseSchema(string databaseName, string defaultSchema = "dbo")
        {
            DatabaseName = databaseName;
            DefaultSchema = string.IsNullOrEmpty(defaultSchema) ? "dbo" : defaultSchema;
        }

        public string DatabaseName { get; }
        public string DefaultSchema { get; }
        public List<string> Schemas { get; } = new List<string>();
        public List<string> OtherDatabases { get; } = new List<string>();
        public List<DbObject> Objects { get; } = new List<DbObject>();
        public List<DbForeignKey> ForeignKeys { get; } = new List<DbForeignKey>();

        /// <summary>Every database on the server with its state and recovery model (may be empty).</summary>
        public List<DbDatabase> Databases { get; } = new List<DbDatabase>();

        /// <summary>Files of this database.</summary>
        public List<DbFile> Files { get; } = new List<DbFile>();

        /// <summary>Recent backups from msdb, newest first (empty without access to msdb).</summary>
        public List<DbBackup> Backups { get; } = new List<DbBackup>();

        public DbDatabase FindDatabase(string name) => Databases.FirstOrDefault(d => Ci.Equals(d.Name, name));

        /// <summary>Linked servers of this server (sys.servers).</summary>
        public List<string> LinkedServers { get; } = new List<string>();

        /// <summary>For a linked server's catalog-only snapshot (no database yet): the server's name.</summary>
        public string Server { get; set; }

        /// <summary>
        /// Supplies another database's snapshot: (null, db) for this server, (linked server, null) for the list of
        /// its databases, (linked server, db) for one of them. Returns null while it loads or when unavailable;
        /// it must never block.
        /// </summary>
        public Func<string, string, DatabaseSchema> Resolver { get; set; }

        public bool IsLinkedServer(string name) => LinkedServers.Any(s => Ci.Equals(s, name));

        /// <summary>
        /// The snapshot for a [server.]database qualifier: this one for the current database, another through
        /// <see cref="Resolver"/>. <paramref name="pending"/> is true when it exists but isn't loaded yet.
        /// </summary>
        public DatabaseSchema For(string server, string database, out bool pending)
        {
            pending = false;
            if (string.IsNullOrEmpty(server))
            {
                if (string.IsNullOrEmpty(database) || Ci.Equals(database, DatabaseName)) return this;
                if (!IsDatabase(database) && FindDatabase(database) == null) return null;
                var other = Resolver?.Invoke(null, database);
                pending = other == null && Resolver != null;
                return other;
            }
            if (!IsLinkedServer(server)) return null;
            var catalog = Resolver?.Invoke(server, null);
            if (catalog == null)
            {
                pending = Resolver != null;
                return null;
            }
            if (string.IsNullOrEmpty(database)) return catalog;
            if (!catalog.IsDatabase(database)) return null;
            var remote = Resolver(server, database);
            pending = remote == null;
            return remote;
        }

        public DatabaseSchema For(string server, string database) => For(server, database, out _);

        /// <summary>Users and roles of this database, plus the server's logins and server roles.</summary>
        public List<DbPrincipal> Principals { get; } = new List<DbPrincipal>();

        /// <summary>A principal by name, preferring database principals unless <paramref name="serverLevel"/> says otherwise.</summary>
        public DbPrincipal FindPrincipal(string name, bool? serverLevel = null)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var matches = Principals.Where(p => Ci.Equals(p.Name, name) && (serverLevel == null || p.IsServerLevel == serverLevel.Value));
            return matches.OrderBy(p => p.IsServerLevel).FirstOrDefault();
        }

        public DbObject Add(DbObject obj)
        {
            Objects.Add(obj);
            _byQualified[obj.QualifiedName] = obj;
            if (!_byName.TryGetValue(obj.Name, out var list)) _byName[obj.Name] = list = new List<DbObject>();
            list.Add(obj);
            return obj;
        }

        /// <summary>Resolve a one- or two-part name. One-part names prefer the default schema, then dbo.</summary>
        public DbObject Find(string schema, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!string.IsNullOrEmpty(schema))
                return _byQualified.TryGetValue(schema + "." + name, out var exact) ? exact : null;
            if (!_byName.TryGetValue(name, out var candidates) || candidates.Count == 0) return null;
            return candidates.FirstOrDefault(o => Ci.Equals(o.Schema, DefaultSchema))
                   ?? candidates.FirstOrDefault(o => Ci.Equals(o.Schema, "dbo"))
                   ?? candidates[0];
        }

        public bool IsSchema(string name) => Schemas.Any(s => Ci.Equals(s, name));
        public bool IsDatabase(string name) => Ci.Equals(name, DatabaseName) || OtherDatabases.Any(d => Ci.Equals(d, name));
    }

    /// <summary>Supplies schema for the active editor; returns null when no connection or not loaded yet.</summary>
    public interface ISchemaProvider
    {
        DatabaseSchema GetSchema();
    }
}
