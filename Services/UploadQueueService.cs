using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace LibmpvIptvClient.Services
{
    public class UploadQueueService
    {
        public static UploadQueueService Instance { get; } = new UploadQueueService();
            public static event Action<string>? OnUploaded; // remoteDir
        readonly object _lock = new object();
        readonly List<UploadItem> _items = new List<UploadItem>();
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _notified = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        SemaphoreSlim _sem = new SemaphoreSlim(1, 64);
        volatile bool _running = false;
        int _maxConcurrency = 1;
        int _maxRetry = 3;
        int _backoffMs = 1000;
        int _maxKBps = 0;
        string QueueFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload_queue.json");
        // NEW-11: Fire-and-forget async save to avoid blocking lock with sync I/O
        volatile bool _dirty = false;
        readonly SemaphoreSlim _saveLock = new(1, 1);

        UploadQueueService() { Load(); }

        public void Configure(int maxConcurrency, int maxRetry, int backoffMs, int maxKBps)
        {
            _maxConcurrency = Math.Max(1, maxConcurrency);
            _maxRetry = Math.Max(0, maxRetry);
            _backoffMs = Math.Max(100, backoffMs);
            _maxKBps = Math.Max(0, maxKBps);
            _sem = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        }

        public void Enqueue(string localPath, string remoteDir, string fileName, LibmpvIptvClient.WebDavConfig wd, bool deleteLocalOnSuccess = false, string? remoteTempUrl = null, string? logoPath = null)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath)) return;
            var item = new UploadItem
            {
                Id = Guid.NewGuid().ToString("N"),
                LocalPath = localPath,
                RemoteDir = remoteDir,
                FileName = fileName,
                Attempts = 0,
                Status = "pending",
                CreatedUtc = DateTime.UtcNow,
                DeleteLocalOnSuccess = deleteLocalOnSuccess,
                RemoteTempUrl = remoteTempUrl ?? "",
                LogoPath = logoPath ?? ""
            };
            lock (_lock)
            {
                _items.Add(item);
                _dirty = true;
            }
            _ = TrySaveAsync(); // non-blocking async save
            _ = RunAsync(wd);
        }
        public List<UploadItem> GetSnapshot()
        {
            lock (_lock)
            {
                return _items.Select(x => new UploadItem
                {
                    Id = x.Id,
                    LocalPath = x.LocalPath,
                    RemoteDir = x.RemoteDir,
                    FileName = x.FileName,
                    Attempts = x.Attempts,
                    Status = x.Status,
                    CreatedUtc = x.CreatedUtc,
                    CompletedUtc = x.CompletedUtc,
                    Error = x.Error,
                    DeleteLocalOnSuccess = x.DeleteLocalOnSuccess
                }).ToList();
            }
        }
        public void Retry(string id)
        {
            bool needSave = false;
            lock (_lock)
            {
                var it = _items.FirstOrDefault(i => i.Id == id);
                if (it != null)
                {
                    it.Status = "pending";
                    it.Error = null;
                    needSave = true;
                }
            }
            if (needSave) { _dirty = true; _ = TrySaveAsync(); }
        }
        public void Remove(string id)
        {
            bool needSave = false;
            lock (_lock)
            {
                _items.RemoveAll(i => i.Id == id);
                needSave = true;
            }
            if (needSave) { _dirty = true; _ = TrySaveAsync(); }
        }

        async Task RunAsync(LibmpvIptvClient.WebDavConfig wd)
        {
            if (_running) return;
            _running = true;
            try
            {
                while (true)
                {
                    UploadItem? next = null;
                    bool needSave = false;
                    lock (_lock)
                    {
                        next = _items.FirstOrDefault(i => i.Status == "pending" || (i.Status == "failed" && i.Attempts <= _maxRetry));
                        if (next != null)
                        {
                            next.Status = "running"; // 立即占位，避免并发挑中同一项
                            needSave = true;
                        }
                    }
                    if (next == null) break;
                    if (needSave) Save();
                    await _sem.WaitAsync();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessItem(next, wd);
                        }
                        finally
                        {
                            _sem.Release();
                        }
                    });
                }
            }
            finally
            {
                _running = false;
            }
        }

        async Task ProcessItem(UploadItem it, LibmpvIptvClient.WebDavConfig wd)
        {
            // 状态已在调度时标为 running
            var cli = new LibmpvIptvClient.Services.WebDavClient(wd);
            var url = cli.Combine(it.RemoteDir.TrimEnd('/') + "/" + it.FileName);
            var elapsedMs = 0L;
            try
            {
                await cli.EnsureCollectionAsync(it.RemoteDir);
            }
            catch { }
            bool ok = false;
            Exception? ex = null;
            for (; it.Attempts <= _maxRetry; it.Attempts++)
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    ok = await cli.PutFileAsync(url, it.LocalPath, "video/MP2T", _maxKBps);
                    sw.Stop();
                    if (ok) elapsedMs = sw.ElapsedMilliseconds;
                    if (ok) break;
                }
                catch (Exception e)
                {
                    ex = e;
                }
                await Task.Delay(_backoffMs * (int)Math.Pow(2, Math.Min(6, it.Attempts)));
            }
            it.Status = ok ? "done" : "failed";
            it.CompletedUtc = ok ? DateTime.UtcNow : null;
            it.Error = ok ? null : (ex?.Message ?? "upload failed");
            _dirty = true;
            _ = TrySaveAsync();
            // 上传 sidecar（同名 .json）
            if (ok)
            {
                try
                {
                    var sideLocal = Path.ChangeExtension(it.LocalPath, ".json");
                    if (File.Exists(sideLocal))
                    {
                        var cli2 = new LibmpvIptvClient.Services.WebDavClient(wd);
                        var sideRemote = cli2.Combine(it.RemoteDir.TrimEnd('/') + "/" + Path.ChangeExtension(it.FileName, ".json"));
                        try { await cli2.PutFileAsync(sideRemote, sideLocal, "application/json", _maxKBps); } catch { }
                    }
                }
                catch { }
            }
            if (ok && it.DeleteLocalOnSuccess)
            {
                var deleted = false;
                for (int i = 0; i < 3 && !deleted; i++)
                {
                    try
                    {
                        if (File.Exists(it.LocalPath))
                        {
                            File.Delete(it.LocalPath);
                            deleted = !File.Exists(it.LocalPath);
                        }
                        else deleted = true;
                    }
                    catch
                    {
                        await Task.Delay(300 * (i + 1));
                    }
                }
            }
            // dual_realtime 残留 .part 清理
            if (ok && !string.IsNullOrWhiteSpace(it.RemoteTempUrl))
            {
                try { await cli.DeleteAsync(it.RemoteTempUrl); } catch { }
            }
            // 所有附属动作完成后再广播刷新事件
            if (ok)
            {
                try { OnUploaded?.Invoke(it.RemoteDir); } catch { }
            }
            try
            {
                if (ok)
                {
                    var now = DateTime.UtcNow;
                    var last = _notified.GetOrAdd(url, now.AddMinutes(-10));
                    if ((now - last).TotalSeconds > 2)
                    {
                        string? subtitle = null;
                        try
                        {
                            var side = Path.ChangeExtension(it.LocalPath, ".json");
                            if (File.Exists(side))
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(side));
                                if (doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String)
                                    subtitle = t.GetString();
                            }
                        }
                        catch { }
                        if (string.IsNullOrWhiteSpace(subtitle)) subtitle = Path.GetFileNameWithoutExtension(it.FileName);
                        // 标题改为频道名（从 RemoteDir 取末级目录名）
                        string titleChan = "SrcBox";
                        try
                        {
                            var rd = (it.RemoteDir ?? "").TrimEnd('/', '\\');
                            var parts = rd.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0) titleChan = Uri.UnescapeDataString(parts.Last());
                        }
                        catch { }
                        LibmpvIptvClient.Services.ToastService.ShowSimple(LibmpvIptvClient.Services.ToastKind.UploadSuccess, titleChan, subtitle, it.LogoPath, 10000);
                        _notified[url] = now;
                    }
                }
            }
            catch { }
        }
        static string FormatSize(long bytes)
        {
            try
            {
                const long KB = 1024, MB = 1024 * 1024, GB = 1024 * 1024 * 1024;
                if (bytes >= GB) return (bytes / (double)GB).ToString("0.00") + " GB";
                if (bytes >= MB) return (bytes / (double)MB).ToString("0.00") + " MB";
                if (bytes >= KB) return (bytes / (double)KB).ToString("0") + " KB";
                return bytes + " B";
            }
            catch { return bytes.ToString(); }
        }
        static string FormatDuration(long ms)
        {
            try
            {
                if (ms <= 0) return "0.0s";
                return (ms / 1000.0).ToString("0.0") + "s";
            }
            catch { return ms + "ms"; }
        }

        void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_items);
                File.WriteAllText(QueueFile, json);
            }
            catch { }
        }

        // NEW-11: Async save with lock-free dirty flag and single-flight pattern
        async Task TrySaveAsync()
        {
            if (!_dirty) return;
            if (!await _saveLock.WaitAsync(0).ConfigureAwait(false)) return; // another save in progress, skip
            try
            {
                // Re-check under lock
                if (!_dirty) return;
                List<UploadItem>? snapshot = null;
                lock (_lock)
                {
                    snapshot = _items.ToList();
                }
                _dirty = false;
                await Task.Run(() =>
                {
                    var json = JsonSerializer.Serialize(snapshot);
                    File.WriteAllText(QueueFile, json);
                }).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                _saveLock.Release();
            }
        }
        void Load()
        {
            try
            {
                if (!File.Exists(QueueFile)) return;
                var json = File.ReadAllText(QueueFile);
                var arr = JsonSerializer.Deserialize<List<UploadItem>>(json);
                if (arr != null)
                {
                    foreach (var i in arr) { if (i.Status == "running") i.Status = "failed"; }
                    _items.AddRange(arr);
                }
            }
            catch { }
        }

        public class UploadItem
        {
            public string Id { get; set; } = "";
            public string LocalPath { get; set; } = "";
            public string RemoteDir { get; set; } = "";
            public string FileName { get; set; } = "";
            public int Attempts { get; set; }
            public string Status { get; set; } = "pending";
            public DateTime CreatedUtc { get; set; }
            public DateTime? CompletedUtc { get; set; }
            public string? Error { get; set; }
            public bool DeleteLocalOnSuccess { get; set; }
            public string RemoteTempUrl { get; set; } = "";
            public string LogoPath { get; set; } = "";
        }
    }
}
