using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace LibmpvIptvClient.Services
{
    public static class HttpClientExtensions
    {
        /// <summary>
        /// Hard limit for downloaded playlists / EPG documents. Without it a huge or hostile document (or a
        /// gzip bomb) is buffered in memory several times over and can take the whole process down.
        /// </summary>
        public const long MaxDownloadBytes = 64L * 1024 * 1024;

        // Static shared handler for direct (no-proxy) fallback - reused across calls (OPT-7)
        private static readonly SocketsHttpHandler s_directHandler = new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromSeconds(10)
        };

        public static async Task<HttpResponseMessage> SendAsyncWithRetry(this HttpClient client, HttpRequestMessage request,
            CancellationToken cancellationToken = default,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
        {
            try
            {
                return await client.SendAsync(request, completionOption, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                LibmpvIptvClient.Diagnostics.Logger.Trace($"[HttpClientExtensions] Request via Proxy failed ({ex.Message}). Trying DIRECT connection...");

                // Fallback Strategy: Use shared DIRECT (No Proxy) handler (OPT-7)
                // Create the client with disposeHandler: false: HttpClient owns its handler by default, so
                // the old "using" disposed the shared handler and every later fallback failed instantly.
                var directClient = new HttpClient(s_directHandler, disposeHandler: false)
                {
                    Timeout = TimeSpan.FromSeconds(10) // Fast fail for fallback
                };
                // Copy headers
                foreach (var header in client.DefaultRequestHeaders) directClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);

                var newRequest = CloneRequest(request);
                try
                {
                    var response = await directClient.SendAsync(newRequest, completionOption, cancellationToken);
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
        
        public static async Task<string> GetStringAsyncWithRetry(this HttpClient client, string url, long maxBytes = MaxDownloadBytes)
        {
            var bytes = await client.GetByteArrayAsyncWithRetry(url, maxBytes);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        
        public static async Task<byte[]> GetByteArrayAsyncWithRetry(this HttpClient client, string url, long maxBytes = MaxDownloadBytes)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await client.SendAsyncWithRetry(request, completionOption: HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                return await ReadBoundedAsync(response, maxBytes);
            }
        }

        /// <summary>Reads the response body while enforcing a byte limit, so an oversized document is
        /// rejected instead of buffered.</summary>
        static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, long maxBytes)
        {
            var declared = response.Content.Headers.ContentLength;
            if (declared.HasValue && declared.Value > maxBytes)
            {
                throw new InvalidOperationException($"响应体过大（{declared.Value} 字节，上限 {maxBytes} 字节）");
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new InvalidOperationException($"响应体超过上限 {maxBytes} 字节");
                }
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
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
