using System;
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using SqlSense.Diagnostics;

namespace SqlSense.Schema
{
    /// <summary>Connection details of the active SSMS query window.</summary>
    internal sealed class SsmsConnectionInfo
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string User { get; set; }
        public int AuthenticationType { get; set; }
        public string ConnectionString { get; set; }

        /// <summary>Why a connection string could not be built (null when it could).</summary>
        public string Unsupported { get; set; }

        public string CacheKey => $"{Server}|{Database}|{User}|{AuthenticationType}".ToLowerInvariant();
    }

    /// <summary>
    /// Reads the active query window's connection from SSMS 22. SSMS exposes this only through
    /// internal types, so it is read by reflection:
    /// ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo.UIConnectionInfo.
    /// </summary>
    internal static class SsmsConnection
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static Type _serviceCache;
        private static bool _loggedMissing;

        public static SsmsConnectionInfo GetActive()
        {
            try
            {
                var factory = GetServiceCache()?.GetProperty("ScriptFactory", Any)?.GetValue(null);
                var wnd = factory?.GetType().GetProperty("CurrentlyActiveWndConnectionInfo", Any)?.GetValue(factory);
                var ui = wnd?.GetType().GetProperty("UIConnectionInfo", Any)?.GetValue(wnd);
                if (ui == null) return null;
                return FromUiConnectionInfo(ui);
            }
            catch (Exception ex)
            {
                Log.Error("Reading the SSMS connection", ex);
                return null;
            }
        }

        private static Type GetServiceCache()
        {
            if (_serviceCache != null) return _serviceCache;
            _serviceCache = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "SqlPackageBase")
                .Select(a => a.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache"))
                .FirstOrDefault(t => t != null);
            if (_serviceCache == null)
            {
                try
                {
                    _serviceCache = Assembly.Load("SqlPackageBase").GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
                }
                catch (Exception ex)
                {
                    if (!_loggedMissing) Log.Error("SqlPackageBase.ServiceCache not found; schema suggestions are off", ex);
                    _loggedMissing = true;
                }
            }
            return _serviceCache;
        }

        private static SsmsConnectionInfo FromUiConnectionInfo(object ui)
        {
            var t = ui.GetType();
            string Str(string name) => t.GetProperty(name, Any)?.GetValue(ui) as string;

            var info = new SsmsConnectionInfo
            {
                Server = Str("ServerName"),
                User = Str("UserName"),
            };
            var authObj = t.GetProperty("AuthenticationType", Any)?.GetValue(ui);
            info.AuthenticationType = authObj == null ? 0 : Convert.ToInt32(authObj);

            var advanced = t.GetProperty("AdvancedOptions", Any)?.GetValue(ui) as NameValueCollection;
            info.Database = advanced?["DATABASE"];
            if (string.IsNullOrEmpty(info.Server)) return null;

            var b = new SqlConnectionStringBuilder
            {
                DataSource = info.Server,
                InitialCatalog = string.IsNullOrEmpty(info.Database) ? "master" : info.Database,
                ApplicationName = "SqlSense (metadata)",
                ConnectTimeout = 15,
                Pooling = true,
            };
            ApplyEncryption(b, advanced);

            switch (info.AuthenticationType)
            {
                case 0: // Windows
                    b.IntegratedSecurity = true;
                    break;
                case 1: // SQL Server
                    var password = Str("Password");
                    if (string.IsNullOrEmpty(password))
                        password = ToPlain(t.GetProperty("InMemoryPassword", Any)?.GetValue(ui) as SecureString);
                    if (string.IsNullOrEmpty(password))
                    {
                        info.Unsupported = "SSMS did not expose the SQL login password for this window";
                        return info;
                    }
                    b.UserID = info.User;
                    b.Password = password;
                    break;
                default:
                    info.Unsupported = "Microsoft Entra / Azure AD authentication (type " + info.AuthenticationType + ") is not supported yet";
                    return info;
            }
            info.ConnectionString = b.ConnectionString;
            return info;
        }

        private static void ApplyEncryption(SqlConnectionStringBuilder b, NameValueCollection advanced)
        {
            if (advanced == null) return;
            foreach (string key in advanced.Keys)
            {
                if (key == null) continue;
                var value = advanced[key] ?? string.Empty;
                var k = key.ToUpperInvariant();
                if (k.Contains("ENCRYPT"))
                    b.Encrypt = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("mandatory", StringComparison.OrdinalIgnoreCase)
                                || value.Equals("strict", StringComparison.OrdinalIgnoreCase) || value == "1";
                else if (k.Contains("TRUST") && k.Contains("CERT"))
                    b.TrustServerCertificate = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
            }
        }

        private static string ToPlain(SecureString s)
        {
            if (s == null || s.Length == 0) return null;
            var ptr = Marshal.SecureStringToGlobalAllocUnicode(s);
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }
}
