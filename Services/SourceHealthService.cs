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
    /// Background source health service that probes HTTP/HTTPS stream sources and updates
    /// their health status (IsReachable, LatencyMs, FailureCount, LastChecked).
    ///
    /// Design: there is NO full-list periodic scan. Probing happens only for
    ///   1. the channel that just started playing (auto, gated by EnableSourceHealthScan),
    ///   2. explicit per-channel re-checks from the right-click menu,
    ///   3. a single-source probe of the tag when its context menu opens (auto, gated).
    /// Successful playback marks the current source healthy with zero network traffic.
    ///
    /// Only HTTP/HTTPS sources are probed. Results are written directly onto Source model
    /// objects, which implement INotifyPropertyChanged for UI auto-refresh.
    /// </summary>
    public class SourceHealthService
    {
        public static SourceHealthService Instance { get; } = new SourceHealthService();

        // Min gap between two automatic probes of the same channel (avoids re-probing on
        // quick channel re-select / auto-degrade retries).
        private const int ChannelProbeMinIntervalMs = 20_000;
        // Min gap for the zero-traffic "playback started" healthy mark.
        private const int MarkHealthyMinIntervalMs = 10_000;
        // Delay before a channel probe actually starts so it never contends with mpv start.
        private const int ProbeStartDelayMs = 500;
        // Throttle NotifySourceHealthChanged to at most once per second per channel
        private const int NotifyThrottleMs = 1000;

        // Throttle NotifySourceHealthChanged per channel
        private readonly Dictionary<string, DateTime> _lastNotifyTime = new();
        // Use Dictionary so concurrent probes for the same URL can wait for the first to finish
        private readonly Dictionary<string, TaskCompletionSource<ProbeResult>> _pendingProbes =
            new Dictionary<string, TaskCompletionSource<ProbeResult>>(StringComparer.OrdinalIgnoreCase);
        // Bookkeeping guarded by _lock
        private readonly object _lock = new object();
        private readonly Dictionary<string, DateTime> _lastChannelProbe = new(); // auto probe throttle
        private readonly Dictionary<string, Task> _channelProbeTasks = new();     // in-flight channel probes
        private readonly Dictionary<string, DateTime> _lastMarkHealthy = new();   // playback mark throttle
        // Serializes ProbeSourcesAsync runs so the global concurrent probe count stays bounded
        private readonly SemaphoreSlim _sem = new SemaphoreSlim(1, 1);

        private SourceHealthService() { }

        private static bool AutoScanEnabled => AppSettings.Current.EnableSourceHealthScan;

        private static string? GetChannelKey(Channel? ch)
        {
            if (ch == null) return null;
            if (!string.IsNullOrWhiteSpace(ch.Id)) return ch.Id;
            if (ch.Tag is Source t && !string.IsNullOrWhiteSpace(t.Url)) return SanitizeUrl(t.Url) ?? t.Url;
            if (ch.Sources != null && ch.Sources.Count > 0 && !string.IsNullOrWhiteSpace(ch.Sources[0].Url))
                return SanitizeUrl(ch.Sources[0].Url) ?? ch.Sources[0].Url;
            return null;
        }

        /// <summary>Stops housekeeping. Kept for lifecycle compatibility (window close / disabling the scan).</summary>
        public void Stop()
        {
            lock (_lock)
            {
                _lastChannelProbe.Clear();
                _channelProbeTasks.Clear();
                _lastMarkHealthy.Clear();
            }
            Logger.Debug("[Source] Health service stopped");
        }

        /// <summary>Calls ch.NotifySourceHealthChanged() at most once per NotifyThrottleMs to prevent log flooding.</summary>
        private void ThrottledNotify(Channel ch)
        {
            var id = GetChannelKey(ch);
            if (id == null) { try { ch.NotifySourceHealthChanged(); } catch { } return; }
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
        /// Zero-traffic health confirmation: the channel's current source just played
        /// successfully, so mark it reachable/healthy without any HTTP probe. Works even
        /// when automatic scanning is disabled, keeping the source indicator and
        /// auto-degrade logic functional.
        /// </summary>
        public void MarkPlaybackHealthy(Channel channel)
        {
            if (channel == null) return;
            var source = channel.Tag ?? channel.Sources?.FirstOrDefault();
            if (source == null) return;

            var id = GetChannelKey(channel);
            lock (_lock)
            {
                if (id != null && _lastMarkHealthy.TryGetValue(id, out var last) &&
                    (DateTime.Now - last).TotalMilliseconds < MarkHealthyMinIntervalMs) return;
                if (id != null) _lastMarkHealthy[id] = DateTime.Now;
            }

            WireChannel(channel);
            source.IsReachable = true;
            source.FailureCount = 0;
            source.LastChecked = DateTime.UtcNow;
            ThrottledNotify(channel);
            Logger.Info($"[Source] Playback confirmed healthy (no probe): {channel.Name} -> {source.Url}");
        }

        /// <summary>
        /// Automatic, channel-scoped probe triggered when a channel starts playing.
        /// Probes ONLY this channel's sources (no full-list scan) with the configured
        /// concurrency (default 5). Gated by EnableSourceHealthScan and throttled per channel.
        /// </summary>
        public void ProbeChannelSourcesAsync(Channel channel)
        {
            if (channel == null) return;
            if (!AutoScanEnabled) return;

            var id = GetChannelKey(channel);
            lock (_lock)
            {
                if (id != null && _lastChannelProbe.TryGetValue(id, out var last) &&
                    (DateTime.Now - last).TotalMilliseconds < ChannelProbeMinIntervalMs) return;
                if (id != null) _lastChannelProbe[id] = DateTime.Now;
            }

            WireChannel(channel);
            var task = ProbeChannelCoreAsync(channel, delayBeforeProbe: true);
            if (id != null)
            {
                lock (_lock) { _channelProbeTasks[id] = task; }
            }
        }

        /// <summary>
        /// Manual full re-check for a single channel (right-click menu "检测源健康").
        /// Always allowed, even when automatic scanning is disabled.
        /// </summary>
        public void StartImmediateRecheck(Channel channel)
        {
            if (channel?.Sources == null) return;
            WireChannel(channel);
            _ = ProbeChannelCoreAsync(channel, delayBeforeProbe: false);
        }

        /// <summary>True while a channel-scoped probe is still running (used by auto-degrade retry).</summary>
        public bool IsProbePending(Channel channel)
        {
            var id = GetChannelKey(channel);
            if (id == null) return false;
            lock (_lock)
            {
                return _channelProbeTasks.TryGetValue(id, out var t) && !t.IsCompleted;
            }
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
                .Cast<string>()
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
        /// Probes multiple sources concurrently, respecting the configured concurrency
        /// limit (1~100, default 5). Runs are serialized so concurrent callers cannot
        /// exceed the limit in total.
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
                var maxConcurrent = Math.Max(1, Math.Min(100, AppSettings.Current.SourceHealthMaxConcurrent));
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

        private async Task ProbeChannelCoreAsync(Channel channel, bool delayBeforeProbe)
        {
            try
            {
                if (delayBeforeProbe)
                {
                    // Give an in-progress mpv stream start a head start (ONU/modem line budget).
                    await Task.Delay(ProbeStartDelayMs).ConfigureAwait(false);
                }
                if (!AutoScanEnabled) return; // user disabled the scan while we waited
                if (channel?.Sources == null || channel.Sources.Count == 0) return;

                // Deduplicate by sanitized URL; always include the current Tag source.
                var list = new List<Source>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in channel.Sources)
                {
                    if (s == null || string.IsNullOrWhiteSpace(s.Url)) continue;
                    var key = SanitizeUrl(s.Url) ?? s.Url;
                    if (seen.Add(key)) list.Add(s);
                }
                if (channel.Tag != null && !string.IsNullOrWhiteSpace(channel.Tag.Url))
                {
                    var key = SanitizeUrl(channel.Tag.Url) ?? channel.Tag.Url;
                    if (seen.Add(key)) list.Add(channel.Tag);
                }
                if (list.Count == 0) return;

                await ProbeSourcesAsync(list).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                var id = GetChannelKey(channel);
                if (id != null)
                {
                    lock (_lock) { _channelProbeTasks.Remove(id); }
                }
            }
        }

        /// <summary>Wires Source→Channel callback so ellipse bindings refresh after probes.</summary>
        private void WireChannel(Channel ch)
        {
            if (ch?.Sources == null) return;
            foreach (var s in ch.Sources)
            {
                if (s != null) s.OnHealthChanged = () => ThrottledNotify(ch);
            }
            if (ch.Tag != null) ch.Tag.OnHealthChanged = () => ThrottledNotify(ch);
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
