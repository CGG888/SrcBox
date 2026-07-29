using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LibmpvIptvClient.Services
{
    public class HttpClientService
    {
        private static readonly Lazy<HttpClientService> _instance = new Lazy<HttpClientService>(() => new HttpClientService());
        public static HttpClientService Instance => _instance.Value;

        private HttpClient? _client;
        private readonly object _lock = new object();

        private HttpClientService()
        {
            // We still listen to events just for logging or advanced triggers, 
            // but the heavy lifting is now done by RegistryProxyProvider polling
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[HttpClientService] UserPreferenceChanged: {e.Category}");
            };
        }

        public HttpClient Client
        {
            get
            {
                lock (_lock)
                {
                    if (_client == null)
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Debug("[HttpClientService] Creating new HttpClient instance...");
                        _client = CreateClient();
                    }
                    return _client;
                }
            }
        }

        public void InvalidateClient()
        {
            // NO-OP or just logging.
            // Since we are now using a dynamic RegistryProxyProvider + Short PooledConnectionLifetime,
            // we don't strictly need to dispose the HttpClient instance anymore.
            // The SocketsHttpHandler will query our GetProxy() method for every new connection.
            LibmpvIptvClient.Diagnostics.Logger.Trace("[HttpClientService] Proxy change detected (Registry/Event). Future connections will adapt automatically.");
        }

        private HttpClient CreateClient()
        {
            // Use our custom Registry-based proxy provider
            // This reads HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings directly
            // completely bypassing any .NET framework caching of proxy settings.
            var proxy = new RegistryProxyProvider();
            
            var handler = new SocketsHttpHandler
            {
                UseProxy = true,
                Proxy = proxy,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = true,
                ConnectTimeout = TimeSpan.FromSeconds(10)
            };
            
            handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true;

            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);
            
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) SrcBox/1.0 Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "*/*");
            
            return client;
        }

        // Remove old SystemWebProxy class as we use RegistryProxyProvider now
    }
}
