using System;
using System.Windows;
using System.Diagnostics;
using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using System.IO;

namespace LibmpvIptvClient
{
    public partial class AboutWindow : Window
    {
        private readonly HttpClient _http = new HttpClient();
        private ReleaseInfo? _latest;
        private string _currentVersion = "0.0.0";
        public AboutWindow()
        {
            InitializeComponent();
            // Get version from Assembly Informational Version (matches <Version> in csproj)
            var v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            // Fallback to assembly version if informational version is missing
            if (string.IsNullOrEmpty(v))
            {
                v = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            }

            // Clean up version string (remove commit hash if present, e.g. 1.0.1+a1b2c3)
            if (v != null && v.Contains("+"))
            {
                v = v.Substring(0, v.IndexOf("+"));
            }

            _currentVersion = v ?? "0.0.0";
            TxtVersion.Text = $"v{_currentVersion}";

            // 仅在打开“关于”窗口时进行一次版本检查
            _ = CheckUpdateAsync();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new UpdateDialog(_latest);
                dlg.Owner = this;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dlg.Topmost = this.Topmost;
                dlg.ShowDialog();
            }
            catch { }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { }
        }

        private async System.Threading.Tasks.Task CheckUpdateAsync()
        {
            try
            {
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("SrcBox/UpdateCheck");
                _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                var latest = await FetchLatestReleaseAsync("https://api.github.com/repos/CGG888/SrcBox/releases/latest");
                if (latest == null) return;
                _latest = latest;
                if (IsNewer(_latest.Version, _currentVersion))
                {
                    BadgeNew.Visibility = Visibility.Visible;
                    // 只在打开时提示一次
                    var r = ModernMessageBox.Show(this, string.Format(LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_NewVersionFound", "发现新版本 v{0}，是否查看更新？"), _latest.Version), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.YesNo);
                    if (r == true) BtnUpdate_Click(this, new RoutedEventArgs());
                }
            }
            catch { }
        }

        private static bool IsNewer(string remote, string local)
        {
            try
            {
                Version vr = new Version(remote.Split('-')[0].TrimStart('v','V'));
                Version vl = new Version(local.Split('-')[0].TrimStart('v','V'));
                return vr > vl;
            }
            catch { return false; }
        }

        public class ReleaseInfo
        {
            public string Version { get; set; } = "";
            public string Notes { get; set; } = "";
            public string DownloadUrl { get; set; } = "";
            public string FileName { get; set; } = "";
        }

        private async System.Threading.Tasks.Task<ReleaseInfo?> FetchLatestReleaseAsync(string apiUrl)
        {
            // 优先主站，失败再走用户设置中的 CDN 列表；若为空再尝试 cdn.md
            var urls = new System.Collections.Generic.List<string> { apiUrl };
            var cdnList = LibmpvIptvClient.AppSettings.Current.UpdateCdnMirrors ?? new System.Collections.Generic.List<string>();
            try
            {
                if (cdnList.Count == 0)
                {
                    var cdnFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "screenshots", "cdn.md");
                    if (File.Exists(cdnFile))
                    {
                        foreach (var line in File.ReadAllLines(cdnFile))
                        {
                            var p = line.Trim();
                            if (p.StartsWith("http")) cdnList.Add(p);
                        }
                    }
                    LibmpvIptvClient.AppSettings.Current.UpdateCdnMirrors = cdnList;
                    LibmpvIptvClient.AppSettings.Current.Save();
                }
                foreach (var p in cdnList)
                    urls.Add(p.TrimEnd('/') + "/" + apiUrl);
            }
            catch { }

            foreach (var url in urls)
            {
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                    using var resp = await _http.GetAsync(url, cts.Token);
                    if (!resp.IsSuccessStatusCode) continue;
                    var json = await resp.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var tag = root.TryGetProperty("tag_name", out var tagEl) ? (tagEl.GetString() ?? "") : "";
                    var body = root.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";
                    string assetUrl = "";
                    string assetName = "";
                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array && assets.GetArrayLength() > 0)
                    {
                        // 选择 .exe 优先，其次第一个
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
                catch { /* try next mirror */ }
            }
            return null;
        }
    }
}
