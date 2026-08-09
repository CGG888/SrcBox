using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Models;
using System.Diagnostics;

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
                var custom = AppSettings.Current?.Logo?.CacheDir;
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
        private TimeSpan Ttl => TimeSpan.FromHours(Math.Max(1, AppSettings.Current?.Logo?.CacheTtlHours ?? 24));
        private long MaxBytes => Math.Max(50, AppSettings.Current?.Logo?.CacheMaxMiB ?? 200) * 1024L * 1024L;
        private const string NegExt = ".neg";

        public string? GetCachedPath(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var hash = Sha1(url);
            var dir = CacheDir;
            if (!Directory.Exists(dir)) return null;

            try
            {
                var ext = GetExtensionFromUrl(url);
                var hashPath = Path.Combine(dir, hash + ext);
                if (File.Exists(hashPath))
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(hashPath);
                    if (age < Ttl) return hashPath;
                }

                var allFiles = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).StartsWith("."))
                    .ToArray();

                foreach (var f in allFiles)
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (name.Equals(hash, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(f))
                        {
                            return f;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public async Task<string?> GetLogoPathAsync(string channelName, string logoUrl)
        {
            if (AppSettings.Current?.Logo?.EnableCache != true) return null;
            if (string.IsNullOrWhiteSpace(logoUrl)) return null;

            var dir = EnsureCacheDir();
            if (string.IsNullOrWhiteSpace(dir)) return null;

            if (IsNegative(logoUrl))
            {
                return null;
            }

            var hash = Sha1(logoUrl);
            var ext = GetExtensionFromUrl(logoUrl);
            var hashPath = Path.Combine(dir, hash + ext);

            if (File.Exists(hashPath))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(hashPath);
                if (age < Ttl)
                {
                    return hashPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(channelName))
            {
                var sanitized = SanitizeFileName(channelName);
                var namePath = Path.Combine(dir, sanitized + ext);

                if (File.Exists(namePath))
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(namePath);
                    if (age < Ttl)
                    {
                        return namePath;
                    }
                }
            }

            return await DownloadLogoAsync(logoUrl, channelName, dir);
        }

        private async Task<string?> DownloadLogoAsync(string url, string channelName, string dir)
        {
            var tmp = Path.Combine(dir, ".dl_" + Guid.NewGuid().ToString("N"));
            try
            {
                try { Logger.Debug($"[LogoCache] Downloading: {url}"); } catch { }
                using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        try { Logger.Debug($"[LogoCache] HTTP failed: {resp.StatusCode} for {url}"); } catch { }
                        MarkNegative(url);
                        return null;
                    }
                    using (var fs = File.Create(tmp))
                    {
                        await resp.Content.CopyToAsync(fs);
                    }
                }

                var hash = Sha1(url);
                var ext = GetExtensionFromUrl(url);
                string targetPath;

                if (!string.IsNullOrWhiteSpace(channelName))
                {
                    var sanitized = SanitizeFileName(channelName);
                    var namePath = Path.Combine(dir, sanitized + ext);
                    if (!File.Exists(namePath))
                    {
                        targetPath = namePath;
                    }
                    else
                    {
                        var existingHash = HashFile(namePath);
                        if (existingHash == hash)
                        {
                            targetPath = namePath;
                        }
                        else
                        {
                            targetPath = Path.Combine(dir, hash + ext);
                        }
                    }
                }
                else
                {
                    targetPath = Path.Combine(dir, hash + ext);
                }

                if (File.Exists(targetPath) && !targetPath.Equals(tmp)) File.Delete(targetPath);
                if (!tmp.Equals(targetPath)) File.Move(tmp, targetPath);
                _ = Task.Run(() => TryCleanup()); // NEW-16: TryCleanup has internal try-catch
                try { Logger.Debug($"[LogoCache] Downloaded: {targetPath}"); } catch { }
                return targetPath;
            }
            catch (Exception ex)
            {
                try { Logger.Debug($"[LogoCache] Download failed: {ex.Message} for {url}"); } catch { }
                MarkNegative(url);
            }
            finally
            {
                if (File.Exists(tmp)) try { File.Delete(tmp); } catch { }
            }
            return null;
        }

        public async Task WarmupAndSwapAsync(IEnumerable<Channel> list)
        {
            var enableCache = AppSettings.Current?.Logo?.EnableCache;
            if (enableCache != true)
            {
                try { Logger.Info($"[LogoCache] Warmup skipped: EnableCache={enableCache}"); } catch { }
                return;
            }
            try { Logger.Info($"[LogoCache] Warmup starting: {list.Count()} channels"); } catch { }
            int ok = 0, fail = 0;
            var tasks = new List<Task>();
            foreach (var ch in list)
            {
                var logo = ch?.Logo ?? "";
                if (string.IsNullOrWhiteSpace(logo)) continue;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var local = await GetLogoPathAsync(ch.Name, logo);
                        if (!string.IsNullOrWhiteSpace(local))
                        {
                            System.Threading.Interlocked.Increment(ref ok);
                            try
                            {
                                System.Windows.Application.Current?.Dispatcher?.Invoke(() => ch.Logo = local);
                            }
                            catch { }
                        }
                        else
                        {
                            System.Threading.Interlocked.Increment(ref fail);
                        }
                    }
                    catch { System.Threading.Interlocked.Increment(ref fail); }
                }));
            }
            try { await Task.WhenAll(tasks); } catch { }
            try { Logger.Info($"[LogoCache] Warmup: {ok} ok, {fail} failed"); } catch { }
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name);
            foreach (var c in invalid)
            {
                sb.Replace(c, '_');
            }
            var sanitized = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(sanitized)) return Sha1(name).Substring(0, 12);
            return sanitized;
        }

        private string GetExtensionFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var ext = Path.GetExtension(uri.AbsolutePath);
                if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5) return ext.ToLower();
                if (url.Contains(".png", StringComparison.OrdinalIgnoreCase)) return ".png";
                if (url.Contains(".jpg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
                if (url.Contains(".jpeg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
                if (url.Contains(".gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
                if (url.Contains(".webp", StringComparison.OrdinalIgnoreCase)) return ".png";
            }
            catch { }
            return ".png";
        }

        private string HashFile(string path)
        {
            try
            {
                using var sha1 = SHA1.Create();
                using var fs = File.OpenRead(path);
                var hash = sha1.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch { return ""; }
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
            }
            catch { }
        }

        private string EnsureCacheDir()
        {
            var dir = CacheDir;
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".probe");
                using (File.Create(probe)) { }
                File.Delete(probe);
                return dir;
            }
            catch (UnauthorizedAccessException)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(AppSettings.Current?.Logo?.CacheDir))
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

        private void TryCleanup()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return;
                var files = new DirectoryInfo(CacheDir).GetFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.Extension.ToLower() != NegExt)
                    .ToArray();
                long total = 0;
                foreach (var f in files) total += f.Length;
                if (total <= MaxBytes) return;
                Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
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
