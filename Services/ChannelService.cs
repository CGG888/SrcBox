using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    public class ChannelService
    {
        HttpClient _http => HttpClientService.Instance.Client;
        readonly M3UParser _m3u;
        readonly IptvCheckerClient _checker;
        public ChannelService(M3UParser m3u, IptvCheckerClient checker)
        {
            _m3u = m3u;
            _checker = checker;
        }
        public async Task<List<Channel>> LoadChannelsAsync(string? m3uUrl, bool m3uPriority)
        {
            var fromM3u = new List<Channel>();
            if (!string.IsNullOrWhiteSpace(m3uUrl))
            {
                try
                {
                    if (Uri.TryCreate(m3uUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
                    {
                        bool cacheEnabled = AppSettings.Current?.EnableM3uCache ?? true;

                        if (cacheEnabled)
                        {
                            var (cached, fromCache) = await M3UCacheService.Instance.LoadFromCacheAsync(m3uUrl);
                            if (cached != null && cached.Count > 0)
                            {
                                fromM3u = cached;
                            }
                            else
                            {
                                fromM3u = await _m3u.ParseFromUrlAsync(m3uUrl);
                                if (fromM3u.Count > 0)
                                {
                                    await M3UCacheService.Instance.SaveToCacheAsync(m3uUrl, fromM3u);
                                }
                            }
                        }
                        else
                        {
                            fromM3u = await _m3u.ParseFromUrlAsync(m3uUrl);
                        }
                    }
                    else if (System.IO.File.Exists(m3uUrl))
                    {
                        fromM3u = await _m3u.ParseFromPathAsync(m3uUrl);
                    }
                }
                catch { }
            }
            LibmpvIptvClient.Diagnostics.Logger.Log("M3U频道数量 " + fromM3u.Count);
            if (fromM3u.Count > 0)
            {
                try
                {
                    _ = LibmpvIptvClient.Services.LogoCacheService.Instance.WarmupAndSwapAsync(fromM3u);
                }
                catch { }
                return fromM3u;
            }
            var fromChecker = new List<Channel>();
            try
            {
                if (_checker != null && _checker.IsConfigured)
                {
                    fromChecker = await _checker.LoadChannelsAsync();
                }
            }
            catch { }
            LibmpvIptvClient.Diagnostics.Logger.Log("后端频道数量 " + fromChecker.Count);
            var merged = MergeChannels(fromM3u, fromChecker, m3uPriority);
            
            // Apply custom logo URL pattern if configured
            var customLogoPattern = AppSettings.Current.CustomLogoUrl;
            if (!string.IsNullOrWhiteSpace(customLogoPattern) && customLogoPattern.Contains("{name}", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var ch in merged)
                {
                    // Only apply if logo is missing, OR maybe user wants to override? 
                    // Usually custom repo is for missing logos.
                    // But if user explicitly sets it, they might want to use it.
                    // Let's stick to "if missing" for now, or "always"? 
                    // The user said "添加匹配规则...按这样的填写地址来处理". 
                    // Let's assume fallback for missing logos first.
                    if (string.IsNullOrWhiteSpace(ch.Logo) && !string.IsNullOrWhiteSpace(ch.Name))
                    {
                        ch.Logo = customLogoPattern.Replace("{name}", ch.Name, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            try
            {
                _ = LibmpvIptvClient.Services.LogoCacheService.Instance.WarmupAndSwapAsync(merged);
            }
            catch { }
            return merged;
        }
        List<Channel> MergeChannels(List<Channel> a, List<Channel> b, bool aPriority)
        {
            var map = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void mergeSources(Channel dst, Channel src)
            {
                if (src.Tag != null && dst.Tag == null) dst.Tag = src.Tag;
                if (src.Sources != null)
                {
                    foreach (var s in src.Sources)
                    {
                        if (!dst.Sources.Exists(x => string.Equals(x.Url, s.Url, StringComparison.OrdinalIgnoreCase)))
                            dst.Sources.Add(s);
                    }
                }
            }
            void add(Channel ch)
            {
                if (!map.TryGetValue(ch.Id, out var exist))
                {
                    var nk = (ch.Name + "|" + ch.Group).ToLowerInvariant();
                    if (nameMap.TryGetValue(nk, out var sameId) && map.TryGetValue(sameId, out var same))
                    {
                        var winner = aPriority ? ch : same;
                        var loser = aPriority ? same : ch;
                        winner.Name = string.IsNullOrWhiteSpace(winner.Name) ? loser.Name : winner.Name;
                        winner.Group = string.IsNullOrWhiteSpace(winner.Group) ? loser.Group : winner.Group;
                        winner.Logo = string.IsNullOrWhiteSpace(winner.Logo) ? loser.Logo : winner.Logo;
                        mergeSources(winner, loser);
                        map[winner.Id] = winner;
                        nameMap[nk] = winner.Id;
                    }
                    else
                    {
                        map[ch.Id] = ch;
                        nameMap[(ch.Name + "|" + ch.Group).ToLowerInvariant()] = ch.Id;
                    }
                }
                else
                {
                    var winner = aPriority ? ch : exist;
                    var loser = aPriority ? exist : ch;
                    winner.Name = string.IsNullOrWhiteSpace(winner.Name) ? loser.Name : winner.Name;
                    winner.Group = string.IsNullOrWhiteSpace(winner.Group) ? loser.Group : winner.Group;
                    winner.Logo = string.IsNullOrWhiteSpace(winner.Logo) ? loser.Logo : winner.Logo;
                    mergeSources(winner, loser);
                    map[winner.Id] = winner;
                    nameMap[(winner.Name + "|" + winner.Group).ToLowerInvariant()] = winner.Id;
                }
            }
            if (aPriority)
            {
                foreach (var ch in a) add(ch);
                foreach (var ch in b) add(ch);
            }
            else
            {
                foreach (var ch in b) add(ch);
                foreach (var ch in a) add(ch);
            }
            return map.Values.ToList();
        }
    }
}
