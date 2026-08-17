using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Services
{
    public class EpgService
    {
        private HttpClient _http => HttpClientService.Instance.Client;
        private Dictionary<string, List<EpgProgram>> _programs = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _channelNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Name -> TvgId
        private Dictionary<string, string> _tvgNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // CleanTvgName -> TvgId (新增)
        private ConcurrentDictionary<string, string?> _smartMatchCache = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase); // ChannelName -> TvgId (Cache)
        private readonly Dictionary<string, Dictionary<DateTime, List<EpgProgram>>> _programsByHour = new Dictionary<string, Dictionary<DateTime, List<EpgProgram>>>(StringComparer.OrdinalIgnoreCase); // tvgId -> hourBucket -> programs in that hour

        public EpgService()
        {
        }

        public async Task LoadEpgAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            
            // Clear cache on reload
            _smartMatchCache.Clear();

            try 
            {
                byte[] data;
                // 使用 URL 的 Hash 作为缓存文件名的一部分
                var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
                var cachePath = Path.Combine(Path.GetTempPath(), $"iptv_epg_{hash}.dat");
                
                // 简单的缓存策略：文件存在且小于 12 小时则直接使用
                if (File.Exists(cachePath) && (DateTime.Now - File.GetLastWriteTime(cachePath)).TotalHours < 12)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Info("正在从缓存加载节目单...");
                    data = await File.ReadAllBytesAsync(cachePath);
                }
                else
                {
                    LibmpvIptvClient.Diagnostics.Logger.Info("正在下载节目单...");
                    try
                    {
                        data = await _http.GetByteArrayAsyncWithRetry(url);
                    }
                    catch (Exception ex)
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Error($"EPG Download Failed: {ex.Message}");
                        return;
                    }
                    // 异步写入缓存
                    _ = File.WriteAllBytesAsync(cachePath, data);
                }

                LibmpvIptvClient.Diagnostics.Logger.Info($"节目单数据大小: {data.Length} bytes");

                using var ms = new MemoryStream(data);
                Stream stream = ms;
                
                // Check for GZIP
                if (data.Length > 2 && data[0] == 0x1F && data[1] == 0x8B)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Info("节目单已解压");
                    stream = new GZipStream(ms, CompressionMode.Decompress);
                }
                else
                {
                    LibmpvIptvClient.Diagnostics.Logger.Info("节目单格式: XML");
                }

                await Task.Run(() => ParseXml(stream));
                LibmpvIptvClient.Diagnostics.Logger.Info($"节目单加载完成，包含 {_programs.Count} 个频道");
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Error($"EPG Load Error: {ex.Message}");
            }
        }

        private void ParseXml(Stream stream)
        {
            var newPrograms = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
            var newMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var newTvgNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 创建命名空间管理器以处理带前缀的节点
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
                using var reader = XmlReader.Create(stream, settings);

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        // 忽略命名空间，只匹配 LocalName
                        if (reader.LocalName == "channel")
                        {
                            var id = reader.GetAttribute("id");
                            if (!string.IsNullOrEmpty(id))
                            {
                                // Read display-name
                                using var inner = reader.ReadSubtree();
                                while (inner.Read())
                                {
                                    if (inner.NodeType == XmlNodeType.Element && inner.LocalName == "display-name")
                                    {
                                        var name = inner.ReadElementContentAsString();
                                        if (!string.IsNullOrEmpty(name))
                                        {
                                            newMap[name] = id;
                                            // 同时构建清洗后的 tvg-name 映射
                                            var cleanedName = EpgMatcher.CleanName(name);
                                            if (!string.IsNullOrEmpty(cleanedName))
                                            {
                                                newTvgNameMap[cleanedName] = id;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else if (reader.LocalName == "programme")
                        {
                            var channelId = reader.GetAttribute("channel");
                            if (string.IsNullOrEmpty(channelId)) continue;

                            var prog = new EpgProgram();
                            if (TryParseTime(reader.GetAttribute("start"), out var start)) prog.Start = start;
                            if (TryParseTime(reader.GetAttribute("stop"), out var end)) prog.End = end;

                            // Read inner elements
                            using var inner = reader.ReadSubtree();
                            while (inner.Read())
                            {
                                if (inner.NodeType == XmlNodeType.Element)
                                {
                                    if (inner.LocalName == "title")
                                    {
                                        prog.Title = inner.ReadElementContentAsString();
                                    }
                                    else if (inner.LocalName == "desc")
                                    {
                                        prog.Description = inner.ReadElementContentAsString();
                                    }
                                }
                            }

                            if (!newPrograms.ContainsKey(channelId))
                            {
                                newPrograms[channelId] = new List<EpgProgram>();
                            }
                            newPrograms[channelId].Add(prog);
                        }
                    }
                }
            }
            catch (Exception ex) 
            {
                System.Diagnostics.Debug.WriteLine("XML Parse Error: " + ex.Message);
            }

            // Sort
            foreach (var kv in newPrograms)
            {
                kv.Value.Sort((a, b) => a.Start.CompareTo(b.Start));
            }

            _programs = newPrograms;
            _channelNameMap = newMap;
            _tvgNameMap = newTvgNameMap;
        }

        private bool TryParseTime(string? s, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrEmpty(s)) return false;

            // Clean up string
            s = s.Trim();
            
            // Format: yyyyMMddHHmmss zzz or yyyyMMddHHmmss
            // Handle space before timezone
            // Example: 20240226120000 +0800
            
            if (s.Length >= 14)
            {
                var core = s.Substring(0, 14);
                if (DateTime.TryParseExact(core, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                {
                    // Check for timezone suffix
                    if (s.Length > 14)
                    {
                        var suffix = s.Substring(14).Trim();
                        // +0800, +08:00, +08
                        if (suffix.StartsWith("+") || suffix.StartsWith("-"))
                        {
                            try 
                            {
                                // Parse offset manually to be robust
                                var sign = suffix.StartsWith("+") ? 1 : -1;
                                var valStr = suffix.Substring(1).Replace(":", "");
                                if (valStr.Length >= 2 && int.TryParse(valStr.Substring(0, 2), out var hh))
                                {
                                    int mm = 0;
                                    if (valStr.Length >= 4) int.TryParse(valStr.Substring(2, 2), out mm);
                                    var offset = new TimeSpan(hh, mm, 0);
                                    var dto = new DateTimeOffset(local, sign * offset);
                                    dt = dto.LocalDateTime;
                                    return true;
                                }
                            }
                            catch { }
                        }
                    }
                    
                    // Fallback to local time if no timezone or parse failed
                    dt = local;
                    return true;
                }
            }
            return false;
        }

        public List<EpgProgram> GetPrograms(string tvgId, string? tvgName = null, string? channelName = null)
        {
            // 1. tvg-id 精确匹配
            if (!string.IsNullOrEmpty(tvgId) && _programs.TryGetValue(tvgId, out var list)) return list;

            // 2. tvg-id 清洗后再匹配（去除空格、连字符、转小写）
            if (!string.IsNullOrEmpty(tvgId))
            {
                var cleanedId = CleanTvgId(tvgId);
                if (cleanedId != tvgId && _programs.TryGetValue(cleanedId, out list)) return list;
            }

            // 3. tvg-name 清洗后匹配（新增）
            if (!string.IsNullOrEmpty(tvgName))
            {
                var cleanedTvgName = EpgMatcher.CleanName(tvgName);
                if (_tvgNameMap.TryGetValue(cleanedTvgName, out var id))
                {
                    if (_programs.TryGetValue(id, out list)) return list;
                }
            }

            // 4. channelName 清洗后匹配（现有逻辑）
            if (!string.IsNullOrEmpty(channelName) && _channelNameMap.TryGetValue(channelName, out var nameId))
            {
                if (_programs.TryGetValue(nameId, out list)) return list;
            }

            // 5. 智能模糊匹配（兜底）
            if (LibmpvIptvClient.AppSettings.Current.Epg.EnableSmartMatch && !string.IsNullOrEmpty(channelName))
            {
                string? smartId = null;
                bool foundInCache = false;

                // Try get from concurrent cache (lock-free)
                _smartMatchCache.TryGetValue(channelName, out smartId);
                foundInCache = smartId != null || _smartMatchCache.ContainsKey(channelName);

                if (foundInCache)
                {
                    if (smartId != null && _programs.TryGetValue(smartId, out var listCache)) return listCache;
                }
                else
                {
                    // 先获取所有已知的EPG频道名称
                    var allEpgNames = _channelNameMap.Keys;
                    var matchedName = EpgMatcher.Match(channelName, allEpgNames);

                    if (matchedName != null && _channelNameMap.TryGetValue(matchedName, out var idFromMatch))
                    {
                        smartId = idFromMatch;
                        _smartMatchCache[channelName] = smartId; // Thread-safe AddOrUpdate
                        if (_programs.TryGetValue(smartId, out var list3)) return list3;
                    }
                    else
                    {
                        // Cache failed result to avoid re-calculation
                        _smartMatchCache[channelName] = null;
                    }
                }
            }

            return new List<EpgProgram>();
        }

        private string CleanTvgId(string tvgId)
        {
            // tvg-id 清洗：去除空格、连字符、下划线，转小写
            return tvgId.Trim().ToLowerInvariant()
                .Replace(" ", "").Replace("-", "").Replace("_", "");
        }

        public EpgProgram? GetCurrentProgram(string tvgId, string? tvgName = null, string? channelName = null)
        {
            var list = GetPrograms(tvgId, tvgName, channelName);
            var now = DateTime.Now;
            // 优化：节目表可能跨越日期，或者节目时间存在时区偏差
            // 1. 直接查找包含当前时间的节目
            var current = list.FirstOrDefault(p => now >= p.Start && now < p.End);
            if (current != null) return current;

            // 2. 如果当前时间刚好在节目间隙（或者误差几分钟），尝试找最近的一个（例如最近 15 分钟内结束的，或者马上开始的）
            // 这里我们采取一个宽松策略：如果没找到正在播放的，但有一个节目结束时间就在刚才（例如 5 分钟内），可能因为时钟误差，仍然显示它
            var recentPast = list.LastOrDefault(p => p.End <= now && (now - p.End).TotalMinutes < 15);
            if (recentPast != null) return recentPast;
            
            // 3. 或者找马上要开始的？（暂时不处理，避免显示未开始的节目造成误解）

            return null;
        }
        public EpgProgram? GetProgramAt(string tvgId, DateTime timeLocal, string? channelName = null)
        {
            if (!ValidateTimeRange(timeLocal)) return null;
            var list = GetPrograms(tvgId, null, channelName);
            var hit = list.FirstOrDefault(p => timeLocal >= p.Start && timeLocal < p.End);
            if (hit != null) return hit;
            var recent = list.LastOrDefault(p => p.End <= timeLocal && (timeLocal - p.End).TotalMinutes < 15);
            if (recent != null) return recent;
            return null;
        }

        bool ValidateTimeRange(DateTime local)
        {
            try
            {
                if (local.Year < 1980 || local.Year > 2037) return false;
                return true;
            }
            catch { return false; }
        }

        public List<EpgProgram> GetProgramsByHour(string tvgId, DateTime hourLocal, string? channelName = null)
        {
            hourLocal = new DateTime(hourLocal.Year, hourLocal.Month, hourLocal.Day, hourLocal.Hour, 0, 0, hourLocal.Kind);
            if (!_programsByHour.TryGetValue(tvgId ?? "", out var hours))
            {
                hours = new Dictionary<DateTime, List<EpgProgram>>();
                _programsByHour[tvgId ?? ""] = hours;
            }
            if (hours.TryGetValue(hourLocal, out var cached)) return cached;
            var list = GetPrograms(tvgId, channelName);
            var seg = list.Where(p => !(p.End <= hourLocal || p.Start >= hourLocal.AddHours(1))).OrderBy(p => p.Start).ToList();
            hours[hourLocal] = seg;
            return seg;
        }

        public void SeedPrograms(string tvgId, List<EpgProgram> programs, string? channelName = null)
        {
            _programs[tvgId] = programs.OrderBy(p => p.Start).ToList();
            if (!string.IsNullOrWhiteSpace(channelName)) _channelNameMap[channelName] = tvgId;
            _programsByHour.Remove(tvgId);
        }
    }
}
