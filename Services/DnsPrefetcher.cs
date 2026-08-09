using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    public static class DnsPrefetcher
    {
        static readonly ConcurrentDictionary<string, DateTime> _seen = new(StringComparer.OrdinalIgnoreCase);
        static readonly SemaphoreSlim _gate = new(1, 1);
        static int _maxParallel = Math.Max(2, Environment.ProcessorCount / 2);

        // Windows DNS query for SRV records (OPT-6)
        [DllImport("dnsapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint DnsQuery(string lpstrName, ushort wType, uint options, IntPtr pExtra, out IntPtr ppQueryResults, IntPtr pReserved);

        [DllImport("dnsapi.dll", SetLastError = true)]
        private static extern void DnsRecordListFree(IntPtr pRecordList, int freeType);

        private const ushort DNS_TYPE_SRV = 0x0021;
        private const uint DNS_QUERY_STANDARD = 0x00000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct DNS_SRV_DATA
        {
            public ushort nameTarget;   // offset to target name (relative to record start)
            public ushort priority;
            public ushort weight;
            public ushort port;
            public uint padding;        // for alignment
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DNS_RECORD_HEADER
        {
            public IntPtr pNext;
            public IntPtr pName;
            public ushort wType;
            public ushort wDataLength;
            public uint flags;
            public uint dwTtl;
            public uint dwReserved;
            // DNS_SRV_DATA follows at offset 24+
        }

        public static void PrefetchForChannels(IEnumerable<Channel> channels, int maxHosts = 40)
        {
            if (channels == null) return;
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ch in channels)
            {
                if (ch?.Sources == null) continue;
                foreach (var s in ch.Sources)
                {
                    var h = ExtractHost(s?.Url);
                    if (!string.IsNullOrEmpty(h)) hosts.Add(h);
                    if (hosts.Count >= maxHosts) break;
                }
                if (hosts.Count >= maxHosts) break;
            }
            if (hosts.Count == 0) return;
            Task.Run(() => PrefetchHostsAsync(hosts));
        }

        public static void PrefetchForUrls(IEnumerable<string> urls)
        {
            if (urls == null) return;
            var hosts = new HashSet<string>(urls.Select(ExtractHost).Where(h => !string.IsNullOrEmpty(h))!, StringComparer.OrdinalIgnoreCase);
            if (hosts.Count == 0) return;
            Task.Run(() => PrefetchHostsAsync(hosts));
        }

        static async Task PrefetchHostsAsync(HashSet<string> hosts)
        {
            try
            {
                var toResolve = hosts.Where(h => !_seen.ContainsKey(h)).ToArray();
                if (toResolve.Length == 0) return;
                using var throttler = new SemaphoreSlim(_maxParallel, _maxParallel);
                var tasks = new List<Task>();
                foreach (var h in toResolve)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await throttler.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            // Skip literals (IPs)
                            if (IPAddress.TryParse(h, out _)) { _seen[h] = DateTime.UtcNow; return; }

                            // Try SRV lookup first (OPT-6: for services like _rtp._udp.example.com)
                            var srvTargets = await ResolveSrvRecordsAsync(h).ConfigureAwait(false);

                            // Then resolve the main A/AAAA record
                            var addrs = await Dns.GetHostAddressesAsync(h).ConfigureAwait(false);
                            _seen[h] = DateTime.UtcNow;

                            var resolved = new List<string>();
                            if (addrs != null && addrs.Length > 0)
                                resolved.AddRange(addrs.Select(a => a.ToString()));
                            if (srvTargets.Count > 0)
                                resolved.AddRange(srvTargets);

                            if (resolved.Count > 0)
                                Logger.Debug($"[DNS] 预解析 {h} -> {string.Join(",", resolved.Take(3))}" +
                                    (srvTargets.Count > 0 ? $" (via SRV: {srvTargets.Count} targets)" : ""));
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"[DNS] 预解析失败 {h}: {ex.Message}");
                        }
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

        // Query SRV records for a domain (OPT-6)
        static async Task<List<string>> ResolveSrvRecordsAsync(string domain)
        {
            var targets = new List<string>();
            try
            {
                // Try common RTP service discovery patterns
                var srvQueries = new[]
                {
                    $"_rtp._udp.{domain}",
                    $"_rtp._tcp.{domain}",
                    $"_mpegts._udp.{domain}"
                };

                foreach (var srvName in srvQueries)
                {
                    try
                    {
                        var result = await Task.Run(() =>
                        {
                            IntPtr ppQueryResults = IntPtr.Zero;
                            uint status = DnsQuery(srvName, DNS_TYPE_SRV, DNS_QUERY_STANDARD, IntPtr.Zero, out ppQueryResults, IntPtr.Zero);
                            if (status != 0 || ppQueryResults == IntPtr.Zero) return IntPtr.Zero;
                            return ppQueryResults;
                        }).ConfigureAwait(false);

                        if (result == IntPtr.Zero) continue;

                        try
                        {
                            var current = result;
                            while (current != IntPtr.Zero)
                            {
                                var header = Marshal.PtrToStructure<DNS_RECORD_HEADER>(current);
                                if (header.wType == DNS_TYPE_SRV && header.wDataLength >= 12)
                                {
                                    // Read target name offset from SRV data (offset 0 in the data section)
                                    var dataOffset = current + Marshal.SizeOf<DNS_RECORD_HEADER>();
                                    var targetOffset = Marshal.ReadInt16(dataOffset);
                                    var targetPtr = current + targetOffset;
                                    var targetName = Marshal.PtrToStringUni(targetPtr);
                                    if (!string.IsNullOrEmpty(targetName) && !targets.Contains(targetName))
                                        targets.Add(targetName);
                                }
                                current = header.pNext;
                            }
                        }
                        finally
                        {
                            DnsRecordListFree(result, 0);
                        }
                    }
                    catch { /* Per-query failure is non-fatal */ }
                }
            }
            catch { /* SRV lookup failure is non-fatal, fallback to A/AAAA */ }

            return targets;
        }

        static string? ExtractHost(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                if (url.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("rtp://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("srt://", StringComparison.OrdinalIgnoreCase)) return null;
                if (Uri.TryCreate(url, UriKind.Absolute, out var u))
                {
                    return u.Host;
                }
            }
            catch { }
            return null;
        }
    }
}
