using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace LibmpvIptvClient.Services
{
    public static class HttpClientExtensions
    {
        public static async Task<HttpResponseMessage> SendAsyncWithRetry(this HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            try
            {
                return await client.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                LibmpvIptvClient.Diagnostics.Logger.Trace($"[HttpClientExtensions] Request via Proxy failed ({ex.Message}). Trying DIRECT connection...");
                
                // Fallback Strategy: Create a temporary DIRECT (No Proxy) client
                // This handles cases where the system proxy is stuck or invalid
                using (var directHandler = new SocketsHttpHandler
                {
                    UseProxy = false, // Force Direct
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                    PooledConnectionLifetime = TimeSpan.FromSeconds(10) // Short life
                })
                using (var directClient = new HttpClient(directHandler))
                {
                    directClient.Timeout = TimeSpan.FromSeconds(10); // Fast fail for fallback
                    // Copy headers
                    foreach (var header in client.DefaultRequestHeaders) directClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                    
                    var newRequest = CloneRequest(request);
                    try 
                    {
                        var response = await directClient.SendAsync(newRequest, cancellationToken);
                        // If success, it means Proxy was indeed the issue. 
                        // We should invalidate the main client so it might pick up correct settings next time.
                        HttpClientService.Instance.InvalidateClient();
                        return response;
                    }
                    catch
                    {
                        // If direct also fails, throw the ORIGINAL proxy exception (it was likely the "correct" network path, just broken)
                        throw ex;
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
