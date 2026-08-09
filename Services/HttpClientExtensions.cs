using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace LibmpvIptvClient.Services
{
    public static class HttpClientExtensions
    {
        // Static shared handler for direct (no-proxy) fallback - reused across calls (OPT-7)
        private static readonly SocketsHttpHandler s_directHandler = new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromSeconds(10)
        };

        public static async Task<HttpResponseMessage> SendAsyncWithRetry(this HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            try
            {
                return await client.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                LibmpvIptvClient.Diagnostics.Logger.Trace($"[HttpClientExtensions] Request via Proxy failed ({ex.Message}). Trying DIRECT connection...");

                // Fallback Strategy: Use shared DIRECT (No Proxy) handler (OPT-7)
                // Reusing the static handler avoids per-fallback allocation
                using (var directClient = new HttpClient(s_directHandler))
                {
                    directClient.Timeout = TimeSpan.FromSeconds(10); // Fast fail for fallback
                    // Copy headers
                    foreach (var header in client.DefaultRequestHeaders) directClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);

                    var newRequest = CloneRequest(request);
                    try
                    {
                        var response = await directClient.SendAsync(newRequest, cancellationToken);
                        HttpClientService.Instance.InvalidateClient();
                        return response;
                    }
                    catch (Exception ex2)
                    {
                        // NEW-20: Throw AggregateException so caller sees BOTH failures
                        throw new AggregateException($"Proxy failed ({ex.Message}), then DIRECT also failed ({ex2.Message})", ex, ex2);
                    }
                }
            }
        }
        
        public static async Task<string> GetStringAsyncWithRetry(this HttpClient client, string url)
        {
            // Use SendAsyncWithRetry to leverage the fallback logic
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await client.SendAsyncWithRetry(request))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }
        
        public static async Task<byte[]> GetByteArrayAsyncWithRetry(this HttpClient client, string url)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await client.SendAsyncWithRetry(request))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync();
            }
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            clone.Content = request.Content;
            clone.Version = request.Version;
            foreach (var prop in request.Options) clone.Options.Set(new HttpRequestOptionsKey<object?>(prop.Key), prop.Value);
            foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return clone;
        }
    }
}
