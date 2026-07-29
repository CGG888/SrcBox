using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services;

public class M3UCacheEntry
{
    public string Url { get; set; } = "";
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public DateTime CachedAt { get; set; }
    public double CacheTtlHours { get; set; } = 24;
}

public class M3UCacheService
{
    private static readonly Lazy<M3UCacheService> _lazy = new Lazy<M3UCacheService>(() => new M3UCacheService());
    public static M3UCacheService Instance => _lazy.Value;

    private readonly string _cacheDir;
    private readonly HttpClient _http;
    private readonly object _lock = new object();

    private const string CacheFileName = "m3u_cache";
    private const string MetaFileExt = ".meta";

    private M3UCacheService()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SrcBox",
            "m3u_cache"
        );
        _http = HttpClientService.Instance.Client;

        try
        {
            if (!Directory.Exists(_cacheDir))
            {
                Directory.CreateDirectory(_cacheDir);
            }
        }
        catch { }
    }

    public string CacheDir => _cacheDir;

    private string GetCacheFilePath(string url)
    {
        var hash = GetUrlHash(url);
        return Path.Combine(_cacheDir, $"{CacheFileName}_{hash}.dat");
    }

    private string GetMetaFilePath(string url)
    {
        var hash = GetUrlHash(url);
        return Path.Combine(_cacheDir, $"{CacheFileName}_{hash}{MetaFileExt}");
    }

    private static string GetUrlHash(string url)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes);
    }

    public async Task<(List<Channel>? channels, bool fromCache)> LoadFromCacheAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (null, false);

        var cachePath = GetCacheFilePath(url);
        var metaPath = GetMetaFilePath(url);

        try
        {
            if (!File.Exists(cachePath) || !File.Exists(metaPath))
            {
                return (null, false);
            }

            M3UCacheEntry? meta = null;
            try
            {
                var metaJson = await File.ReadAllTextAsync(metaPath);
                meta = JsonSerializer.Deserialize<M3UCacheEntry>(metaJson);
            }
            catch
            {
                return (null, false);
            }

            if (meta == null) return (null, false);

            var ttl = AppSettings.Current?.M3uCacheTtlHours ?? meta.CacheTtlHours;
            if ((DateTime.Now - meta.CachedAt).TotalHours > ttl)
            {
                Logger.Info($"M3U缓存已过期 (TTL={ttl}h)，将刷新");
                return (null, false);
            }

            bool needRefresh = false;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                bool urlHasDynamicToken = url.Contains("token=", StringComparison.OrdinalIgnoreCase) ||
                                          url.Contains("time=", StringComparison.OrdinalIgnoreCase) ||
                                          url.Contains("ts=", StringComparison.OrdinalIgnoreCase) ||
                                          url.Contains("v=", StringComparison.OrdinalIgnoreCase);

                if (!urlHasDynamicToken)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Head, url);
                        using var response = await _http.SendAsync(request);

                        if (response.IsSuccessStatusCode)
                        {
                            var serverEtag = response.Headers.ETag?.Tag;
                            var serverLastModified = response.Content.Headers.LastModified?.ToString();

                            if (!string.IsNullOrEmpty(serverEtag) && !string.Equals(meta.ETag, serverEtag, StringComparison.Ordinal))
                            {
                                Logger.Info("M3U缓存ETag已变化，需要刷新");
                                needRefresh = true;
                            }
                            else if (!string.IsNullOrEmpty(serverLastModified) && !string.Equals(meta.LastModified, serverLastModified, StringComparison.Ordinal))
                            {
                                Logger.Info("M3U缓存已修改，需要刷新");
                                needRefresh = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"[M3U缓存] HEAD请求失败: {ex.Message}，依赖TTL判断");
                    }
                }
                else
                {
                    Logger.Info("M3U URL包含动态参数，依赖TTL判断");
                }
            }
            else if (File.Exists(url))
            {
                var fileInfo = new FileInfo(url);
                var fileLastModified = fileInfo.LastWriteTimeUtc.ToString("O");
                if (!string.Equals(meta.LastModified, fileLastModified, StringComparison.Ordinal))
                {
                    Logger.Info("M3U本地文件已修改，需要刷新");
                    needRefresh = true;
                }
            }

            if (needRefresh)
            {
                return (null, false);
            }

            try
            {
                var data = await File.ReadAllBytesAsync(cachePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var channels = JsonSerializer.Deserialize<List<Channel>>(data, options);
                if (channels != null)
                {
                    Logger.Info($"从缓存加载了 {channels.Count} 个频道");
                    return (channels, true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[M3U Cache] Failed to deserialize cache: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[M3U Cache] Load failed: {ex.Message}");
        }

        return (null, false);
    }

    public async Task SaveToCacheAsync(string url, List<Channel> channels, string? etag = null, string? lastModified = null)
    {
        if (string.IsNullOrWhiteSpace(url) || channels == null || channels.Count == 0) return;

        var cachePath = GetCacheFilePath(url);
        var metaPath = GetMetaFilePath(url);

        try
        {
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = false
            };
            var data = JsonSerializer.Serialize(channels, options);
            await File.WriteAllTextAsync(cachePath, data);

            var meta = new M3UCacheEntry
            {
                Url = url,
                ETag = etag,
                LastModified = lastModified,
                CachedAt = DateTime.Now,
                CacheTtlHours = AppSettings.Current?.M3uCacheTtlHours ?? 24
            };
            var metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(metaPath, metaJson);

            Logger.Info($"已保存 {channels.Count} 个频道到缓存");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[M3U Cache] Save failed: {ex.Message}");
        }
    }

    public async Task<string?> GetServerETagAsync(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return response.Headers.ETag?.Tag ?? response.Content.Headers.LastModified?.ToString();
                }
            }
        }
        catch { }
        return null;
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            try
            {
                if (Directory.Exists(_cacheDir))
                {
                    foreach (var file in Directory.GetFiles(_cacheDir, $"{CacheFileName}_*"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    Logger.Info("M3U缓存已清除");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[M3U Cache] Clear failed: {ex.Message}");
            }
        }
    }

    public void RemoveCache(string url)
    {
        try
        {
            var cachePath = GetCacheFilePath(url);
            var metaPath = GetMetaFilePath(url);
            if (File.Exists(cachePath)) File.Delete(cachePath);
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
        catch { }
    }
}
