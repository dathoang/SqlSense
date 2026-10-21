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
            var key = $"{conn.Server}|{database}|{conn.User}|{conn.AuthenticationType}".ToLowerInvariant();
            var entry = Cache.GetOrAdd(key, _ => new Entry());
            var maxAge = TimeSpan.FromMinutes(SqlSenseSettings.Current.SchemaCacheMinutes);
            lock (entry)
            {
                var stale = entry.Schema == null || DateTime.UtcNow - entry.LoadedUtc > maxAge;
                if (stale && entry.Loading == null && (entry.LastError == null || DateTime.UtcNow - entry.LoadedUtc > TimeSpan.FromMinutes(1)))
                    entry.Loading = Task.Run(() => Load(entry, key, connectionString));
                return entry.Schema;
            }
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

        public static void Clear()
        {
            Cache.Clear();
            Reported.Clear();
            Log.Info("Schema cache cleared; metadata reloads on next use.");
        }

        private static void Load(Entry entry, string key, string connectionString)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var schema = SchemaLoader.Load(connectionString, SqlSenseSettings.Current.IncludeSystemObjects);
                lock (entry)
                {
                    entry.Schema = schema;
                    entry.LoadedUtc = DateTime.UtcNow;
                    entry.LastError = null;
                }
                Log.Info($"Loaded {schema.Objects.Count} objects from {key.Split('|')[0]}/{schema.DatabaseName} in {sw.ElapsedMilliseconds} ms.");
                SchemaLoaded?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                lock (entry)
                {
                    entry.LastError = ex.Message;
                    entry.LoadedUtc = DateTime.UtcNow;
                }
                Log.Error($"Loading schema for {key.Split('|')[0]}/{key.Split('|')[1]}", ex);
            }
            finally
            {
                lock (entry) entry.Loading = null;
            }
        }
    }
}
