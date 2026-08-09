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
                            if (!string.IsNullOrWhiteSpace(proxyServer))
                            {
                                if (proxyServer.Contains("="))
                                {
                                    if (!proxyServer.Contains("=") && !proxyServer.Contains(";"))
                                    {
                                        proxy = new Uri($"http://{proxyServer}");
                                    }
                                }
                                else
                                {
                                    proxy = new Uri($"http://{proxyServer}");
                                }
                            }
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

        public bool IsBypassed(Uri host)
        {
            // We can implement "ProxyOverride" registry parsing here if needed.
            // For now, assume localhost is bypassed.
            return host.IsLoopback;
        }
    }
}
