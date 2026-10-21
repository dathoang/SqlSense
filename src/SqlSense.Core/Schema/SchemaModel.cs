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
