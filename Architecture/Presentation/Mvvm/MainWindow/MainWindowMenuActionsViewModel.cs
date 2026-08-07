using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public partial class MainWindowMenuActionsViewModel : ObservableObject
    {
        private readonly MainShellViewModel _shell;

        public event Action<string>? RequestLoadChannels;
        public event Action<string>? RequestLoadSingleStream;
        public event Action<bool>? RequestFccUpdate;
        public event Action<bool>? RequestUdpUpdate;
        public event Action<bool>? RequestEpgToggle;
        public event Action<bool>? RequestMinimalToggle;
        public event Action<bool>? RequestDeinterlaceUpdate;
        public event Action? RequestRefreshM3uList;

        public MainWindowMenuActionsViewModel(MainShellViewModel shell)
        {
            _shell = shell;
        }

        public void OpenFile()
        {
            var file = _shell.DialogActions.PromptOpenFile();
            if (!string.IsNullOrWhiteSpace(file))
            {
                _shell.SourceLoader.UpdateLastSource(file, null);
                RequestLoadChannels?.Invoke(file!);
            }
        }

        public async Task OpenUrlAsync()
        {
            var owner = GetOwnerWindow();
            var url = _shell.DialogActions.PromptOpenUrl(owner);
            if (!string.IsNullOrWhiteSpace(url))
            {
                if (AppSettings.Current.SavedSources != null)
                {
                    foreach (var s in AppSettings.Current.SavedSources) s.IsSelected = false;
                    AppSettings.Current.Save();
                }

                // 自动检测 URL 类型：M3U 列表还是单个播放源
                var (isM3U, errorMsg) = await DetectUrlTypeAsync(url!);
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    ShowError(errorMsg);
                    return;
                }

                if (isM3U)
                {
                    // M3U 列表：加载频道列表（会自动播放第一个频道）
                    RequestLoadChannels?.Invoke(url!);
                }
                else
                {
                    // 单个播放源
                    RequestLoadSingleStream?.Invoke(url!);
                }
            }
        }

        // 同步版本，供菜单命令使用
        public void OpenUrl()
        {
            _ = OpenUrlAsync();
        }

        private async Task<(bool isM3U, string? errorMsg)> DetectUrlTypeAsync(string url)
        {
            // 非 HTTP/HTTPS 的 URL（如 rtp://, udp:// 等组播地址）直接当作单个播放源
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return (false, null);
            }

            // HTTP URL 中包含 /rtp/、/udp/ 等路径的，是组播代理地址，直接当单个播放源
            var urlLower = url.ToLowerInvariant();
            if (urlLower.Contains("/rtp/") || urlLower.Contains("/udp/") ||
                urlLower.Contains("/mpegts/") || urlLower.Contains("/ts/"))
            {
                return (false, null);
            }

            // 检测是否为 TXT 格式（根据 URL 扩展名）
            bool isTxt = IsTxtUrl(url);
            LibmpvIptvClient.Diagnostics.Logger.Info($"[TxtDetect] URL={url}, IsTxt={isTxt}");
            if (isTxt)
            {
                return (true, null);
            }

            try
            {
                var client = HttpClientService.Instance.Client;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(0, 8192);
                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    return (false, null);
                }
                var content = await resp.Content.ReadAsStringAsync();

                // 检测是否为 M3U 格式（必须是标准的 IPTV M3U 列表）
                // HLS 流 (.m3u8) 通常每个 #EXTINF 后面跟的是 .ts 或 .m3u8 分片地址
                // IPTV M3U 列表通常有多个频道条目
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                int extInfCount = 0;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                    {
                        extInfCount++;
                    }
                }

                // 如果有 2 个以上 #EXTINF，认为是 M3U 列表
                if (extInfCount >= 2)
                {
                    return (true, null);
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                // 检测失败，当作单个播放源处理
                LibmpvIptvClient.Diagnostics.Logger.Warn($"URL 类型检测失败: {ex.Message}");
                return (false, null);
            }
        }

        private static bool IsTxtUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var path = uri.AbsolutePath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        path = path.ToLowerInvariant();
                        if (path.EndsWith("/txt") || path.EndsWith("/text"))
                        {
                            return true;
                        }
                        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                        var extWithoutDot = ext.TrimStart('.');
                        LibmpvIptvClient.Diagnostics.Logger.Info($"[TxtDetect] path={path}, ext={ext}, extWithoutDot={extWithoutDot}");
                        return extWithoutDot == "txt" || extWithoutDot == "text";
                    }
                    else
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Info($"[TxtDetect] path is null or empty for URL={url}");
                    }
                }
                else
                {
                    LibmpvIptvClient.Diagnostics.Logger.Info($"[TxtDetect] Uri.TryCreate failed for URL={url}");
                }
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Info($"[TxtDetect] Exception: {ex.Message}");
            }
            return false;
        }

        private void ShowError(string message)
        {
            try
            {
                var owner = GetOwnerWindow();
                System.Windows.MessageBox.Show(owner, message, "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            catch { }
        }

        public void AddM3uFile()
        {
            var files = _shell.DialogActions.PromptOpenFiles();
            if (files.Count > 0)
            {
                if (AppSettings.Current.SavedSources == null) AppSettings.Current.SavedSources = new List<M3uSource>();
                
                M3uSource? lastSrc = null;
                foreach (var file in files)
                {
                    var src = new M3uSource { Name = System.IO.Path.GetFileNameWithoutExtension(file), Url = file };
                    AppSettings.Current.SavedSources.Add(src);
                    lastSrc = src;
                }
                AppSettings.Current.Save();
                RequestRefreshM3uList?.Invoke();

                if (lastSrc != null) LoadM3u(lastSrc);
            }
        }

        public void AddM3uUrl()
        {
            var owner = GetOwnerWindow();
            var result = _shell.DialogActions.PromptAddM3u(owner);
            if (result == null) return;
            var src = new M3uSource { Name = result.Name, Url = result.Url };
            if (AppSettings.Current.SavedSources == null) AppSettings.Current.SavedSources = new List<M3uSource>();
            AppSettings.Current.SavedSources.Add(src);
            AppSettings.Current.Save();
            RequestRefreshM3uList?.Invoke();
            LoadM3u(src);
        }

        public void EditM3u(M3uSource source)
        {
            var owner = GetOwnerWindow();
            var result = _shell.DialogActions.PromptEditM3u(owner, source.Name, source.Url, _shell.WindowStateActions.IsFullscreen);
            
            if (result != null)
            {
                if (result.IsDelete)
                {
                    if (AppSettings.Current.SavedSources != null)
                    {
                        var target = AppSettings.Current.SavedSources.FirstOrDefault(s => s.Name == source.Name && s.Url == source.Url);
                        if (target != null)
                        {
                            AppSettings.Current.SavedSources.Remove(target);
                            AppSettings.Current.Save();
                        }
                    }
                }
                else
                {
                    source.Name = result.Name;
                    source.Url = result.Url;
                    AppSettings.Current.Save();
                }
            }
        }

        public void LoadM3u(M3uSource src)
        {
            _shell.SourceLoader.UpdateLastSource("", src);
            RequestLoadChannels?.Invoke(src.Url);
        }

        public void ShowAbout()
        {
            var owner = GetOwnerWindow();
            var dlg = _shell.DialogActions.CreateAboutDialog(owner, _shell.WindowStateActions.IsFullscreen);
            dlg.ShowDialog();
        }

        public void ExitApp()
        {
            _shell.CloseWindow();
        }

        public void ToggleFcc(bool on)
        {
            AppSettings.Current.FccPrefetchCount = on ? 2 : 0;
            AppSettings.Current.Save();
            RequestFccUpdate?.Invoke(on);
        }

        public void ToggleUdp(bool on)
        {
            AppSettings.Current.EnableUdpOptimization = on;
            AppSettings.Current.Save();
            RequestUdpUpdate?.Invoke(on);
        }

        /// <summary>
        /// 切换反交错（右键菜单快速开关）：
        /// - on=true  → "auto"（自动检测，仅对真正隔行的流启用）
        /// - on=false → "no"（关闭）
        /// 立即生效到当前 mpv 实例
        /// </summary>
        public void ToggleDeinterlace(bool on)
        {
            AppSettings.Current.Deinterlace = on ? "auto" : "no";
            AppSettings.Current.Save();
            ApplyDeinterlaceImmediate();
            RequestDeinterlaceUpdate?.Invoke(on);
            try
            {
                LibmpvIptvClient.Diagnostics.Logger.Info($"[Deinterlace] 切换: {(on ? "auto" : "no")}");
            }
            catch { }
        }

        /// <summary>
        /// 立即将 PlaybackSettings 中的反交错配置应用到当前 mpv 实例
        /// 供菜单 / 设置页 / 启动初始化共用
        /// </summary>
        public void ApplyDeinterlaceImmediate()
        {
            try
            {
                _shell.PlayerEngine?.SetDeinterlace(
                    AppSettings.Current.Deinterlace,
                    AppSettings.Current.DeinterlaceFieldParity,
                    AppSettings.Current.DeinterlaceAlgorithm);
            }
            catch (Exception ex)
            {
                try { LibmpvIptvClient.Diagnostics.Logger.Warn($"[Deinterlace] 应用失败: {ex.Message}"); } catch { }
            }
        }

        public void ToggleEpg(bool on)
        {
            RequestEpgToggle?.Invoke(on);
        }

        public void ToggleDrawer(bool on)
        {
            _shell.IsDrawerCollapsed = !on;
        }

        public void ToggleMinimalMode(bool on)
        {
            RequestMinimalToggle?.Invoke(on);
        }

        private System.Windows.Window GetOwnerWindow()
        {
            return (_shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.FullscreenWindow != null) 
                ? (System.Windows.Window)_shell.WindowStateActions.FullscreenWindow 
                : System.Windows.Application.Current.MainWindow;
        }

        public List<MenuItemViewModel> BuildSourceMenuItems()
        {
            if (_shell.CurrentChannel == null) return new List<MenuItemViewModel>();
            if (_shell.CurrentSources == null || _shell.CurrentSources.Count == 0)
            {
                _shell.CurrentSources = _shell.SourceLoader.BuildSourcesForChannel(_shell.CurrentChannel, _shell.Channels);
            }

            string currentPlayingUrl = "";
            if (_shell.CurrentChannel.Tag is Source playingSrc)
            {
                currentPlayingUrl = _shell.SourceLoader.SanitizeUrl(playingSrc.Url);
            }

            var list = new List<MenuItemViewModel>();
            foreach (var s in _shell.CurrentSources)
            {
                var isChecked = _shell.SourceLoader.SanitizeUrl(s.Url) == currentPlayingUrl;
                list.Add(new MenuItemViewModel
                {
                    Header = SourceLabel(s),
                    Command = new RelayCommand(() => SwitchSource(s)),
                    IsChecked = isChecked,
                    IsCheckable = true,
                    Tag = s
                });
            }
            return list;
        }

        private void SwitchSource(Source newSrc)
        {
            if (_shell.CurrentChannel == null) return;
            _shell.CurrentChannel.Tag = newSrc;
            var u = _shell.SourceLoader.SanitizeUrl(newSrc.Url);
            LibmpvIptvClient.Diagnostics.Logger.Info("切换到 " + u);
            _shell.CurrentUrl = u;
            _shell.PlayerEngine?.Play(u);
        }

        public void SwitchSourceViaRemote()
        {
            SwitchSourceCycle(true);
        }

        public void SwitchSourceCycle(bool next)
        {
            if (_shell.CurrentChannel == null) return;
            var channels = _shell.Channels?.ToList() ?? new List<Channel>();
            if (_shell.CurrentSources == null || _shell.CurrentSources.Count == 0)
            {
                _shell.CurrentSources = _shell.SourceLoader.BuildSourcesForChannel(_shell.CurrentChannel, channels);
            }
            var sources = _shell.CurrentSources;
            if (sources == null || sources.Count <= 1) return;

            var currentUrl = _shell.SourceLoader.SanitizeUrl(_shell.CurrentUrl ?? "");
            var currentIndex = sources.FindIndex(s => _shell.SourceLoader.SanitizeUrl(s.Url) == currentUrl);

            int nextIndex;
            if (next)
            {
                nextIndex = (currentIndex + 1) % sources.Count;
            }
            else
            {
                nextIndex = (currentIndex - 1 + sources.Count) % sources.Count;
            }
            var nextSource = sources[nextIndex];
            LibmpvIptvClient.Diagnostics.Logger.Log($"[Remote] SwitchSource cycle {currentIndex + 1}→{nextIndex + 1}/{sources.Count}");
            SwitchSource(nextSource);
        }

        public List<MenuItemViewModel> BuildRatioMenuItems()
        {
            var options = new[] { 
                (LibmpvIptvClient.Helpers.ResxLocalizer.Get("Ratio_Default", "默认"), "default"),
                ("16:9", "16:9"),
                ("4:3", "4:3"),
                (LibmpvIptvClient.Helpers.ResxLocalizer.Get("Ratio_Stretch", "拉伸"), "stretch"),
                (LibmpvIptvClient.Helpers.ResxLocalizer.Get("Ratio_Fill", "填充"), "fill"),
                (LibmpvIptvClient.Helpers.ResxLocalizer.Get("Ratio_Crop", "裁剪"), "crop")
            };

            var list = new List<MenuItemViewModel>();
            foreach (var (label, val) in options)
            {
                var isChecked = string.Equals(_shell.CurrentAspect, val, StringComparison.OrdinalIgnoreCase);
                list.Add(new MenuItemViewModel
                {
                    Header = label,
                    Command = new RelayCommand(() => SwitchRatio(val)),
                    IsChecked = isChecked,
                    IsCheckable = true,
                    Tag = val
                });
            }
            return list;
        }

        private void SwitchRatio(string val)
        {
            _shell.CurrentAspect = val;
            _shell.PlayerEngine?.SetAspectRatio(val);
        }

        private string SourceLabel(Source s)
        {
            var parts = new List<string>();
            bool isMulticast = false;
            bool isUnicast = false;

            if (!string.IsNullOrWhiteSpace(s.Name))
            {
                if (s.Name.Contains("组播")) isMulticast = true;
                else if (s.Name.Contains("单播")) isUnicast = true;
            }
            
            if (!isMulticast && !isUnicast)
            {
                isMulticast = _shell.SourceLoader.IsMulticast(s.Url);
            }

            parts.Add(isMulticast ? LibmpvIptvClient.Helpers.ResxLocalizer.Get("Stream_Multicast", "组播") : LibmpvIptvClient.Helpers.ResxLocalizer.Get("Stream_Unicast", "单播"));
            
            if (s.Quality != null)
            {
                if (s.Quality.Height >= 2160) parts.Add("UHD");
                else if (s.Quality.Height >= 1080) parts.Add("FHD");
                else if (s.Quality.Height >= 720) parts.Add("HD");
                else if (s.Quality.Height > 0) parts.Add("SD");
            }
            
            if (s.Quality != null && s.Quality.Fps > 0) parts.Add($"{s.Quality.Fps:0.#}fps");
            
            return string.Join("-", parts);
        }
    }

    public class MenuItemViewModel
    {
        public string Header { get; set; } = "";
        public ICommand? Command { get; set; }
        public bool IsChecked { get; set; }
        public bool IsCheckable { get; set; }
        public object? Tag { get; set; }
    }
}
