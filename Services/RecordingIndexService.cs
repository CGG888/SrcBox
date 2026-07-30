using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace LibmpvIptvClient.Services
{
    public class RecordingEntry : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        public string Title { get; set; } = "";
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Source { get; set; } = "Local"; // Local / Remote
        public string PathOrUrl { get; set; } = "";
        public long SizeBytes { get; set; }
        public string SourceLabel { get; set; } = "";
        public string SizeLabel { get; set; } = "";
        public string FormatLabel { get; set; } = "";
        public string EndLabel { get; set; } = "";
        bool _isPlaying;
        public bool IsPlaying { get => _isPlaying; set { if (_isPlaying != value) { _isPlaying = value; OnPropertyChanged(); } } }
    }
    public class RecordingGroup
    {
        public string Channel { get; set; } = "";
        public List<RecordingEntry> Items { get; set; } = new List<RecordingEntry>();
        public int Count => Items?.Count ?? 0;
    }

    public class RecordingIndexService
    {
        public async Task<List<RecordingEntry>> GetForChannelAsync(string channelIdOrName, Func<DateTime?, (string? title, TimeSpan?)>? resolver = null)
        {
            var list = new List<RecordingEntry>();
            try
            {
                // Local scan: ./recordings/{channelIdOrName}
                var localRoot = System.AppDomain.CurrentDomain.BaseDirectory;
                var chanDir = Path.Combine(localRoot, "recordings", San(channelIdOrName));
                if (Directory.Exists(chanDir))
                {
                    IEnumerable<string> files;
                    try { files = Directory.EnumerateFiles(chanDir, "*.*", SearchOption.AllDirectories); }
                    catch { files = Directory.GetFiles(chanDir); }
                    foreach (var f in files)
                    {
                        var fi = new FileInfo(f);
                        var name = Path.GetFileNameWithoutExtension(f);
                        var ext = Path.GetExtension(f).Trim('.').ToUpperInvariant();
                        if (!IsVideoExt(ext)) continue;
                        DateTime? start = ParseStart(name);
                        var item = new RecordingEntry
                        {
                            Title = name,
                            StartTime = start,
                            Source = "Local",
                            PathOrUrl = f,
                            SizeBytes = fi.Length
                        };
                         try { item.SourceLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Access_Local", "本地存取"); } catch { item.SourceLabel = "本地存取"; }
                        var hasMetaTitle = TryLoadMeta(f, item);
                        if (resolver != null)
                        {
                            try
                            {
                                var tup = resolver(start);
                                if (!hasMetaTitle && !string.IsNullOrWhiteSpace(tup.Item1)) item.Title = tup.Item1!;
                            }
                            catch { }
                        }
                        FillLabels(item, ext);
                        list.Add(item);
                    }
                }
            }
            catch { }
            try
            {
                // Remote: WebDAV /recordings/{channel}/ depth=1 PROPFIND; fallback to /recordings/ depth=2
                var wd = LibmpvIptvClient.AppSettings.Current?.WebDav;
                if (wd != null && wd.Enabled && !string.IsNullOrWhiteSpace(wd.BaseUrl))
                {
                    var cli = new LibmpvIptvClient.Services.WebDavClient(wd);
                    var recBase = (wd.RecordingsPath ?? "/srcbox/recordings/").TrimEnd('/');
                    var chanSan = San(channelIdOrName);
                    // 1st try: /recordings/{channel}/ depth=1
                    var url1 = cli.Combine(recBase + "/" + chanSan + "/");
                    var props1 = await cli.ListWithPropsAsync(url1, 1);
                    var hrefs = new List<string>();
                    int remoteCount = 0, videoCount = 0, withMetaCount = 0;
                    if (props1 != null && props1.Count > 0)
                    {
                        hrefs = props1.Select(p => p.href).ToList();
                    }
                    // 不再做根目录 depth=2 的全量回退，以减少无谓扫描
                    foreach (var href in hrefs)
                    {
                        var finalHref = href;
                        try { if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) finalHref = cli.Combine(href); } catch { }
                        var baseName = "";
                        try
                        {
                            baseName = Path.GetFileName(new Uri(finalHref, UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new Uri(finalHref).AbsolutePath : finalHref);
                        }
                        catch
                        {
                            baseName = Path.GetFileName(finalHref);
                        }
                        var titleNoExt = Path.GetFileNameWithoutExtension(baseName);
                        var r = new RecordingEntry
                        {
                            Title = titleNoExt,
                            Source = "Remote",
                            PathOrUrl = finalHref,
                            SizeBytes = 0
                        };
                        var ext = Path.GetExtension(baseName).Trim('.').ToUpperInvariant();
                        // 尝试从文件名解析开始时间，便于与本地条目按文件名/时间一致聚合
                        try { r.StartTime = ParseStart(titleNoExt); } catch { }
                        if (!IsVideoExt(ext)) continue;
                        videoCount++;
                        try
                        {
                            var head = await cli.HeadAsync(finalHref); // 利用缓存避免重复请求
                            if (head.ok && head.size > 0) r.SizeBytes = head.size;
                            if (head.ok && head.lastmod.HasValue) r.EndTime = head.lastmod.Value.ToLocalTime();
                            if (r.SizeBytes > 0 || r.EndTime.HasValue) withMetaCount++;
                        }
                        catch { }
                             try { r.SourceLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Access_Remote", "网络存取"); } catch { r.SourceLabel = "网络存取"; }
                        if (resolver != null)
                        {
                            try
                            {
                                var tup = resolver(r.StartTime);
                                if (!string.IsNullOrWhiteSpace(tup.Item1)) r.Title = tup.Item1!;
                            }
                            catch { }
                        }
                        FillLabels(r, ext);
                        list.Add(r);
                    }
                    try
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Debug($"[Recordings.Remote] channel={channelIdOrName} total={hrefs.Count} video={videoCount} withMeta={withMetaCount}");
                    }
                    catch { }
                }
            }
            catch { }
            try
            {
                var merged = new List<RecordingEntry>();
                var grouped = list.GroupBy(e => KeyOf(e));
                foreach (var g in grouped)
                {
                    var local = g.FirstOrDefault(e => string.Equals(e.Source, "Local", StringComparison.OrdinalIgnoreCase));
                    var remote = g.FirstOrDefault(e => string.Equals(e.Source, "Remote", StringComparison.OrdinalIgnoreCase));
                    if (local != null && remote != null)
                    {
                        try { local.SourceLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Access_Mixed", "网络/本地"); } catch { local.SourceLabel = "网络/本地"; }
                        if (local.EndTime == null && remote.EndTime != null) local.EndTime = remote.EndTime;
                        if (local.SizeBytes == 0 && remote.SizeBytes > 0) local.SizeBytes = remote.SizeBytes;
                        merged.Add(local);
                    }
                    else if (local != null)
                    {
                        try { local.SourceLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Access_Local", "本地存取"); } catch { local.SourceLabel = "本地存取"; }
                        merged.Add(local);
                    }
                    else if (remote != null)
                    {
                        try { remote.SourceLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Access_Remote", "网络存取"); } catch { remote.SourceLabel = "网络存取"; }
                        merged.Add(remote);
                    }
                }
                list = merged;
            }
            catch { }
            return list.OrderByDescending(e => e.StartTime ?? DateTime.MinValue).ThenByDescending(e => e.SizeBytes).ToList();
        }
        public Task<List<RecordingGroup>> GetAllLocalGroupedAsync(Func<string, DateTime?, string?>? titleResolver = null, Func<string, DateTime?, TimeSpan?>? durationResolver = null)
        {
            var res = new List<RecordingGroup>();
            try
            {
                var root = System.AppDomain.CurrentDomain.BaseDirectory;
                var recRootCfg = LibmpvIptvClient.AppSettings.Current?.RecordingLocalDir ?? "recordings/{channel}";
                var recScanBase = recRootCfg.Replace("{source}", "").Replace("{channel}", "");
                if (string.IsNullOrWhiteSpace(recScanBase)) recScanBase = "recordings";
                var recRoot = Path.IsPathRooted(recScanBase) ? recScanBase : Path.Combine(root, recScanBase);
                if (!Directory.Exists(recRoot)) return Task.FromResult(res);
                foreach (var dir in Directory.GetDirectories(recRoot))
                {
                    var ch = Path.GetFileName(dir);
                    var subdirs = new List<string>();
                    try { subdirs = Directory.GetDirectories(dir).ToList(); } catch { }
                    if (subdirs.Count > 0)
                    {
                        foreach (var sd in subdirs)
                        {
                            var ch2 = Path.GetFileName(sd);
                            var g2 = new RecordingGroup { Channel = ch2 };
                            foreach (var f in Directory.GetFiles(sd))
                            {
                                var fi = new FileInfo(f);
                                var name = Path.GetFileNameWithoutExtension(f);
                                var ext = Path.GetExtension(f).Trim('.').ToUpperInvariant();
                                if (ext == "JSON") continue;
                                DateTime? start = ParseStart(name);
                                string title = name;
                                if (titleResolver != null)
                                {
                                    try { title = titleResolver(ch2, start) ?? title; } catch { }
                                }
                                var item = new RecordingEntry
                                {
                                    Title = title,
                                    StartTime = start,
                                    Source = "Local",
                                    PathOrUrl = f,
                                    SizeBytes = fi.Length
                                };
                            try { item.SourceLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Access_Local", "本地存取"); } catch { item.SourceLabel = "本地存取"; }
                            TryLoadMeta(f, item);
                                FillLabels(item, ext);
                                g2.Items.Add(item);
                            }
                            g2.Items = g2.Items.OrderByDescending(x => x.StartTime ?? DateTime.MinValue).ToList();
                            res.Add(g2);
                        }
                    }
                    else
                    {
                        var g = new RecordingGroup { Channel = ch };
                        foreach (var f in Directory.GetFiles(dir))
                        {
                            var fi = new FileInfo(f);
                            var name = Path.GetFileNameWithoutExtension(f);
                            var ext = Path.GetExtension(f).Trim('.').ToUpperInvariant();
                            if (ext == "JSON") continue;
                            DateTime? start = ParseStart(name);
                            string title = name;
                            if (titleResolver != null)
                            {
                                try { title = titleResolver(ch, start) ?? title; } catch { }
                            }
                            var item = new RecordingEntry
                            {
                                Title = title,
                                StartTime = start,
                                Source = "Local",
                                PathOrUrl = f,
                                SizeBytes = fi.Length
                            };
                            try { item.SourceLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Access_Local", "本地存取"); } catch { item.SourceLabel = "本地存取"; }
                            TryLoadMeta(f, item);
                            FillLabels(item, ext);
                            g.Items.Add(item);
                        }
                        g.Items = g.Items.OrderByDescending(x => x.StartTime ?? DateTime.MinValue).ToList();
                        res.Add(g);
                    }
                }
                res = res.OrderBy(x => x.Channel).ToList();
            }
            catch { }
            return Task.FromResult(res);
        }
        DateTime? ParseStart(string name)
        {
            // Expect yyyyMMdd_HHmmss in filename
            try
            {
                var m = Regex.Match(name, @"(?<d>\d{8})_(?<t>\d{6})");
                if (m.Success)
                {
                    var ds = m.Groups["d"].Value;
                    var ts = m.Groups["t"].Value;
                    return DateTime.ParseExact(ds + ts, "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch { }
            return null;
        }
        bool IsVideoExt(string ext)
        {
            if (string.IsNullOrWhiteSpace(ext)) return false;
            var e = ext.ToUpperInvariant();
            return e == "TS" || e == "MP4" || e == "MKV" || e == "MOV" || e == "M4V";
        }
        bool TryLoadMeta(string filePath, RecordingEntry item)
        {
            try
            {
                // 优先从 NTFS ADS 读取；不支持时回退旁路 .json
                string? json = null;
                try
                {
                    var adsPath = filePath + ":srcbox-meta";
                    using var ads = new FileStream(adsPath, FileMode.Open, FileAccess.Read);
                    using var sr = new StreamReader(ads);
                    json = sr.ReadToEnd();
                }
                catch
                {
                    var jf = Path.ChangeExtension(filePath, ".json");
                    if (File.Exists(jf)) json = File.ReadAllText(jf);
                }
                if (string.IsNullOrWhiteSpace(json)) return false;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var hasMetaTitle = false;
                if (root.TryGetProperty("start_utc", out var st) && st.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(st.GetString(), out var dt)) item.StartTime = dt.ToLocalTime();
                }
                if (root.TryGetProperty("end_utc", out var et) && et.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(et.GetString(), out var edt)) item.EndTime = edt.ToLocalTime();
                }
                if (root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    var s = t.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        item.Title = s!;
                        hasMetaTitle = true;
                    }
                }
                return hasMetaTitle;
            }
            catch { }
            return false;
        }

        void FillLabels(RecordingEntry item, string ext)
        {
            item.SizeLabel = HumanSize(item.SizeBytes);
            item.FormatLabel = string.IsNullOrWhiteSpace(ext) ? "" : ext;
            if (item.EndTime.HasValue) item.EndLabel = item.EndTime.Value.ToString("yyyy-MM-dd HH:mm");
        }
        string KeyOf(RecordingEntry e)
        {
            try
            {
                if (string.Equals(e.Source, "Remote", StringComparison.OrdinalIgnoreCase))
                {
                    var nm = Path.GetFileName(new Uri(e.PathOrUrl, UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new Uri(e.PathOrUrl).AbsolutePath : e.PathOrUrl);
                    return Path.GetFileNameWithoutExtension(nm).ToLowerInvariant();
                }
                else
                {
                    return Path.GetFileNameWithoutExtension(e.PathOrUrl).ToLowerInvariant();
                }
            }
            catch
            {
                return (e.Title ?? "").ToLowerInvariant();
            }
        }
        string HumanSize(long bytes)
        {
            try
            {
                string[] units = { "B", "KB", "MB", "GB" };
                double v = bytes;
                int i = 0;
                while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
                return $"{v:0.##} {units[i]}";
            }
            catch { return $"{bytes} B"; }
        }
        // ListRemoteAsync no longer used; replaced by WebDavClient.ListAsync

        string San(string s)
        {
            return (s ?? "").Replace(":", "_").Replace("/", "_").Replace("\\", "_");
        }
    }
}
