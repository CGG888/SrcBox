using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Background service that periodically probes HTTP/HTTPS stream sources
    /// and updates their health status (IsReachable, LatencyMs, FailureCount, LastChecked).
    ///
    /// Only probes HTTP/HTTPS sources. Results are written directly onto Source model
    /// objects, which implement INotifyPropertyChanged for UI auto-refresh.
    /// </summary>
    public class SourceHealthService
    {
        public static SourceHealthService Instance { get; } = new SourceHealthService();

        private System.Threading.Timer? _timer;
        private bool _running;
        private bool _firstScan = true; // true = next scan probes all sources at once (no batching)
        private List<Channel>? _shellChannels;
        private readonly SemaphoreSlim _sem = new SemaphoreSlim(1, 1);
        // Throttle NotifySourceHealthChanged to at most once per second per channel
        private readonly Dictionary<string, DateTime> _lastNotifyTime = new();
        private const int NotifyThrottleMs = 1000;
        // Use Dictionary so concurrent probes for the same URL can wait for the first to finish
        private readonly Dictionary<string, TaskCompletionSource<ProbeResult>> _pendingProbes =
            new Dictionary<string, TaskCompletionSource<ProbeResult>>(StringComparer.OrdinalIgnoreCase);
        private readonly Random _rng = new Random();

        private SourceHealthService() { }

        /// <summary>Calls ch.NotifySourceHealthChanged() at most once per NotifyThrottleMs to prevent log flooding.</summary>
        private void ThrottledNotify(Channel ch)
        {
            var id = ch.Id;
            if (string.IsNullOrEmpty(id)) { try { ch.NotifySourceHealthChanged(); } catch { } return; }
            var now = DateTime.Now;
            lock (_lastNotifyTime)
            {
                if (_lastNotifyTime.TryGetValue(id, out var last) && (now - last).TotalMilliseconds < NotifyThrottleMs)
                    return;
                _lastNotifyTime[id] = now;
            }
            try { ch.NotifySourceHealthChanged(); } catch { }
        }

        /// <summary>
        /// Starts periodic background health scanning of all sources in the given channels.
        /// First scan is delayed by a random 0~2000ms to avoid all clients probing at once.
        /// Subsequent scans respect SourceHealthScanIntervalSec.
        /// </summary>
        public void Start(IEnumerable<Channel> channels)
        {
            if (_running) return;
            _running = true;
            _firstScan = true;
            _shellChannels = channels?.ToList() ?? new List<Channel>();

            var intervalMs = Math.Max(10_000, AppSettings.Current.SourceHealthScanIntervalSec * 1000);
            // Random initial delay 0~2s to stagger first probe across multiple clients
            var initialDelayMs = _rng.Next(0, 2000);
            _timer = new System.Threading.Timer(_ => _ = ScanAllAsync(_shellChannels), null, initialDelayMs, intervalMs);
            Logger.Debug($"[Source] Health service started, initial delay={initialDelayMs}ms, interval={AppSettings.Current.SourceHealthScanIntervalSec}s");
        }

        /// <summary>
        /// Stops the background health scanner.
        /// </summary>
        public void Stop()
        {
            _running = false;
            _timer?.Dispose();
            _timer = null;
            Logger.Debug("[Source] Health service stopped");
        }

        /// <summary>
        /// Triggers an immediate health re-check for all sources of the given channel.
        /// Called from the right-click context menu "重新检测源健康" action.
        /// </summary>
        public void StartImmediateRecheck(Channel channel)
        {
            if (channel?.Sources == null) return;
            _ = ProbeSourcesAsync(channel.Sources);
        }

        /// <summary>
        /// Immediately scans all sources from all channels in the given list.
        /// Call this after channels are loaded to trigger instant health detection.
        /// </summary>
        public void RefreshAll(IEnumerable<Channel>? channels)
        {
            if (channels == null)
            {
                Logger.Info($"[Source] RefreshAll called with NULL channels");
                return;
            }
            var list = channels.ToList();
            Logger.Info($"[Source] RefreshAll called _running={_running} channels={list.Count}");
            _firstScan = true;
            // Update _shellChannels so timer-based scans use the loaded channels
            _shellChannels = list;
            _ = ScanAllAsync(list, fromRefreshAll: true);
        }

        /// <summary>
        /// Probes a single source asynchronously and updates its health fields.
        /// Handles $$-separated multi-URL sources (e.g. "rtp://...$$http://...") by
        /// probing each URL and aggregating: source is reachable if ANY URL responds.
        /// </summary>
        public async Task ProbeSourceAsync(Source source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Url))
                return;

            // Split $$-separated multi-URL sources; probe each and aggregate results
            var urls = source.Url.Split(new[] { "$$" }, StringSplitOptions.RemoveEmptyEntries);
            if (urls.Length == 0) return;

            var httpUrls = urls
                .Select(u => SanitizeUrl(u.Trim()))
                .Where(u => !string.IsNullOrWhiteSpace(u) && u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (httpUrls.Count == 0) return;

            // Probe all HTTP URLs concurrently, track best result
            var timeoutSec = Math.Max(1, AppSettings.Current.SourceHealthProbeTimeoutSec);
            var probeTasks = httpUrls.Select(url => ProbeSingleUrlAsync(url, timeoutSec)).ToList();
            var results = await Task.WhenAll(probeTasks).ConfigureAwait(false);

            var anyReachable = results.Any(r => r.Reachable);
            var bestLatency = results.Where(r => r.Reachable).Select(r => r.LatencyMs).DefaultIfEmpty(0).Min();
            var maxFailCount = results.Select(r => r.FailCount).Max();

            // Remove pending entry for the primary URL so subsequent probes can run fresh
            var primaryUrl = httpUrls[0];
            RemovePendingProbe(primaryUrl);

            source.IsReachable = anyReachable;
            source.LatencyMs = bestLatency;
            source.LastChecked = DateTime.UtcNow;
            source.FailureCount = anyReachable ? 0 : maxFailCount;
            try { source.OnHealthChanged?.Invoke(); } catch (Exception ex) { Logger.Warn($"[Source] OnHealthChanged error: {ex.Message}"); }
        }

        private record ProbeResult(bool Reachable, int LatencyMs, int FailCount);

        private async Task<ProbeResult> ProbeSingleUrlAsync(string url, int timeoutSec)
        {
            // If another probe is already running for this URL, wait for it instead of racing
            TaskCompletionSource<ProbeResult>? pendingTcs = null;
            bool mine = false;
            lock (_pendingProbes)
            {
                if (_pendingProbes.TryGetValue(url, out var existing))
                {
                    pendingTcs = existing;
                }
                else
                {
                    pendingTcs = new TaskCompletionSource<ProbeResult>();
                    _pendingProbes[url] = pendingTcs;
                    mine = true;
                }
            }

            if (!mine && pendingTcs != null)
            {
                // Another probe for this URL is in progress — wait for it
                return await pendingTcs.Task.ConfigureAwait(false);
            }

            // We own this probe — run it and broadcast result to any waiters
            ProbeResult result = new ProbeResult(false, 0, 1);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
                var sw = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                var response = await HttpClientService.Instance.Client.SendAsync(request, cts.Token).ConfigureAwait(false);
                sw.Stop();

                result = response.IsSuccessStatusCode
                    ? new ProbeResult(true, (int)sw.ElapsedMilliseconds, 0)
                    : new ProbeResult(false, 0, 1);
            }
            catch (OperationCanceledException)
            {
                result = new ProbeResult(false, 0, 1);
            }
            catch
            {
                result = new ProbeResult(false, 0, 1);
            }
            finally
            {
                RemovePendingProbe(url);
                pendingTcs?.SetResult(result);
            }

            return result;
        }

        private void RemovePendingProbe(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            lock (_pendingProbes)
            {
                _pendingProbes.Remove(url!);
            }
        }

        /// <summary>
        /// Probes multiple sources concurrently, respecting the max concurrent limit.
        /// </summary>
        public async Task ProbeSourcesAsync(IEnumerable<Source> sources)
        {
            if (sources == null) return;

            var sourceList = sources.Where(s => s != null).ToList();
            if (sourceList.Count == 0) return;

            Logger.Info($"[Source] ProbeSourcesAsync starting {sourceList.Count} sources");

            await _sem.WaitAsync().ConfigureAwait(false);
            try
            {
                var maxConcurrent = Math.Max(2, Math.Min(16, AppSettings.Current.SourceHealthMaxConcurrent));
                using var throttler = new SemaphoreSlim(maxConcurrent, maxConcurrent);

                var tasks = sourceList.Select(async s =>
                {
                    await throttler.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await ProbeSourceAsync(s).ConfigureAwait(false);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            finally
            {
                _sem.Release();
            }
        }

        private async Task ScanAllAsync(IEnumerable<Channel> channels, bool fromRefreshAll = false)
        {
            // RefreshAll triggers immediate scan regardless of timer state
            if (!_running && !fromRefreshAll) return;

            Logger.Info($"[Source] ScanAllAsync running _running={_running} fromRefreshAll={fromRefreshAll}");

            try
            {
                var allSources = new List<(Channel ch, Source src)>();
                foreach (var ch in channels ?? Enumerable.Empty<Channel>())
                {
                    if (ch?.Sources == null) continue;
                    foreach (var s in ch.Sources)
                    {
                        // Wire Source → Channel callback so Ellipse binding refreshes
                        s.OnHealthChanged = () => ThrottledNotify(ch);
                        allSources.Add((ch, s));
                    }
                    // Always include the current Tag source even if its URL differs after sanitization
                    // (e.g. assembled via $$-separator or URL query param order differs)
                    if (ch.Tag != null && !allSources.Any(x => x.src.Id == ch.Tag.Id))
                    {
                        ch.Tag.OnHealthChanged = () => ThrottledNotify(ch);
                        allSources.Add((ch, ch.Tag));
                    }
                }

                // Deduplicate by URL to avoid redundant probes
                var uniqueSources = allSources
                    .Where(t => !string.IsNullOrWhiteSpace(t.src.Url))
                    .GroupBy(t => SanitizeUrl(t.src.Url) ?? "")
                    .Select(g => g.First().src)
                    .ToList();

                if (uniqueSources.Count == 0)
                {
                    Logger.Info($"[Source] ScanAllAsync no unique sources, allSources={allSources.Count}");
                    return;
                }

                Logger.Info($"[Source] ScanAllAsync uniqueSources={uniqueSources.Count} firstScan={_firstScan}");

                // Batch scanning: split into groups and probe each batch sequentially
                var batchSize = Math.Max(10, AppSettings.Current.SourceHealthBatchSize);
                var batchDelayMs = Math.Max(50, AppSettings.Current.SourceHealthBatchDelayMs);
                var batches = uniqueSources
                    .Select((src, idx) => new { src, idx })
                    .GroupBy(x => x.idx / batchSize)
                    .Select(g => g.Select(x => x.src).ToList())
                    .ToList();

                if (_firstScan)
                {
                    _firstScan = false;
                    await ProbeSourcesAsync(uniqueSources).ConfigureAwait(false);
                }
                else
                {
                    for (var i = 0; i < batches.Count; i++)
                    {
                        if (!_running) break;
                        var batch = batches[i];
                        await ProbeSourcesAsync(batch).ConfigureAwait(false);

                        if (i < batches.Count - 1)
                            await Task.Delay(batchDelayMs).ConfigureAwait(false);
                    }
                }
            }
            catch { }
        }

        private static string? SanitizeUrl(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim();
            var idx = s.IndexOf('$');
            if (idx > 0) s = s.Substring(0, idx).Trim();
            return s;
        }
    }
}
