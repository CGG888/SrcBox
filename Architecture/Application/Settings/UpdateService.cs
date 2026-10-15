using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Architecture.Application.Settings;

/// <summary>
/// Release metadata shared by the update service, the update dialog and the settings page.
/// </summary>
public sealed class ReleaseInfo
{
    public string Version { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FileName { get; set; } = "";
}

public sealed record UpdateCheckResult(
    bool IsNewer,
    string CurrentVersion,
    ReleaseInfo? ReleaseInfo,
    string ErrorMessage);

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(bool useCdn, CancellationToken cancellationToken = default);
    string GetCurrentVersion();
}

public class GithubUpdateService : IUpdateService
{
    /// <summary>Release endpoint every source below is built from.</summary>
    internal const string LatestReleaseApiUrl = "https://api.github.com/repos/CGG888/SrcBox/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly Func<HttpClient> _directClientFactory;

    public GithubUpdateService(HttpClient httpClient) : this(httpClient, CreateDirectClient)
    {
    }

    /// <summary>Test seam: the client used for the proxy-less retry pass.</summary>
    internal GithubUpdateService(HttpClient httpClient, Func<HttpClient> directClientFactory)
    {
        _httpClient = httpClient;
        _directClientFactory = directClientFactory;
    }

    /// <summary>Plain client without the system proxy, for the retry after every source failed.</summary>
    internal static HttpClient CreateDirectClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrcBox/UpdateCheck");
        return client;
    }

    public string GetCurrentVersion()
    {
        try
        {
            var v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (string.IsNullOrEmpty(v))
                v = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            if (v != null && v.Contains("+")) v = v.Substring(0, v.IndexOf("+"));
            return v ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool useCdn, CancellationToken cancellationToken = default)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SrcBox/UpdateCheck");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var currentVersion = GetCurrentVersion();
            var attempts = new List<string>();

            var latest = await FetchLatestReleaseAsync(_httpClient, LatestReleaseApiUrl, useCdn, cancellationToken, attempts);

            // The shared client routes through the system (WinINET) proxy, and a local proxy that is down
            // or half configured breaks every source while a plain connection still works. A total failure
            // is therefore retried once without a proxy before the check is called failed.
            if (latest == null)
            {
                using var direct = _directClientFactory();
                latest = await FetchLatestReleaseAsync(direct, LatestReleaseApiUrl, false, cancellationToken, attempts);
            }

            if (latest == null)
            {
                Logger.Info("[Update] 检查失败: " + string.Join("; ", attempts));
                return new UpdateCheckResult(false, currentVersion, null, "Failed to fetch release info");
            }

            if (attempts.Count > 0)
                Logger.Debug("[Update] 已回退到镜像: " + string.Join("; ", attempts));

            var isNewer = IsNewer(latest.Version, currentVersion);
            Logger.Info(isNewer
                ? $"[Update] 发现新版本 {latest.Version}（当前 {currentVersion}）"
                : $"[Update] 已是最新版本 {currentVersion}");
            return new UpdateCheckResult(isNewer, currentVersion, latest, string.Empty);
        }
        catch (Exception ex)
        {
            Logger.Info("[Update] 检查异常: " + ex.Message);
            return new UpdateCheckResult(false, GetCurrentVersion(), null, ex.Message);
        }
    }

    private bool IsNewer(string remote, string local) => IsNewerVersion(remote, local);

    /// <summary>
    /// True when <paramref name="remote"/> is a strictly newer semantic version than
    /// <paramref name="local"/>. Unparsable values are treated as "not newer".
    /// </summary>
    public static bool IsNewerVersion(string? remote, string? local)
    {
        try
        {
            Version vr = new Version((remote ?? "").Split('-')[0].TrimStart('v', 'V'));
            Version vl = new Version((local ?? "").Split('-')[0].TrimStart('v', 'V'));
            return vr > vl;
        }
        catch { return false; }
    }

    private async Task<ReleaseInfo?> FetchLatestReleaseAsync(HttpClient client, string apiUrl, bool useCdn,
        CancellationToken cancellationToken, List<string> attempts)
    {
        var mirrors = AppSettings.Current.UpdateCdnMirrors;

        if (useCdn)
        {
            try
            {
                if (mirrors == null || mirrors.Count == 0)
                {
                    mirrors = new List<string>(CdnMirrorList.Load());
                    if (mirrors.Count > 0)
                    {
                        AppSettings.Current.UpdateCdnMirrors = mirrors;
                        AppSettings.Current.Save();
                    }
                }
            }
            catch { }
        }

        var urls = BuildCandidateUrls(apiUrl, useCdn ? mirrors : null);

        foreach (var url in urls)
        {
            var host = HostOf(url);
            try
            {
                // Create a separate CTS for each request to avoid sharing the parent token if it's long-lived
                // But we should respect the parent token
                using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCts.Token);
                
                using var resp = await client.GetAsync(url, linkedCts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    // A blocked or rate limited source is worth reporting when the whole check fails.
                    attempts.Add($"{host}={(int)resp.StatusCode}");
                    continue;
                }
                
                var json = await resp.Content.ReadAsStringAsync(linkedCts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var tag = root.TryGetProperty("tag_name", out var tagEl) ? (tagEl.GetString() ?? "") : "";
                var body = root.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";
                
                string assetUrl = "";
                string assetName = "";
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array && assets.GetArrayLength() > 0)
                {
                    JsonElement? chosen = null;
                    foreach (var a in assets.EnumerateArray())
                    {
                        if (a.TryGetProperty("name", out var n) && (n.GetString() ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            chosen = a; break;
                        }
                    }
                    if (chosen == null) chosen = assets[0];
                    assetUrl = chosen.Value.GetProperty("browser_download_url").GetString() ?? "";
                    assetName = chosen.Value.GetProperty("name").GetString() ?? "";
                }
                
                var ver = (tag ?? root.GetProperty("name").GetString() ?? "").Trim().TrimStart('v', 'V');
                return new ReleaseInfo { Version = ver, Notes = body ?? "", DownloadUrl = assetUrl, FileName = assetName };
            }
            catch (Exception ex)
            {
                attempts.Add($"{host}={Shorten(ex)}");
            }
        }
        return null;
    }

    /// <summary>Host of a candidate url, used only for the diagnostic line.</summary>
    static string HostOf(string url)
    {
        try { return new Uri(url).Host; } catch { return url; }
    }

    static string Shorten(Exception ex)
    {
        var message = (ex.InnerException ?? ex).Message.Replace("\r", " ").Replace("\n", " ");
        return ex is TaskCanceledException || ex is OperationCanceledException ? "超时" : message;
    }

    /// <summary>
    /// Request order of one release lookup: the API itself first, then every mirror prefix with the API
    /// url appended (gh-proxy style: <c>https://gh-proxy.org/https://api.github.com/...</c>).
    /// </summary>
    internal static List<string> BuildCandidateUrls(string apiUrl, IEnumerable<string>? mirrors)
    {
        var urls = new List<string> { apiUrl };
        if (mirrors == null) return urls;

        foreach (var mirror in mirrors)
        {
            var prefix = (mirror ?? "").Trim().TrimEnd('/');
            if (prefix.Length == 0) continue;
            urls.Add(prefix + "/" + apiUrl);
        }
        return urls;
    }
}
