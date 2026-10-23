using System;
using System.Collections.Concurrent;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Threading.Tasks;
using SqlSense.Core.Schema;
using SqlSense.Diagnostics;
using SqlSense.Settings;

namespace SqlSense.Schema
{
    /// <summary>
    /// Caches database metadata per server/database/login and loads it in the background, so a
    /// keystroke never waits on the network. Until a load finishes, suggestions work without schema.
    /// </summary>
    internal static class SchemaService
    {
        private sealed class Entry
        {
            public DatabaseSchema Schema;
            public DateTime LoadedUtc;
            public Task Loading;
            public string LastError;

            /// <summary>Reload on the next use (or right after the running load), keeping the current snapshot meanwhile.</summary>
            public bool ForceReload;
        }

        private static readonly ConcurrentDictionary<string, Entry> Cache = new ConcurrentDictionary<string, Entry>();
        private static readonly ConcurrentDictionary<string, bool> Reported = new ConcurrentDictionary<string, bool>();

        /// <summary>Raised (on a background thread) after a schema finishes loading.</summary>
        public static event EventHandler SchemaLoaded;

        /// <summary>
        /// Schema for the active window's connection, switched to <paramref name="databaseOverride"/>
        /// when the script has a USE statement. Returns null if not loaded yet or unavailable.
        /// </summary>
        public static DatabaseSchema Get(SsmsConnectionInfo conn, string databaseOverride)
        {
            if (conn == null) return null;
            if (conn.Unsupported != null)
            {
                if (Reported.TryAdd(conn.CacheKey, true)) Log.Info($"No schema suggestions for {conn.Server}: {conn.Unsupported}.");
                return null;
            }

            var connectionString = ConnectionStringFor(conn, databaseOverride, out var database);
            var key = KeyFor(conn, database);
            return GetCached(key, conn.Server + "/" + database, () =>
            {
                var schema = SchemaLoader.Load(connectionString, SqlSenseSettings.Current.IncludeSystemObjects);
                schema.Resolver = (server, db) => server == null ? Get(conn, db) : GetLinked(conn, server, db);
                return schema;
            });
        }

        /// <summary>
        /// A linked server's metadata read through the window's connection: its database list when
        /// <paramref name="database"/> is null, else that database's schemas, objects and columns. Null until loaded.
        /// </summary>
        public static DatabaseSchema GetLinked(SsmsConnectionInfo conn, string server, string database)
        {
            if (conn == null || conn.Unsupported != null || string.IsNullOrEmpty(server)) return null;
            var connectionString = conn.ConnectionString;
            var key = $"{conn.Server}|linked:{server}|{database}|{conn.User}|{conn.AuthenticationType}".ToLowerInvariant();
            return GetCached(key, server + "/" + (database ?? "databases") + " (via " + conn.Server + ")", () =>
            {
                var schema = database == null
                    ? SchemaLoader.LoadLinkedCatalog(connectionString, server)
                    : SchemaLoader.LoadLinkedDatabase(connectionString, server, database);
                schema.Resolver = (srv, db) => srv == null ? null : GetLinked(conn, srv, db);
                return schema;
            });
        }

        private static string KeyFor(SsmsConnectionInfo conn, string database) =>
            $"{conn.Server}|{database}|{conn.User}|{conn.AuthenticationType}".ToLowerInvariant();

        private static DatabaseSchema GetCached(string key, string label, Func<DatabaseSchema> load)
        {
            var entry = Cache.GetOrAdd(key, _ => new Entry());
            var maxAge = TimeSpan.FromMinutes(SqlSenseSettings.Current.SchemaCacheMinutes);
            lock (entry)
            {
                var stale = entry.Schema == null || entry.ForceReload || DateTime.UtcNow - entry.LoadedUtc > maxAge;
                if (stale && entry.Loading == null
                    && (entry.ForceReload || entry.LastError == null || DateTime.UtcNow - entry.LoadedUtc > TimeSpan.FromMinutes(1)))
                {
                    // First loads and asked-for refreshes are announced; periodic ones happen quietly behind a working list.
                    var announce = entry.Schema == null || entry.ForceReload ? label : null;
                    entry.ForceReload = false;
                    if (announce != null) SchemaStatus.Started(announce);
                    entry.Loading = Task.Run(() => Load(entry, key, label, announce, load));
                }
                return entry.Schema;
            }
        }

        /// <summary>
        /// Reloads the window's database now (the one <paramref name="databaseOverride"/> names, if set). Suggestions keep
        /// the current snapshot until the new one arrives; a load already running is followed by another.
        /// </summary>
        public static bool Refresh(SsmsConnectionInfo conn, string databaseOverride)
        {
            if (conn == null || conn.Unsupported != null) return false;
            ConnectionStringFor(conn, databaseOverride, out var database);
            var entry = Cache.GetOrAdd(KeyFor(conn, database), _ => new Entry());
            lock (entry) entry.ForceReload = true;
            Get(conn, databaseOverride);
            return true;
        }

        /// <summary>Marks every cached database and linked server for reload on next use, keeping the snapshots meanwhile.</summary>
        public static void RefreshAll()
        {
            foreach (var entry in Cache.Values)
                lock (entry) entry.ForceReload = true;
            Reported.Clear();
            Log.Info("All cached metadata marked for reload on next use.");
        }

        /// <summary>The window's connection string, switched to <paramref name="databaseOverride"/> when set. Null if unusable.</summary>
        public static string ConnectionStringFor(SsmsConnectionInfo conn, string databaseOverride, out string database)
        {
            database = conn?.Database;
            if (conn == null || conn.Unsupported != null) return null;
            if (string.IsNullOrEmpty(databaseOverride) || string.Equals(databaseOverride, conn.Database, StringComparison.OrdinalIgnoreCase))
                return conn.ConnectionString;
            database = databaseOverride;
            return new SqlConnectionStringBuilder(conn.ConnectionString) { InitialCatalog = databaseOverride }.ConnectionString;
        }

        private static string FirstLine(string message)
        {
            var line = (message ?? "").Split('\n')[0].Trim();
            return line.Length > 120 ? line.Substring(0, 120) + "…" : line;
        }

        private static void Load(Entry entry, string key, string label, string announce, Func<DatabaseSchema> load)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var schema = load();
                lock (entry)
                {
                    entry.Schema = schema;
                    entry.LoadedUtc = DateTime.UtcNow;
                    entry.LastError = null;
                }
                Log.Info($"Loaded {schema.Objects.Count} objects from {label} in {sw.ElapsedMilliseconds} ms.");
                if (announce != null)
                    SchemaStatus.Finished(announce, schema.Objects.Count > 0 || schema.Server == null
                        ? $"metadata ready for {label}: {schema.Objects.Count:N0} objects in {sw.Elapsed.TotalSeconds:0.0} s"
                        : $"{schema.OtherDatabases.Count:N0} databases on {label} in {sw.Elapsed.TotalSeconds:0.0} s");
                SchemaLoaded?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                lock (entry)
                {
                    entry.LastError = ex.Message;
                    entry.LoadedUtc = DateTime.UtcNow;
                }
                Log.Error($"Loading schema for {label}", ex);
                if (announce != null)
                    SchemaStatus.Finished(announce, $"couldn't load metadata for {label}: {FirstLine(ex.Message)} (Tools › SqlSense › Show Log)");
            }
            finally
            {
                bool again;
                lock (entry)
                {
                    entry.Loading = null;
                    again = entry.ForceReload;
                }
                // Asked to refresh while this load ran (DDL just executed): the snapshot may predate it.
                if (again) GetCached(key, label, load);
            }
        }
    }
}
