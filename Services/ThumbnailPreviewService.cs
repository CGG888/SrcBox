using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Provides channel preview thumbnails via rtp2httpd's video snapshot feature.
    /// Uses X-Request-Snapshot HTTP header only — does NOT modify URL query parameters.
    /// Only operates on HTTP/HTTPS (rtp2httpd proxy) URLs; raw UDP/RTP/file URLs return null immediately.
    /// </summary>
    public class ThumbnailPreviewService
    {
        public static ThumbnailPreviewService Instance { get; } = new ThumbnailPreviewService();

        // LRU cache: stream URL -> cached thumbnail
        private readonly ConcurrentDictionary<string, CachedThumbnail> _cache = new ConcurrentDictionary<string, CachedThumbnail>(StringComparer.OrdinalIgnoreCase);

        // Prevent duplicate concurrent requests for the same URL
        private readonly ConcurrentDictionary<string, Task<BitmapImage?>> _pendingRequests = new ConcurrentDictionary<string, Task<BitmapImage?>>(StringComparer.OrdinalIgnoreCase);

        // Limit concurrent snapshot requests to avoid overloading rtp2httpd
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(2, 2);

        private readonly HttpClient _http;

        // Cache TTL in seconds
        private const int CACHE_TTL_SECONDS = 30;

        // Maximum cached thumbnails
        private const int MAX_CACHE_SIZE = 50;

        private ThumbnailPreviewService()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(5); // Fast fail for preview
        }

        /// <summary>
        /// Gets a preview thumbnail for the given stream URL.
        /// Uses rtp2httpd X-Request-Snapshot HTTP header only (does NOT modify URL).
        /// Returns null if snapshot is unavailable or URL is not a HTTP/HTTPS rtp2httpd proxy URL.
        /// </summary>
        public Task<BitmapImage?> GetPreviewAsync(string streamUrl)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
                return Task.FromResult<BitmapImage?>(null);

            // Check LRU cache first
            if (_cache.TryGetValue(streamUrl, out var cached) && !cached.IsExpired)
                return Task.FromResult<BitmapImage?>(cached.Image);

            // Deduplicate concurrent requests for the same URL
            var task = _pendingRequests.GetOrAdd(streamUrl, url => FetchSnapshotAsync(url));
            return task;
        }

        private async Task<BitmapImage?> FetchSnapshotAsync(string streamUrl)
        {
            try
            {
                await _semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    // Double-check cache after acquiring semaphore (another request might have populated it)
                    if (_cache.TryGetValue(streamUrl, out var cached) && !cached.IsExpired)
                        return cached.Image;

                    // Only request snapshot for HTTP/HTTPS URLs (rtp2httpd proxy URLs).
                    // Raw UDP/RTP/file URLs are NOT proxy URLs — skip them entirely.
                    if (!streamUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !streamUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    // Use X-Request-Snapshot HTTP header ONLY — does NOT modify the URL
                    using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                    request.Headers.TryAddWithoutValidation("X-Request-Snapshot", "1");

                    using var response = await _http.SendAsync(request).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Debug($"[Thumbnail] rtp2httpd snapshot failed: {response.StatusCode} for {streamUrl}");
                        return null;
                    }

                    var contentType = response.Content.Headers.ContentType?.MediaType;
                    if (string.IsNullOrEmpty(contentType) || !contentType.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Debug($"[Thumbnail] rtp2httpd snapshot returned non-JPEG content-type: {contentType} for {streamUrl}");
                        return null;
                    }

                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (bytes == null || bytes.Length == 0)
                    {
                        Logger.Debug($"[Thumbnail] rtp2httpd snapshot returned empty data for {streamUrl}");
                        return null;
                    }

                    // Validate minimum JPEG size (SOI marker = 0xFF 0xD8)
                    if (bytes.Length < 2 || bytes[0] != 0xFF || bytes[1] != 0xD8)
                    {
                        Logger.Debug($"[Thumbnail] rtp2httpd snapshot returned non-JPEG data ({bytes.Length} bytes) for {streamUrl}");
                        return null;
                    }

                    var bitmap = LoadBitmapFromBytes(bytes);
                    if (bitmap != null)
                    {
                        // Evict oldest entries if cache is full
                        EvictIfNeeded();
                        _cache[streamUrl] = new CachedThumbnail(bitmap);
                        Logger.Debug($"[Thumbnail] Cached snapshot ({bytes.Length} bytes) for {streamUrl}");
                    }
                    return bitmap;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"[Thumbnail] Snapshot fetch failed: {ex.Message} for {streamUrl}");
                return null;
            }
            finally
            {
                _pendingRequests.TryRemove(streamUrl, out _);
            }
        }

        private static BitmapImage? LoadBitmapFromBytes(byte[] bytes)
        {
            try
            {
                using var ms = new System.IO.MemoryStream(bytes);
                // Decode JPEG using BitmapDecoder (more reliable than BitmapImage for network streams)
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource source = decoder.Frames[0];

                // Downscale to 160px width to save memory
                if (source.PixelWidth > 160)
                {
                    double scale = 160.0 / source.PixelWidth;
                    source = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
                }

                // Convert BitmapSource to BitmapImage (required for BitmapImage-specific features)
                // Write to a MemoryStream as PNG to create a clean bitmap that BitmapImage can decode reliably
                using var outMs = new System.IO.MemoryStream();
                var encoder = new PngBitmapEncoder(); // PNG avoids JPEG recompression artifacts
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(outMs);
                outMs.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = outMs;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Debug($"[Thumbnail] Failed to decode JPEG: {ex.Message}");
                return null;
            }
        }

        private void EvictIfNeeded()
        {
            // Remove expired entries first, then oldest if still over limit
            var expiredKeys = _cache.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
            foreach (var key in expiredKeys)
                _cache.TryRemove(key, out _);

            while (_cache.Count > MAX_CACHE_SIZE)
            {
                var oldest = _cache.OrderBy(kvp => kvp.Value.CachedAt).FirstOrDefault();
                if (!string.IsNullOrEmpty(oldest.Key))
                    _cache.TryRemove(oldest.Key, out _);
                else
                    break;
            }
        }

        /// <summary>
        /// Preloads preview thumbnails for the given URLs in the background.
        /// </summary>
        public void Preload(IEnumerable<string> streamUrls)
        {
            foreach (var url in streamUrls.Take(20))
            {
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (_cache.ContainsKey(url)) continue;
                if (_pendingRequests.ContainsKey(url)) continue;

                // Fire-and-forget background preload
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await GetPreviewAsync(url).ConfigureAwait(false);
                    }
                    catch { }
                });
            }
        }

        /// <summary>
        /// Clears the thumbnail cache.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
        }

        private class CachedThumbnail
        {
            public BitmapImage Image { get; }
            public DateTime CachedAt { get; }

            public CachedThumbnail(BitmapImage image)
            {
                Image = image;
                CachedAt = DateTime.UtcNow;
            }

            public bool IsExpired => (DateTime.UtcNow - CachedAt).TotalSeconds > CACHE_TTL_SECONDS;
        }
    }
}
