using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LibmpvIptvClient
{
    public class AsyncImageConverter : IValueConverter
    {
        // Thread-safe cache
        private static readonly ConcurrentDictionary<string, BitmapImage> _cache = new ConcurrentDictionary<string, BitmapImage>();
        // Urls that already failed: without this every list scroll retried them and flooded the network.
        private static readonly ConcurrentDictionary<string, byte> _failed = new ConcurrentDictionary<string, byte>();
        private static readonly BitmapImage _defaultImage;
        private static readonly HttpClient _http;

        static AsyncImageConverter()
        {
            // Initialize default image from resources
            try 
            {
                _defaultImage = new BitmapImage(new Uri("pack://application:,,,/srcbox.png"));
                _defaultImage.Freeze();
            }
            catch 
            {
                // Fallback if resource not found (should not happen)
                _defaultImage = new BitmapImage();
            }

            // Initialize HttpClient with browser-like headers
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            _http = new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(15); // Increased timeout for slow logo servers
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string url && !string.IsNullOrWhiteSpace(url))
            {
                // Check cache first
                if (_cache.TryGetValue(url, out var cachedImg))
                {
                    return cachedImg;
                }

                if (_failed.ContainsKey(url))
                {
                    return _defaultImage;
                }

                try
                {
                    // URL Preprocessing
                    string processedUrl = url.Trim();
                    if (processedUrl.StartsWith("//"))
                        processedUrl = "http:" + processedUrl;
                    else if (!processedUrl.Contains("://") && !Path.IsPathRooted(processedUrl))
                    {
                        // Assume http for domains like "example.com/logo.png"
                        if (!File.Exists(Path.GetFullPath(processedUrl)) && processedUrl.Contains("."))
                             processedUrl = "http://" + processedUrl;
                    }

                    // Download data (Blocking is safe here because Binding.IsAsync=True runs this on a background thread)
                    byte[]? data = null;

                    if (processedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        // Use .Result to block synchronously on the background thread
                        data = _http.GetByteArrayAsync(processedUrl).Result;
                    }
                    else if (File.Exists(processedUrl))
                    {
                        data = File.ReadAllBytes(processedUrl);
                    }
                    
                    if (data != null && data.Length > 0)
                    {
                        using var ms = new MemoryStream(data);
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad; // Load immediately to close stream
                        img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
                        img.StreamSource = ms;
                        img.DecodePixelWidth = 160; // Downscale to save memory
                        img.EndInit();
                        img.Freeze(); // Freeze for cross-thread access

                        _cache[url] = img; // Cache successful load
                        return img;
                    }

                    _failed[url] = 0;
                }
                catch (Exception ex)
                {
                    _failed[url] = 0;
                    LibmpvIptvClient.Diagnostics.Logger.Warn($"[Logo] 加载失败: {url} -> {ex.Message}");
                }
            }
            
            return _defaultImage;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
