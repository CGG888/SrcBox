using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System;
using System.Reflection;

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
    private readonly HttpClient _httpClient;

    public GithubUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
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
            var latest = await FetchLatestReleaseAsync("https://api.github.com/repos/CGG888/SrcBox/releases/latest", useCdn, cancellationToken);
            
            if (latest == null)
            {
                return new UpdateCheckResult(false, currentVersion, null, "Failed to fetch release info");
            }

            var isNewer = IsNewer(latest.Version, currentVersion);
            return new UpdateCheckResult(isNewer, currentVersion, latest, string.Empty);
        }
        catch (Exception ex)
        {
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

    private async Task<ReleaseInfo?> FetchLatestReleaseAsync(string apiUrl, bool useCdn, CancellationToken cancellationToken)
    {
        var urls = new List<string> { apiUrl };
        
        if (useCdn)
        {
            var cdnList = AppSettings.Current.UpdateCdnMirrors ?? new List<string>();
            try
            {
                if (cdnList.Count == 0)
                {
                    var cdnFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "cdn.md");
                    if (File.Exists(cdnFile))
                    {
                        foreach (var line in File.ReadAllLines(cdnFile))
                        {
                            var p = line.Trim();
                            if (p.StartsWith("http")) cdnList.Add(p);
                        }
                    }
                    AppSettings.Current.UpdateCdnMirrors = cdnList;
                    AppSettings.Current.Save();
                }
                foreach (var p in cdnList)
                    urls.Add(p.TrimEnd('/') + "/" + apiUrl);
            }
            catch { }
        }

        foreach (var url in urls)
        {
            try
            {
                // Create a separate CTS for each request to avoid sharing the parent token if it's long-lived
                // But we should respect the parent token
                using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCts.Token);
                
                using var resp = await _httpClient.GetAsync(url, linkedCts.Token);
                if (!resp.IsSuccessStatusCode) continue;
                
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
            catch { }
        }
        return null;
    }
}
