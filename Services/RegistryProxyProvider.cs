using System;
using System.Net;
using Microsoft.Win32;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Reads proxy settings directly from the Windows Registry to bypass .NET's caching mechanisms.
    /// Uses a short TTL cache to reduce per-connection registry overhead.
    /// </summary>
    public class RegistryProxyProvider : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        // Cache proxy result for 5 seconds to avoid per-connection registry reads (OPT-1)
        private static (Uri? proxy, DateTime cachedAt) _cachedProxy = (null, DateTime.MinValue);
        private const int ProxyCacheTtlMs = 5000;

        public Uri? GetProxy(Uri destination)
        {
            // Check cache first (OPT-1)
            if ((DateTime.Now - _cachedProxy.cachedAt).TotalMilliseconds < ProxyCacheTtlMs)
            {
                return _cachedProxy.proxy;
            }

            Uri? proxy = null;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (key != null)
                    {
                        var proxyEnable = key.GetValue("ProxyEnable") as int?;
                        if (proxyEnable == 1)
                        {
                            var proxyServer = key.GetValue("ProxyServer") as string;
                            proxy = ParseProxyServer(proxyServer);
                        }
                    }
                }
            }
            catch
            {
                // Fallback: ignore
            }

            // Update cache
            _cachedProxy = (proxy, DateTime.Now);
            return proxy;
        }

        /// <summary>
        /// Parses the Windows "ProxyServer" registry value. It is either a single "host:port" or a
        /// per-protocol list such as "http=host:port;https=host:port" (also "socks=...", "ftp=...").
        /// The previous implementation nested "!Contains('=')" inside "Contains('=')", which can never be
        /// true, so every per-protocol value was silently ignored and the app went direct.
        /// </summary>
        public static Uri? ParseProxyServer(string? proxyServer)
        {
            if (string.IsNullOrWhiteSpace(proxyServer)) return null;

            var value = proxyServer.Trim();

            if (value.Contains('='))
            {
                string? httpEntry = null;
                string? firstEntry = null;

                foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length != 2) continue;

                    var scheme = kv[0].Trim().ToLowerInvariant();
                    var host = kv[1].Trim();
                    if (host.Length == 0 || scheme.Length == 0) continue;

                    firstEntry ??= host;
                    if (scheme == "http") httpEntry ??= host;
                }

                value = httpEntry ?? firstEntry ?? "";
            }

            if (value.Length == 0) return null;
            if (!value.Contains("://")) value = "http://" + value;

            return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
        }

        public bool IsBypassed(Uri host)
        {
            // We can implement "ProxyOverride" registry parsing here if needed.
            // For now, assume localhost is bypassed.
            return host.IsLoopback;
        }
    }
}
