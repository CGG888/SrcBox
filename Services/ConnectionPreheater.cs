using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    public static class ConnectionPreheater
    {
        static int _maxParallel = Math.Max(2, Environment.ProcessorCount / 2);

        public static void PreheatForChannels(IEnumerable<Channel> channels, int maxHosts = 30)
        {
            if (channels == null) return;
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ch in channels)
            {
                if (ch?.Sources == null) continue;
                foreach (var s in ch.Sources)
                {
                    var u = SanitizeUrl(s?.Url);
                    if (!string.IsNullOrWhiteSpace(u) && (u.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
                    {
                        urls.Add(u);
                        if (urls.Count >= maxHosts) break;
                    }
                }
                if (urls.Count >= maxHosts) break;
            }
            if (urls.Count == 0) return;
            Task.Run(() => PreheatAsync(urls));
        }

        public static void PreheatForUrls(IEnumerable<string> urls)
        {
            if (urls == null) return;
            var set = new HashSet<string>(urls.Where(u => !string.IsNullOrWhiteSpace(u) && u.StartsWith("http", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0) return;
            Task.Run(() => PreheatAsync(set));
        }

        static async Task PreheatAsync(HashSet<string> urls)
        {
            try
            {
                using var throttler = new SemaphoreSlim(_maxParallel, _maxParallel);
                var hc = HttpClientService.Instance.Client;
                var tasks = new List<Task>();
                foreach (var u in urls)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await throttler.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            var uri = new Uri(u);
                            // Preheat the actual URL path instead of just root "/" to warm up CDN/middleware for real stream path
                            using var req = new HttpRequestMessage(HttpMethod.Head, uri);
                            try
                            {
                                var res = await hc.SendAsync(req, cts.Token).ConfigureAwait(false);
                                Logger.Debug($"[Preheat] {uri} {(int)res.StatusCode}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Debug($"[Preheat] {uri} fail: {ex.Message}");
                            }
                        }
                        catch { }
                        finally
                        {
                            throttler.Release();
                        }
                    }));
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch { }
        }

        static string? SanitizeUrl(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim();
            var idx = s.IndexOf('$');
            if (idx > 0) s = s.Substring(0, idx).Trim();
            return s;
        }
    }
}
