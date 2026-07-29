using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;
using System.Diagnostics;
using WpfApp = System.Windows.Application;

namespace LibmpvIptvClient.Services
{
    public class LogoCacheService
    {
        private static readonly Lazy<LogoCacheService> _lazy = new Lazy<LogoCacheService>(() => new LogoCacheService());
        public static LogoCacheService Instance => _lazy.Value;
        private readonly HttpClient _http = HttpClientService.Instance.Client;

        private string CacheDir
        {
            get
            {
                var custom = AppSettings.Current.Logo.CacheDir;
                if (!string.IsNullOrWhiteSpace(custom)) return custom;
                string exeDir = "";
                try { exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(exeDir))
                {
                    try { exeDir = AppContext.BaseDirectory; } catch { }
                }
                return Path.Combine(exeDir, "logo-cache");
            }
        }
        private TimeSpan Ttl => TimeSpan.FromHours(Math.Max(1, AppSettings.Current.Logo.CacheTtlHours));
        private long MaxBytes => Math.Max(50, AppSettings.Current.Logo.CacheMaxMiB) * 1024L * 1024L;
        private const string NegExt = ".neg"; // negative cache marker (no image / unsupported)

        public async Task<string?> GetOrDownloadAsync(string url)
        {
            try
            {
                if (!AppSettings.Current.Logo.EnableCache) return null;
                if (string.IsNullOrWhiteSpace(url)) return null;
                var dir = EnsureCacheDir();
                if (string.IsNullOrWhiteSpace(dir)) return null;
                
                // Negative cache: if previously determined no valid image, skip network entirely
                if (IsNegative(url))
                {
                    return null;
                }
                
                var ext = Path.GetExtension(url);
                if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5) ext = ".img";
                var name = Sha1(url) + ext;
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                    if (age < Ttl)
                    {
                        return path;
                    }
                }
                var tmp = path + ".downloading";
                using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        try { MarkNegative(url); } catch { }
                        return File.Exists(path) ? path : null;
                    }
                    // Content-Type gate: avoid saving unsupported formats (e.g., webp) to prevent WIC decode error
                    try
                    {
                        var ct = resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                        if (!(ct == "image/png" || ct == "image/jpeg" || ct == "image/jpg" || ct == "image/bmp" || ct == "image/gif"))
                        {
                            try { MarkNegative(url); } catch { }
                            return null;
                        }
                    }
                    catch { }
                    using (var fs = File.Create(tmp))
                    {
                        await resp.Content.CopyToAsync(fs);
                    }
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _ = Task.Run(() => TryCleanup());
                return path;
            }
            catch { }
            return null;
        }
        public bool IsNegative(string url)
        {
            try
            {
                var dir = EnsureCacheDir();
                var marker = Path.Combine(dir, Sha1(url) + NegExt);
                if (File.Exists(marker))
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(marker);
                    if (age < Ttl) return true;
                    try { File.Delete(marker); } catch { }
                }
            }
            catch { }
            return false;
        }
        public void MarkNegative(string url)
        {
            try
            {
                var dir = EnsureCacheDir();
                var marker = Path.Combine(dir, Sha1(url) + NegExt);
                File.WriteAllText(marker, "neg");
                try { Logger.Debug($"[LogoCache] NEG-WRITE {marker}"); } catch { }
            }
            catch { }
        }
        private string EnsureCacheDir()
        {
            var dir = CacheDir;
            try
            {
                Directory.CreateDirectory(dir);
                // quick write probe
                var probe = Path.Combine(dir, ".probe");
                using (File.Create(probe)) { }
                File.Delete(probe);
                return dir;
            }
            catch (UnauthorizedAccessException)
            {
                // If default (empty in settings) points to a protected location, fallback to LocalAppData and persist
                try
                {
                    if (string.IsNullOrWhiteSpace(AppSettings.Current.Logo.CacheDir))
                    {
                        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SrcBox", "logo-cache");
                        Directory.CreateDirectory(fallback);
                        AppSettings.Current.Logo.CacheDir = fallback;
                        AppSettings.Current.Save();
                        try { Logger.Info($"Logo缓存目录切换: {fallback}"); } catch { }
                        return fallback;
                    }
                }
                catch { }
            }
            catch { }
            return dir;
        }

        public async Task WarmupAndSwapAsync(System.Collections.Generic.IEnumerable<Models.Channel> list)
        {
            if (!AppSettings.Current.Logo.EnableCache) return;
            var tasks = new System.Collections.Generic.List<Task>();
            int ok = 0, fail = 0, neg = 0;
            foreach (var ch in list)
            {
                var logo = ch?.Logo ?? "";
                if (string.IsNullOrWhiteSpace(logo)) continue;
                tasks.Add(Task.Run(async () =>
                {
                    var local = await GetOrDownloadAsync(logo);
                    if (!string.IsNullOrWhiteSpace(local))
                    {
                        System.Threading.Interlocked.Increment(ref ok);
                        try { WpfApp.Current?.Dispatcher?.Invoke(() => ch.Logo = local); } catch { }
                    }
                    else
                    {
                        if (IsNegative(logo)) System.Threading.Interlocked.Increment(ref neg);
                        else System.Threading.Interlocked.Increment(ref fail);
                    }
                }));
            }
            try { await Task.WhenAll(tasks); } catch { }
            try
            {
                var dir = EnsureCacheDir();
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[LogoCache] summary dir={dir} ok={ok} fail={fail} neg={neg}");
            }
            catch { }
        }

        private void TryCleanup()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return;
                var files = new DirectoryInfo(CacheDir).GetFiles("*", SearchOption.TopDirectoryOnly);
                long total = 0;
                Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                foreach (var f in files) total += f.Length;
                if (total <= MaxBytes) return;
                foreach (var f in files)
                {
                    if (total <= MaxBytes) break;
                    try
                    {
                        total -= f.Length;
                        f.Delete();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string Sha1(string s)
        {
            using var sha1 = SHA1.Create();
            var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(s));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
