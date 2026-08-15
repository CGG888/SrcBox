using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public class MainWindowChannelPlaybackActionsViewModel : ViewModelBase
    {
        // Pre-compiled regex patterns for rtp2httpd time placeholder expansion (OPT-4)
        private static readonly System.Text.RegularExpressions.Regex s_rtp2httpdMacroRegex =
            new System.Text.RegularExpressions.Regex(@"\$\{\((b|e)\)(.*?)\}",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex s_utcPlaceholderRegex =
            new System.Text.RegularExpressions.Regex(@"\{utc:(.*?)\}",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex s_utcendPlaceholderRegex =
            new System.Text.RegularExpressions.Regex(@"\{utcend:(.*?)\}",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly MainShellViewModel _shell;
        private readonly DispatcherTimer _sourceTimeoutTimer;

        public event Action? RequestEpgRefresh;
        public event Action? RequestHistoryRefresh;
        public event Action<Channel, DateTime?>? RequestEpgReload;
        public event Action? RequestVideoShow;

        public MainWindowChannelPlaybackActionsViewModel(MainShellViewModel shell)
        {
            _shell = shell;
            _sourceTimeoutTimer = new DispatcherTimer();
            _sourceTimeoutTimer.Tick += OnSourceTimeout;
        }

        public void PlayChannel(Channel ch, IEnumerable<EpgProgram>? epgItems = null)
        {
            if (_shell.PlayerEngine == null || ch == null || _shell.UserDataStore == null) return;

            try
            {
                _shell.PlayerEngine.EnsureReadyForLoad();
                _shell.ClearRecordingPlayingIndicator();
                if (_shell.IsTimeshiftActive) _shell.IsTimeshiftActive = false;

                var syncResult = _shell.ChannelPlaybackSyncActions.SyncChannelPlaybackAndEpg(
                    _shell.Channels,
                    ch,
                    _shell.CurrentPlayingProgram,
                    epgItems,
                    _shell.EpgSelectionSyncActions.ClearPlaying);
                _shell.CurrentPlayingProgram = syncResult.CurrentPlayingProgram;
                
                if (syncResult.ShouldRefreshEpg)
                {
                    RequestEpgRefresh?.Invoke();
                }
            }
            catch { }

            if (ch.Tag is Source src)
            {
                _shell.IsPaused = false;
                var url = _shell.SourceLoader.SanitizeUrl(src.Url);

                try
                {
                    var neighbors = new List<string> { url };
                    if (_shell.CurrentSources != null && _shell.CurrentSources.Count > 0)
                    {
                        foreach (var s in _shell.CurrentSources)
                            if (!string.IsNullOrWhiteSpace(s?.Url)) neighbors.Add(s.Url);
                    }
                    DnsPrefetcher.PrefetchForUrls(neighbors);
                    ConnectionPreheater.PreheatForUrls(neighbors);
                }
                catch { }

                string streamType = "Unicast";
                if (_shell.SourceLoader.IsMulticast(url)) streamType = "Multicast";
                else if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) streamType = "LocalFile";
                else if (src.Name.Contains("组播")) streamType = "Multicast(Proxy)";
                else if (src.Name.Contains("单播")) streamType = "Unicast(Proxy)";

                LibmpvIptvClient.Diagnostics.Logger.Info($"[Live] 开始直播播放 - 频道: {ch.Name}, 类型: {streamType}, URL: {url}");

                try
                {
                    _shell.PlayerEngine.SetPropertyString("cache", "yes");
                    _shell.PlayerEngine.SetPropertyString("demuxer-max-bytes", AppSettings.Current.DemuxerMaxBytesMiB + "MiB");
                    _shell.PlayerEngine.SetPropertyString("demuxer-max-back-bytes", AppSettings.Current.DemuxerMaxBackBytesMiB + "MiB");
                    _shell.PlayerEngine.SetPropertyString("cache-pause", "no");
                    _shell.PlayerEngine.SetPropertyString("force-seekable", "yes");
                    LibmpvIptvClient.Diagnostics.Logger.Debug($"[Live] mpv properties set: cache=yes, demuxer-max-bytes={AppSettings.Current.DemuxerMaxBytesMiB}MiB, demuxer-max-back-bytes={AppSettings.Current.DemuxerMaxBackBytesMiB}MiB");
                }
                catch (Exception ex)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Warn($"[Live] Failed to set mpv properties: {ex.Message}");
                }

                _shell.CurrentChannel = ch;
                _shell.CurrentSources = _shell.SourceLoader.BuildSourcesForChannel(ch, _shell.Channels);
                _shell.CurrentUrl = url;

                _sourceTimeoutTimer.Stop();
                _sourceTimeoutTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, AppSettings.Current.SourceTimeoutSec));
                _sourceTimeoutTimer.Start();

                if (AppSettings.Current.FccPrefetchCount > 0 && _shell.SourceLoader.IsMulticast(url))
                {
                    var idx = _shell.Channels.IndexOf(ch);
                    var list = new List<string>();
                    var count = Math.Max(0, AppSettings.Current.FccPrefetchCount);
                    for (int i = 1; i <= count; i++)
                    {
                        var n = idx + i;
                        if (n >= 0 && n < _shell.Channels.Count)
                        {
                            var next = _shell.Channels[n];
                            if (next?.Tag is Source nextSrc)
                            {
                                list.Add(_shell.SourceLoader.SanitizeUrl(nextSrc.Url));
                            }
                        }
                    }
                    _shell.PlayerEngine.LoadWithPrefetch(url, list);
                }
                else
                {
                    _shell.PlayerEngine.Play(url);
                }

                try
                {
                    var key = UserDataStore.ComputeKey(ch, url);
                    _shell.UserDataStore.AddOrUpdateHistory(new HistoryItem
                    {
                        Key = key,
                        Name = ch.Name,
                        Logo = ch.Logo,
                        Group = ch.Group,
                        SourceUrl = url,
                        PlayType = "live"
                    });
                    RequestHistoryRefresh?.Invoke();
                }
                catch { }

                try
                {
                    LibmpvIptvClient.Diagnostics.Logger.Info("[Live] 请求显示视频画面");
                    RequestVideoShow?.Invoke();
                    _shell.DispatchPlaybackEvent(new StartLivePlayback(
                        ch.TvgId ?? ch.Id ?? ch.Name ?? "",
                        _shell.CurrentPlayingProgram,
                        url));
                }
                catch (Exception ex)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Error($"[Live] RequestVideoShow error: {ex.Message}");
                }

                RequestEpgReload?.Invoke(ch, null);
            }
            else
            {
                LibmpvIptvClient.Diagnostics.Logger.Warn($"[Live] PlayChannel skipped: ch.Tag is not Source (ch.Tag={(ch.Tag == null ? "null" : ch.Tag.GetType().Name)}, ch.Name={ch.Name}, ch.Id={ch.Id})");
            }
        }

        private void OnSourceTimeout(object? sender, EventArgs e)
        {
            _sourceTimeoutTimer.Stop();
            if (_shell.PlayerEngine == null || _shell.CurrentChannel == null || _shell.CurrentSources == null || _shell.CurrentSources.Count <= 1) return;

            var t = _shell.PlayerEngine.GetTimePos();
            if (t.HasValue && t.Value > 0) return;

            var currentUrl = _shell.SourceLoader.SanitizeUrl(_shell.CurrentChannel.Tag is Source src ? src.Url : "");

            // Log all source health statuses
            var healthSummary = string.Join(", ", _shell.CurrentSources
                .Select(s => $"{s.Name ?? ShortenUrl(s.Url)}={s.IsHealthy}({s.LatencyMs}ms)"));
            LibmpvIptvClient.Diagnostics.Logger.Warn($"[Source] Auto-degrade for {_shell.CurrentChannel.Name}. Health: {healthSummary}");

            // Try to find the first healthy source, preferring the one after the current
            var sources = _shell.CurrentSources;
            Source? healthySource = null;

            // First pass: healthy sources after current index
            for (int i = 1; i < sources.Count; i++)
            {
                var idx = (sources.IndexOf(_shell.CurrentChannel.Tag ?? sources[0]) + i) % sources.Count;
                if (sources[idx].IsHealthy)
                {
                    healthySource = sources[idx];
                    break;
                }
            }

            // Fallback: any healthy source
            healthySource ??= sources.FirstOrDefault(s => s.IsHealthy);

            if (healthySource != null)
            {
                var logMsg = healthySource.Url != currentUrl
                    ? $"[Source] Health-aware switch: {currentUrl} → {healthySource.Url ?? healthySource.Name}"
                    : $"[Source] Health check passed, continuing: {currentUrl}";
                LibmpvIptvClient.Diagnostics.Logger.Warn(logMsg);
                _shell.CurrentChannel.Tag = healthySource;
                PlayChannel(_shell.CurrentChannel);
            }
            else
            {
                LibmpvIptvClient.Diagnostics.Logger.Warn($"[Source] All sources unreachable for {_shell.CurrentChannel.Name}, no more to switch to.");
            }
        }

        private static string ShortenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "(空)";
            try { return new Uri(url).Host; } catch { return url.Length > 30 ? url.Substring(0, 30) + "..." : url; }
        }

        public void CheckPlaybackStarted(double timePos)
        {
            if (timePos > 0) _sourceTimeoutTimer.Stop();
        }

        public void PlayCatchup(Channel ch, EpgProgram prog)
        {
            if (_shell.PlayerEngine == null) return;
            
            _shell.ClearRecordingPlayingIndicator();
            _shell.PlayerEngine.EnsureReadyForLoad();
            
            var url = ch.CatchupSource;
            if (string.IsNullOrEmpty(url))
            {
                if (AppSettings.Current.Replay.Enabled && !string.IsNullOrEmpty(AppSettings.Current.Replay.UrlFormat))
                {
                    var fmt = AppSettings.Current.Replay.UrlFormat;
                    if (!string.IsNullOrEmpty(fmt) && (fmt.StartsWith("?") || fmt.StartsWith("&")))
                    {
                        var live = (ch.Tag is Source s1 && !string.IsNullOrEmpty(s1.Url)) ? s1.Url
                                   : (ch.Sources != null && ch.Sources.Count > 0 ? ch.Sources[0].Url : "");
                        if (string.IsNullOrEmpty(live)) return;
                        var sep = live.Contains("?") ? "&" : "?";
                        url = live + sep + fmt.TrimStart('?', '&');
                    }
                    else
                    {
                        url = fmt.Replace("{id}", ch.Id ?? ch.Name);
                    }
                }
                else return;
            }

            try
            {
                if (_shell.IsTimeshiftActive) _shell.IsTimeshiftActive = false;
                
                url = ProcessUrlPlaceholders(url, prog.Start, prog.End, AppSettings.Current.Replay.AppendEpgTime);
                try { url = LibmpvIptvClient.Services.UrlTimeRewriter.RewriteIfEnabled(AppSettings.Current, url, prog.Start, prog.End, false); } catch { }

                LibmpvIptvClient.Diagnostics.Logger.Info($"[Replay] 开始回看: {prog.Title} ({prog.Start:HH:mm}-{prog.End:HH:mm}), URL: {url}");
                _shell.PlayerEngine.Play(url);
                _shell.CurrentUrl = url;
                RequestVideoShow?.Invoke();
                
                // Update playing status (requires access to current EPG list items, passed via callback or stored in VM)
                // For now, we assume the View handles the EPG list refresh via event if needed, or we pass items.
                // But `EpgSelectionSyncActions.SetPlaying` takes items.
                // Let's trigger a refresh request that View handles by updating binding source.
                
                _shell.CurrentPlayingProgram = prog;

                try
                {
                    _shell.DispatchPlaybackEvent(new StartReplayPlayback(
                        ch.TvgId ?? ch.Id ?? ch.Name ?? "",
                        prog,
                        url));
                }
                catch { }

                _shell.CurrentEpgDate = prog.Start.Date;
                RequestEpgReload?.Invoke(ch, prog.Start);
                
                try
                {
                    var key = _shell.CurrentChannel != null ? UserDataStore.ComputeKey(_shell.CurrentChannel, url) : (prog.Title + "|" + url);
                    _shell.UserDataStore.AddOrUpdateHistory(new HistoryItem
                    {
                        Key = key,
                        Name = _shell.CurrentChannel?.Name ?? prog.Title,
                        Logo = _shell.CurrentChannel?.Logo ?? "",
                        Group = _shell.CurrentChannel?.Group ?? "",
                        SourceUrl = url,
                        PlayType = "catchup"
                    });
                    RequestHistoryRefresh?.Invoke();
                }
                catch { }
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Error($"[Replay] Playback failed: {ex.Message}");
            }
        }

        public void PlayUrlGeneric(string url, string playTypeLabel)
        {
            if (_shell.PlayerEngine == null) return;
            
            _shell.PlayerEngine.Play(url);
            _shell.CurrentUrl = url;

            bool isReplay = playTypeLabel == "回放" || playTypeLabel == LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Playback", "回放") || playTypeLabel == "catchup";
            bool isTimeshift = playTypeLabel == "时移" || playTypeLabel == LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Timeshift", "时移") || playTypeLabel == "timeshift";

            if (isTimeshift)
            {
                _shell.DispatchPlaybackEvent(new StartTimeshiftPlayback(
                    _shell.CurrentChannel?.TvgId ?? _shell.CurrentChannel?.Id ?? _shell.CurrentChannel?.Name ?? "",
                    DateTime.Now,
                    _shell.CurrentPlayingProgram,
                    url));
            }
            else if (isReplay)
            {
                var replayProgram = _shell.CurrentPlayingProgram;
                if (replayProgram == null && _shell.CurrentChannel != null && _shell.EpgService != null)
                {
                    try
                    {
                        var t = _shell.GetPlaybackLocalTime();
                        if (t.HasValue)
                        {
                            replayProgram = _shell.EpgService.GetProgramAt(_shell.CurrentChannel.TvgId, t.Value, _shell.CurrentChannel.Name);
                        }
                    }
                    catch { }
                }
                _shell.DispatchPlaybackEvent(new StartReplayPlayback(
                    _shell.CurrentChannel?.TvgId ?? _shell.CurrentChannel?.Id ?? _shell.CurrentChannel?.Name ?? "",
                    replayProgram,
                    url));
            }
            else
            {
                _shell.DispatchPlaybackEvent(new StartLivePlayback(
                    _shell.CurrentChannel?.TvgId ?? _shell.CurrentChannel?.Id ?? _shell.CurrentChannel?.Name ?? "",
                    _shell.CurrentPlayingProgram,
                    url));
            }

            if (isReplay) { _shell.IsTimeshiftActive = false; }
            if (isTimeshift) { _shell.IsTimeshiftActive = true; }

            _shell.IsPaused = false;
        }

        public void PlayCatchupAt(Channel ch, DateTime start)
        {
            if (_shell.PlayerEngine == null) return;

            var url = ch.CatchupSource;
            if (string.IsNullOrEmpty(url))
            {
                if (AppSettings.Current.Timeshift.Enabled && !string.IsNullOrEmpty(AppSettings.Current.Timeshift.UrlFormat))
                {
                    var fmt = AppSettings.Current.Timeshift.UrlFormat;
                    if (!string.IsNullOrEmpty(fmt) && (fmt.StartsWith("?") || fmt.StartsWith("&")))
                    {
                        var live = (ch.Tag is Source s1 && !string.IsNullOrEmpty(s1.Url)) ? s1.Url
                                   : (ch.Sources != null && ch.Sources.Count > 0 ? ch.Sources[0].Url : "");
                        if (string.IsNullOrEmpty(live)) return;
                        var sep = live.Contains("?") ? "&" : "?";
                        url = live + sep + fmt.TrimStart('?', '&');
                    }
                    else
                    {
                        url = fmt.Replace("{id}", ch.Id ?? ch.Name);
                    }
                }
                else return;
            }

            try
            {
                _shell.MarkTimeshiftSeeked();
                DateTime end = DateTime.Now;
                EpgProgram? targetProgram = null;
                try
                {
                    if (_shell.EpgService != null)
                    {
                        var programs = _shell.EpgService.GetPrograms(ch.TvgId, ch.TvgName, ch.Name);
                        if (programs != null && programs.Count > 0)
                        {
                            var prog = programs.FirstOrDefault(p => p.Start <= start && p.End > start);
                            if (prog != null)
                            {
                                targetProgram = prog;
                                end = prog.End; // Use actual program end time, not current time
                            }
                        }
                    }
                }
                catch { }

                url = ProcessUrlPlaceholders(url, start, end, AppSettings.Current.Timeshift.AppendEpgTime);
                try { url = LibmpvIptvClient.Services.UrlTimeRewriter.RewriteIfEnabled(AppSettings.Current, url, start, end, _shell.IsTimeshiftActive); } catch { }
                
                LibmpvIptvClient.Diagnostics.Logger.Info($"[Replay] Start Replay - Channel: {ch.Name}, Time: {start:yyyy-MM-dd HH:mm:ss}, URL: {url}");
                _shell.PlayerEngine.Play(url);
                _shell.CurrentUrl = url;
                LibmpvIptvClient.Diagnostics.Logger.Info($"[Replay] After Play - CurrentPlayingProgram={_shell.CurrentPlayingProgram?.Title ?? "null"}");
                _shell.CurrentPlayingProgram = targetProgram;
                LibmpvIptvClient.Diagnostics.Logger.Info($"[Replay] After assignment - CurrentPlayingProgram={_shell.CurrentPlayingProgram?.Title ?? "null"}");
                _shell.IsTimeshiftActive = false;
                RequestVideoShow?.Invoke();

                _shell.DispatchPlaybackEvent(new StartReplayPlayback(
                    ch.TvgId ?? ch.Id ?? ch.Name ?? "",
                    targetProgram,
                    url));
                
                try
                {
                    var key = UserDataStore.ComputeKey(ch, url);
                    _shell.UserDataStore.AddOrUpdateHistory(new HistoryItem
                    {
                        Key = key,
                        Name = ch.Name,
                        Logo = ch.Logo,
                        Group = ch.Group,
                        SourceUrl = url,
                        PlayType = "timeshift"
                    });
                    RequestHistoryRefresh?.Invoke();
                }
                catch { }
            }
            catch { }
        }

        private string ProcessUrlPlaceholders(string url, DateTime start, DateTime end, bool appendEpgTime)
        {
            // 0. Decode URL-encoded placeholders (e.g., $%7B(b)yyyyMMdd%7CUTC%7D -> ${(b)yyyyMMdd|UTC})
            url = Uri.UnescapeDataString(url);

            // 1. Unix Timestamp & Duration (rtp2httpd macros)
            long tsStart = new DateTimeOffset(start).ToUnixTimeSeconds();
            long tsEnd = new DateTimeOffset(end).ToUnixTimeSeconds();
            url = url.Replace("${timestamp}", tsStart.ToString());
            url = url.Replace("{timestamp}", tsStart.ToString());
            url = url.Replace("${end_timestamp}", tsEnd.ToString());
            url = url.Replace("{end_timestamp}", tsEnd.ToString());

            long dur = (long)(end - start).TotalSeconds;
            url = url.Replace("${duration}", dur.ToString());
            url = url.Replace("{duration}", dur.ToString());

            // 2. {utc:...} and {utcend:...} with Macro Expansion - use pre-compiled regex
            url = s_utcPlaceholderRegex.Replace(url, m => FormatUtcPlaceholder(m.Groups[1].Value, start.ToUniversalTime()));
            url = s_utcendPlaceholderRegex.Replace(url, m => FormatUtcPlaceholder(m.Groups[1].Value, end.ToUniversalTime()));

            // 3. ${...} format with Macro Expansion - use pre-compiled regex
            url = s_rtp2httpdMacroRegex.Replace(url, m =>
            {
                var type = m.Groups[1].Value;
                var fmt = m.Groups[2].Value;

                // Expand rtp2httpd Macros
                if (fmt == "YmdHMS") fmt = "yyyyMMddHHmmss";
                else if (fmt == "Ymd") fmt = "yyyyMMdd";
                else if (fmt == "HMS") fmt = "HHmmss";

                var dt = (type == "b" ? start : end);
                if (fmt.EndsWith("|UTC", StringComparison.OrdinalIgnoreCase))
                {
                    dt = dt.ToUniversalTime();
                    fmt = fmt.Substring(0, fmt.Length - 4);
                }

                // Unix seconds for start/end
                if (string.Equals(fmt, "timestamp", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fmt, "unix", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fmt, "epoch", StringComparison.OrdinalIgnoreCase))
                {
                    var unix = new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeSeconds();
                    return unix.ToString();
                }
                try { return dt.ToString(fmt); } catch { return m.Value; }
            });

            // 4. Fixed Local Time Placeholders
            url = url.Replace("{start}", start.ToString("yyyyMMddHHmmss"));
            url = url.Replace("{end}", end.ToString("yyyyMMddHHmmss"));
            // 5. Append EPG tracking parameters (minute-level) for replay/timeshift correlation
            // 仅当 appendEpgTime 为 true 时追加，避免影响不支持该参数的播放源
            if (appendEpgTime)
            {
                try
                {
                    var minTs = start.ToString("yyyy-MM-ddTHH:mm");
                    var sep = url.Contains("?") ? "&" : "?";
                    url = url + sep + "epg_time=" + Uri.EscapeDataString(minTs);
                }
                catch { }
            }
            return url;
        }

        private static string FormatUtcPlaceholder(string fmt, DateTime dt)
        {
            // Expand rtp2httpd Macros
            if (fmt == "YmdHMS") fmt = "yyyyMMddHHmmss";
            else if (fmt == "Ymd") fmt = "yyyyMMdd";
            else if (fmt == "HMS") fmt = "HHmmss";
            try { return dt.ToString(fmt); } catch { return "{utc:" + fmt + "}"; }
        }

        public void JumpToChannelByIdOrName(string id, string name)
        {
            try
            {
                if (_shell.Channels == null || _shell.Channels.Count == 0) return;
                Channel? target = null;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    target = _shell.Channels.FirstOrDefault(c => string.Equals(c?.TvgId ?? "", id, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(c?.Id ?? "", id, StringComparison.OrdinalIgnoreCase));
                }
                if (target == null && !string.IsNullOrWhiteSpace(name))
                {
                    target = _shell.Channels.FirstOrDefault(c => string.Equals(c?.Name ?? "", name, StringComparison.OrdinalIgnoreCase));
                }
                if (target != null) PlayChannel(target);
            }
            catch { }
        }
    }
}
