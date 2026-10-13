using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using LibmpvIptvClient.Services.WebRemote.Voice;

namespace LibmpvIptvClient.Services.WebRemote
{
    public static class WebRemoteManager
    {
        private static WebRemoteServer? _server;
        private static MainShellViewModel? _shell;

        public static bool IsRunning => _server?.IsRunning ?? false;

        /// <summary>True when the certificate is loaded, so the same port also answers https.</summary>
        public static bool IsTlsAvailable => _server?.IsTlsAvailable ?? false;

        /// <summary>
        /// The token GET /api/voice currently expects, for display in the settings tab. Falls back to the
        /// stored value before the server has started.
        /// </summary>
        public static string CurrentVoiceToken
        {
            get
            {
                var live = _server?.VoiceToken;
                return string.IsNullOrWhiteSpace(live)
                    ? AppSettings.Current?.WebRemote?.Voice?.Token ?? ""
                    : live;
            }
        }

        public static void Initialize(MainShellViewModel shell)
        {
            _shell = shell;
            var config = AppSettings.Current.WebRemote;

            if (!config.Enabled)
            {
                Stop();
                return;
            }

            try
            {
                _server = new WebRemoteServer();

                // Wire up callbacks
                _server.GetStatusCallback = GetStatus;
                _server.GetChannelsCallback = GetChannels;
                _server.GetEpgCallback = GetEpg;
                _server.PlayCallback = DoPlay;
                _server.PauseCallback = DoPause;
                _server.StopCallback = DoStop;
                _server.SetVolumeCallback = (v) => {
                    if (_shell == null) return;
                    _shell.Volume = v;
                    _shell.IsMuted = v <= 0;
                };
                _server.ChangeChannelCallback = ChangeChannel;
                _server.ExitCallback = ExitApp;
                _server.FullscreenCallback = ToggleFullscreen;
                _server.SwitchSourceCallback = DoSwitchSource;
                _server.GetSourcesCallback = GetSources;
                _server.SelectSourceCallback = SelectSource;
                _server.AddSourceCallback = AddSource;
                _server.RemoveSourceCallback = RemoveSource;
                _server.SeekCallback = DoSeek;
                _server.SetSpeedCallback = DoSetSpeed;
                _server.SetTimeshiftCallback = DoSetTimeshift;
                _server.ReplayProgramCallback = DoReplayProgram;
                _server.GetRemindersCallback = GetReminders;
                _server.AddReminderCallback = AddReminder;
                _server.CancelReminderCallback = CancelReminder;
                _server.GetRecordingsCallback = GetRecordings;
                _server.StopRecordingCallback = StopRecording;
                _server.DeleteRecordingCallback = DeleteRecording;
                _server.VoiceCommandCallback = VoiceCommand;
                // The voice endpoint is meant for a phone-side shortcut, which cannot log in over the
                // websocket: it carries this token instead. An empty WebRemote password would leave the
                // endpoint open to the LAN, so a random one is minted once and kept in the settings.
                _server.SetVoiceToken(EnsureVoiceToken());

                // The HTTPS listener always comes up with the HTTP one: it is what makes PWA install work on a phone.
                _server.Start(config.HttpPort, config.RequirePassword, config.Password);
                Logger.Debug($"[WebRemote] Manager initialized on port {config.HttpPort}{(IsTlsAvailable ? " (http + https)" : " (http only)")}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] Failed to initialize: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            _server?.Stop();
            _server?.Dispose();
            _server = null;
            Logger.Debug("[WebRemote] Manager shutdown");
        }

        public static void RestartIfNeeded()
        {
            if (_shell == null) return;
            var config = AppSettings.Current.WebRemote;

            // Always restart if enabled (settings may have changed)
            if (config.Enabled)
            {
                Stop(); // Stop existing server first
                Initialize(_shell); // Reinitialize with new settings
            }
            else if (IsRunning)
            {
                Stop();
            }
        }

        private static void Stop()
        {
            _server?.Stop();
            _server?.Dispose();
            _server = null;
        }

        private static string GetLocalizedFavGroupName()
        {
            var lang = AppSettings.Current.Language ?? "";
            if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return WebRemoteStrings.Get("en-US").TryGetValue("fav_group", out var en) ? en : "Favorites";
            if (lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                return WebRemoteStrings.Get("ru-RU").TryGetValue("fav_group", out var ru) ? ru : "Избранное";
            if (lang.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || lang.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
                return WebRemoteStrings.Get("zh-TW").TryGetValue("fav_group", out var tw) ? tw : "我的收藏";
            return WebRemoteStrings.Get("zh-CN").TryGetValue("fav_group", out var cn) ? cn : "我的收藏";
        }

        private static string MakeBadgeHtml(string? badge, string lang = "zh-CN")
        {
            if (string.IsNullOrEmpty(badge)) return "";
            var t = WebRemoteStrings.Get(lang);
            string label = badge switch
            {
                "live" => t.TryGetValue("epg_current", out var v1) ? v1 : "正在播出",
                "replay" => t.TryGetValue("epg_replay_badge", out var v2) ? v2 : "回看",
                "reminder" => t.TryGetValue("epg_reminder_badge", out var v3) ? v3 : "预约",
                "next" => t.TryGetValue("epg_next", out var v4) ? v4 : "下一节目",
                _ => ""
            };
            if (string.IsNullOrEmpty(label)) return "";
            return $"<span class=\"epg-badge epg-badge-{badge}\">{label}</span>";
        }

        private static void TogglePlayPause()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_shell?.PlayerEngine != null)
                {
                    _shell.PlaybackActions.TryTogglePlayPause(_shell.PlayerEngine, _shell.IsPaused, out _);
                }
            });
        }

        private static void DoPlay()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_shell?.PlayerEngine != null)
                {
                    if (_shell.IsPaused)
                        _shell.PlaybackActions.TryTogglePlayPause(_shell.PlayerEngine, true, out _);
                    else if (_shell.CurrentChannel != null)
                        _shell.ChannelPlaybackActions?.PlayChannel(_shell.CurrentChannel);
                }
            });
        }

        private static void DoPause()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_shell?.PlayerEngine != null && !_shell.IsPaused)
                {
                    _shell.PlaybackActions.TryTogglePlayPause(_shell.PlayerEngine, false, out _);
                }
            });
        }

        private static void DoStop()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_shell?.PlayerEngine != null)
                {
                    _shell.PlaybackActions.TryStop(_shell.PlayerEngine);
                }
            });
        }

        private static WebRemoteStatus GetStatus()
        {
            if (_shell == null) return new WebRemoteStatus();

            var status = new WebRemoteStatus
            {
                Playing = !_shell.IsPaused && _shell.CurrentChannel != null,
                Volume = _shell.Volume,
                Muted = _shell.IsMuted,
                Speed = _shell.PlaybackSpeed,
                Mode = "Stopped",
                ModeText = "已停止"
            };

            if (_shell.CurrentChannel != null)
            {
                var cachedLogo = LogoCacheService.Instance.GetCachedPath(_shell.CurrentChannel.Logo ?? "");
                var logoUrl = !string.IsNullOrEmpty(cachedLogo) ? ("file:///" + cachedLogo.Replace("\\", "/")) : (_shell.CurrentChannel.Logo ?? "");
                status.Channel = new WebRemoteChannel
                {
                    Id = _shell.CurrentChannel.Id ?? _shell.CurrentChannel.TvgId ?? "",
                    Name = _shell.CurrentChannel.Name ?? "",
                    Logo = logoUrl
                };
            }

            // Determine playback mode
            Logger.Debug($"[WebRemote] Mode check: IsTimeshiftActive={_shell.IsTimeshiftActive}, TimeshiftCursorSec={_shell.TimeshiftCursorSec}, CurrentPlayingProgram={_shell.CurrentPlayingProgram?.Title}");
            if (_shell.IsTimeshiftActive && _shell.TimeshiftCursorSec > 0)
            {
                // Timeshift cursor > 0 means user has seeked, it's a true timeshift playback
                status.Mode = "Timeshift";
                status.ModeText = "时移";
            }
            else if (_shell.CurrentPlayingProgram != null)
            {
                var prog = _shell.CurrentPlayingProgram;
                // Check if it's a replay (current program has ended)
                if (prog.End != default && prog.End < DateTime.Now)
                {
                    status.Mode = "Replay";
                    status.ModeText = "回看";
                }
                else
                {
                    status.Mode = "Live";
                    status.ModeText = "直播";
                }
                status.CurrentProgram = new WebRemoteProgram
                {
                    Name = prog.Title ?? "",
                    Start = prog.Start.ToString("HH:mm"),
                    End = prog.End.ToString("HH:mm"),
                    IsCurrent = true
                };
            }
            else if (_shell.CurrentChannel != null)
            {
                status.Mode = "Live";
                status.ModeText = "直播";
                // Try to get current program from EPG service directly (may not be synced yet after WebRemote playback start)
                if (_shell.EpgService != null)
                {
                    var liveProg = _shell.EpgService.GetCurrentProgram(_shell.CurrentChannel.TvgId, _shell.CurrentChannel.TvgName, _shell.CurrentChannel.Name);
                    if (liveProg != null && !string.IsNullOrWhiteSpace(liveProg.Title))
                    {
                        status.CurrentProgram = new WebRemoteProgram
                        {
                            Name = liveProg.Title ?? "",
                            Start = liveProg.Start.ToString("HH:mm"),
                            End = liveProg.End.ToString("HH:mm"),
                            IsCurrent = true
                        };
                    }
                }
            }

            if (_shell.IsTimeshiftActive)
            {
                status.Timeshift = new WebRemoteTimeshift
                {
                    Active = true,
                    Cursor = _shell.TimeshiftCursorSec > 0 ? TimeSpan.FromSeconds(_shell.TimeshiftCursorSec).ToString(@"hh\:mm\:ss") : null,
                    Range = $"{_shell.TimeshiftMin:HH:mm} - {_shell.TimeshiftMax:HH:mm}"
                };
            }

            status.TimeshiftEnabled = AppSettings.Current.Timeshift?.Enabled ?? false;
            return status;
        }

        private static List<WebRemoteChannelGroup> GetChannels()
        {
            if (_shell == null) return new List<WebRemoteChannelGroup>();

            var groups = new List<WebRemoteChannelGroup>();
            var channelGroups = _shell.ChannelGroups?.ToList() ?? new List<ChannelGroupItem>();

            foreach (var group in channelGroups)
            {
                var channelGroup = new WebRemoteChannelGroup
                {
                    Name = group.Name ?? "",
                    Channels = group.Items?.Select(c => {
                        var prog = _shell.EpgService?.GetPrograms(c.Id ?? c.TvgId ?? "", c.TvgName, c.Name)?.FirstOrDefault(p => p.Start <= DateTime.Now && p.End > DateTime.Now);
                        // Use cached logo path if available
                        var cachedLogo = LogoCacheService.Instance.GetCachedPath(c.Logo ?? "");
                        var logoUrl = !string.IsNullOrEmpty(cachedLogo) ? ("file:///" + cachedLogo.Replace("\\", "/")) : (c.Logo ?? "");
                        if (group.Name == "央视频" || c.Name.Contains("央视"))
                        {
                            Logger.Debug($"[WebRemote] Logo for {c.Name}: original={c.Logo}, cached={cachedLogo}, url={logoUrl}");
                        }
                        return new WebRemoteChannel
                        {
                            Id = c.Id ?? c.TvgId ?? "",
                            Name = c.Name ?? "",
                            Logo = logoUrl,
                            CurrentProgram = prog?.Title,
                            CurrentTime = prog != null ? prog.Start.ToString("HH:mm") + "-" + prog.End.ToString("HH:mm") : ""
                        };
                    }).ToList() ?? new List<WebRemoteChannel>()
                };
                groups.Add(channelGroup);
            }

            // Add favorites as a special group
            var favorites = _shell.Favorites?.ToList() ?? new List<Channel>();
            if (favorites.Any())
            {
                var favGroup = new WebRemoteChannelGroup
                {
                    Name = GetLocalizedFavGroupName(),
                    IsFavorites = true,
                    Channels = favorites.Select(c => {
                        var prog = _shell.EpgService?.GetPrograms(c.Id ?? c.TvgId ?? "", c.TvgName, c.Name)?.FirstOrDefault(p => p.Start <= DateTime.Now && p.End > DateTime.Now);
                        var cachedLogo = LogoCacheService.Instance.GetCachedPath(c.Logo ?? "");
                        var logoUrl = !string.IsNullOrEmpty(cachedLogo) ? ("file:///" + cachedLogo.Replace("\\", "/")) : (c.Logo ?? "");
                        return new WebRemoteChannel
                        {
                            Id = c.Id ?? c.TvgId ?? "",
                            Name = c.Name ?? "",
                            Logo = logoUrl,
                            CurrentProgram = prog?.Title,
                            CurrentTime = prog != null ? prog.Start.ToString("HH:mm") + "-" + prog.End.ToString("HH:mm") : ""
                        };
                    }).ToList()
                };
                groups.Insert(0, favGroup);
            }

            // Publish the shared channel numbers, so the page shows exactly the number the local
            // channel-number overlay shows for a channel switched from the remote.
            var numbers = BuildChannelNumbers();
            foreach (var group in groups)
                foreach (var channel in group.Channels)
                    channel.Number = numbers.TryGetValue(channel.Id ?? "", out var number) ? number : 0;

            return groups;
        }

        /// <summary>1-based channel numbers (web channel id → number) shared by the remote page and the
        /// local channel-number overlay. The numbering follows the playlist group order and numbers the
        /// favourites last, which is the order the page renders as well.</summary>
        private static Dictionary<string, int> BuildChannelNumbers()
        {
            var groups = new List<IEnumerable<string?>>();
            if (_shell == null) return WebRemoteChannelNumbering.BuildNumberMap(groups);

            var channelGroups = _shell.ChannelGroups?.ToList() ?? new List<ChannelGroupItem>();
            foreach (var group in channelGroups)
                groups.Add((group.Items ?? new List<Channel>()).Select(c => c.Id ?? c.TvgId));

            var favorites = _shell.Favorites;
            if (favorites != null && favorites.Any())
                groups.Add(favorites.Select(c => c.Id ?? c.TvgId));

            return WebRemoteChannelNumbering.BuildNumberMap(groups);
        }

        private static List<WebRemoteProgram> GetEpg(string channelId, DateTime? filterDate = null)
        {
            if (_shell == null || _shell.EpgService == null || string.IsNullOrEmpty(channelId))
                return new List<WebRemoteProgram>();

            try
            {
                // Look up the channel to get tvgName and channelName for better EPG matching
                string? tvgName = null;
                string? channelName = null;
                var channels = _shell.Channels?.ToList() ?? new List<Channel>();
                var channel = channels.FirstOrDefault(c =>
                    c.Id == channelId || c.TvgId == channelId);
                if (channel != null)
                {
                    tvgName = channel.TvgName;
                    channelName = channel.Name;
                }

                var programs = _shell.EpgService.GetPrograms(channelId, tvgName, channelName);
                var now = DateTime.Now;

                // 判断当前频道是否在回看某个节目
                var replayTitle = (_shell.CurrentPlayingProgram != null &&
                    _shell.CurrentChannel != null &&
                    (_shell.CurrentChannel.Id == channelId || _shell.CurrentChannel.TvgId == channelId))
                    ? _shell.CurrentPlayingProgram.Title : null;
                var replayStart = _shell.CurrentPlayingProgram?.Start;
                var replayEnd = _shell.CurrentPlayingProgram?.End;

                // 检查预约列表中哪些节目被预约了（按节目时间匹配）
                var reminderTitles = new HashSet<string>();
                var reminders = AppSettings.Current.ScheduledReminders ?? new List<ScheduledReminder>();
                var channelObj = channels.FirstOrDefault(c => c.Id == channelId || c.TvgId == channelId);
                foreach (var r in reminders)
                {
                    if (r.ChannelId == channelId || r.ChannelId == channelObj?.Id || r.ChannelId == channelObj?.TvgId)
                    {
                        // 匹配节目名且时间相近（前后5分钟容差）
                        foreach (var p2 in programs)
                        {
                            if (r.Note == p2.Title &&
                                Math.Abs((r.StartAtUtc - p2.Start).TotalMinutes) < 5)
                            {
                                reminderTitles.Add(p2.Title);
                            }
                        }
                    }
                }

                // 找出正在播出和下一个节目的索引
                int currentIdx = -1, nextIdx = -1;
                for (int i = 0; i < programs.Count; i++)
                {
                    if (currentIdx < 0 && programs[i].Start <= now && programs[i].End > now)
                        currentIdx = i;
                    else if (currentIdx >= 0 && nextIdx < 0 && programs[i].Start > now)
                        nextIdx = i;
                }

                // 过滤策略：使用6 AM边界的TV风格日期
                // 优先级：filterDate > replay日期 > 今天+明天
                var maxItems = AppSettings.Current.WebRemote.MaxEpgItems;
                int filterStart, filterEnd;

                DateTime windowStart, windowEnd;

                if (filterDate.HasValue)
                {
                    // 用户选择特定日期：00:00 到 23:59:59
                    windowStart = filterDate.Value.Date;
                    windowEnd = windowStart.AddDays(1);
                    Logger.Debug($"[WebRemote] Using filterDate: {filterDate.Value:yyyy-MM-dd}, window={windowStart:yyyy-MM-dd HH:mm}-{windowEnd:yyyy-MM-dd HH:mm}");
                }
                else if (replayTitle != null && _shell.CurrentPlayingProgram != null)
                {
                    // 有回看：使用回看节目的日期（完整24小时）
                    var currentProg = _shell.CurrentPlayingProgram;
                    windowStart = currentProg.Start.Date;
                    windowEnd = windowStart.AddDays(1);
                    Logger.Debug($"[WebRemote] Replay date: {currentProg.Start:yyyy-MM-dd HH:mm}, window={windowStart:yyyy-MM-dd HH:mm}-{windowEnd:yyyy-MM-dd HH:mm}");
                }
                else
                {
                    // 默认：显示今天0点到明天0点的节目
                    var today = DateTime.Today;
                    windowStart = today;
                    windowEnd = today.AddDays(1);
                    Logger.Debug($"[WebRemote] Default window: {windowStart:yyyy-MM-dd HH:mm}-{windowEnd:yyyy-MM-dd HH:mm}");
                }

                filterStart = 0; filterEnd = programs.Count;
                for (int i = 0; i < programs.Count; i++)
                {
                    if (programs[i].End < windowStart) filterStart = i + 1;
                    if (programs[i].Start >= windowEnd) { filterEnd = i; break; }
                }

                var filteredPrograms = programs.Skip(filterStart).Take(filterEnd - filterStart).ToList();

                // 当用户选择特定日期时，显示所有节目；否则只显示当天的节目
                if (!filterDate.HasValue)
                {
                    // 默认视图：严格只显示当天的节目（0点到24点）
                    Logger.Debug($"[WebRemote] Default view: showing only today's {filteredPrograms.Count} programs");
                }

                // 重新计算索引（相对于filteredPrograms）
                currentIdx = -1; nextIdx = -1;
                for (int i = 0; i < filteredPrograms.Count; i++)
                {
                    if (currentIdx < 0 && filteredPrograms[i].Start <= now && filteredPrograms[i].End > now)
                        currentIdx = i;
                    else if (currentIdx >= 0 && nextIdx < 0 && filteredPrograms[i].Start > now)
                        nextIdx = i;
                }

                // 选择要返回的节目（只限制最多maxItems）
                var resultPrograms = filteredPrograms.Take(maxItems).ToList();

                Logger.Info($"[WebRemote] GetEpg: filterDate={filterDate}, window={windowStart:yyyy-MM-dd HH:mm}-{windowEnd:yyyy-MM-dd HH:mm}, resultPrograms={resultPrograms.Count}, filteredPrograms={filteredPrograms.Count}");

                return resultPrograms.Select(p =>
                {
                    string? badgeType =
                        (replayTitle != null && p.Title == replayTitle && replayStart.HasValue && replayEnd.HasValue && p.Start == replayStart.Value && p.End == replayEnd.Value) ? "replay" :
                        (reminderTitles.Contains(p.Title)) ? "reminder" :
                        (p.Start <= now && p.End > now ? "live" : null);
                    string? badgeHtml = null;
                    if (!string.IsNullOrEmpty(badgeType)) {
                        var t = WebRemoteStrings.Get(AppSettings.Current.Language);
                        string label = badgeType switch
                        {
                            "live" => t.TryGetValue("epg_current", out var v1) ? v1 : "正在播出",
                            "replay" => t.TryGetValue("epg_replay_badge", out var v2) ? v2 : "回看",
                            "reminder" => t.TryGetValue("epg_reminder_badge", out var v3) ? v3 : "预约",
                            _ => ""
                        };
                        badgeHtml = $"<span class=\"epg-badge epg-badge-{badgeType}\">{label}</span>";
                    }
                    return new WebRemoteProgram
                    {
                        Name = p.Title ?? "",
                        Start = p.Start.ToString("HH:mm"),
                        End = p.End.ToString("HH:mm"),
                        StartISO = p.Start.ToString("yyyy-MM-ddTHH:mm:ss"),
                        EndISO = p.End.ToString("yyyy-MM-ddTHH:mm:ss"),
                        Date = p.Start.ToString("MM-dd"),
                        IsCurrent = p.Start <= now && p.End > now,
                        // Replay only works on channels with a catch-up source or timeshift url
                        CanReplay = channelObj?.HasTimeshift == true,
                        Badge = badgeType,
                        BadgeHtml = badgeHtml
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] GetEpg error: {ex.Message}");
                return new List<WebRemoteProgram>();
            }
        }

        private static void ChangeChannel(string channelId)
        {
            if (_shell == null || string.IsNullOrEmpty(channelId)) return;

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    var channels = _shell.Channels?.ToList() ?? new List<Channel>();
                    var channel = channels.FirstOrDefault(c =>
                        c.Id == channelId || c.TvgId == channelId);
                    if (channel != null)
                    {
                        Logger.Debug($"[WebRemote] ChangeChannel via web remote: {channel.Name}");
                        _shell.ChannelPlaybackActions?.PlayChannel(channel);
                        // The channel-number badge is a remote-only affordance: mouse driven switches
                        // take a different path and never reach this method.
                        ShowChannelNumberOverlay(channelId);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WebRemote] ChangeChannel error: {ex.Message}");
                }
            });
        }

        /// <summary>Flashes the channel number over the player video area for a channel switched from the
        /// remote page (can be turned off in the web remote settings).</summary>
        private static void ShowChannelNumberOverlay(string channelId)
        {
            try
            {
                if (AppSettings.Current.WebRemote?.ShowChannelNumber == false) return;
                if (!BuildChannelNumbers().TryGetValue(channelId, out var number) || number <= 0) return;
                _shell?.WindowStateActions?.ShowChannelNumberOverlay(number);
            }
            catch (Exception ex)
            {
                Logger.Debug($"[WebRemote] channel number overlay failed: {ex.Message}");
            }
        }

        private static void ExitApp()
        {
            try
            {
                Logger.Debug("[WebRemote] Exit requested via web remote");
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current?.Shutdown();
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] ExitApp error: {ex.Message}");
            }
        }

        private static Action<bool>? _toggleFullscreenCallback;

        public static void SetToggleFullscreenCallback(Action<bool> callback)
        {
            _toggleFullscreenCallback = callback;
        }

        private static void ToggleFullscreen()
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_shell == null) return;
                    var isCurrentlyFullscreen = _shell.WindowStateActions?.IsFullscreen ?? false;
                    Logger.Debug($"[WebRemote] Fullscreen toggled via web remote (currently: {(isCurrentlyFullscreen ? "fullscreen" : "windowed")})");
                    _toggleFullscreenCallback?.Invoke(!isCurrentlyFullscreen);
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] ToggleFullscreen error: {ex.Message}");
            }
        }

        private static void DoSwitchSource()
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_shell?.MenuActions == null) return;
                    _shell.MenuActions.SwitchSourceViaRemote();
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"[WebRemote] DoSwitchSource error: {ex.Message}");
            }
        }

        // Source management
        private static List<WebRemoteSource> GetSources()
        {
            var list = AppSettings.Current.SavedSources ?? new List<M3uSource>();
            return list.Select(s => new WebRemoteSource { Name = s.Name, Url = s.Url, IsSelected = s.IsSelected }).ToList();
        }

        private static void SelectSource(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (key == "__prev__") { _shell?.MenuActions.SwitchSourceCycle(false); return; }
                if (key == "__next__") { _shell?.MenuActions.SwitchSourceCycle(true); return; }
                var src = AppSettings.Current.SavedSources?.FirstOrDefault(s =>
                    s.Name == key || s.Url == key);
                if (src != null)
                {
                    _shell?.MenuActions.LoadM3u(src);
                    return;
                }

                // Not a saved playlist: it may be one of the current channel's streams, which is what a
                // tapped voice candidate for "换到线路二" carries.
                SelectVoiceStream(key);
            });
        }

        private static void AddSource(string name, string url)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (AppSettings.Current.SavedSources == null)
                    AppSettings.Current.SavedSources = new List<M3uSource>();
                if (!AppSettings.Current.SavedSources.Any(s => s.Url == url))
                {
                    AppSettings.Current.SavedSources.Add(new M3uSource { Name = name, Url = url, IsSelected = false });
                    AppSettings.Current.Save();
                }
            });
        }

        private static void RemoveSource(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                AppSettings.Current.SavedSources?.RemoveAll(s => s.Name == key || s.Url == key);
                AppSettings.Current.Save();
            });
        }

        // Playback control
        private static void DoSeek(double seconds)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _shell?.ShortcutActions.SeekBySeconds((int)seconds);
            });
        }

        private static void DoSetSpeed(double speed)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_shell != null) _shell.PlaybackSpeed = speed;
            });
        }

        private static void DoSetTimeshift(bool enabled)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_shell == null) return;
                _shell.IsTimeshiftActive = enabled;
            });
        }

        private static void DoReplayProgram(string channelId, string programTitle, string start, string end)
        {
            if (string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(start)) return;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    var channels = _shell?.Channels?.ToList() ?? new List<Channel>();
                    var channel = channels.FirstOrDefault(c =>
                        c.Id == channelId || c.TvgId == channelId);
                    if (channel == null)
                    {
                        Logger.Error($"[WebRemote] DoReplayProgram: channel not found id={channelId}, available={string.Join(",", channels.Select(c => c.Id + "/" + c.TvgId).Take(5))}");
                        return;
                    }
                    var startTime = DateTime.Parse(start);
                    Logger.Info($"[WebRemote] DoReplayProgram: input='{start}', parsed={startTime:yyyy-MM-dd HH:mm:ss} Kind={startTime.Kind}, ToUniversalTime={startTime.ToUniversalTime():yyyy-MM-dd HH:mm:ss}");
                    Logger.Info($"[WebRemote] DoReplayProgram: {channel.Name} at {startTime}, CatchupSource={channel.CatchupSource}, Timeshift.Enabled={AppSettings.Current.Timeshift?.Enabled}, Timeshift.UrlFormat={AppSettings.Current.Timeshift?.UrlFormat}");
                    _shell?.ChannelPlaybackActions.PlayCatchupAt(channel, startTime, keepTimeshiftMode: _shell.IsTimeshiftActive);
                    Logger.Debug($"[WebRemote] DoReplayProgram success: {channel.Name} at {startTime}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WebRemote] DoReplayProgram error: {ex.Message}");
                }
            });
        }

        // Voice control
        /// <summary>
        /// Runs one spoken sentence or one direct action through the shared router. The router owns the
        /// language work; this method is the only place that knows how the shell's data maps onto it and
        /// that marshals a resulting callback onto the UI thread.
        /// </summary>
        private static VoiceCommandReply VoiceCommand(VoiceCommandContext context, string action, string target)
        {
            if (context == null) return new VoiceCommandReply { Spoken = "" };
            context.Channels = GetVoiceChannels;
            context.Playlists = GetVoicePlaylists;
            context.CurrentStreams = GetVoiceStreams;
            context.CurrentChannel = GetVoiceCurrentChannel;
            context.Volume = () => _shell?.Volume ?? 0;
            context.IsMuted = () => _shell?.IsMuted ?? false;
            context.EpgNow = GetVoiceNow;
            context.CatchupPrograms = GetVoiceCatchup;
            context.UserRules = GetVoiceRules;
            var voice = AppSettings.Current.WebRemote?.Voice;
            if (voice != null)
            {
                context.VolumeStep = voice.VolumeStep;
                context.AutoReplaySingle = voice.AutoReplaySingle;
                if (string.IsNullOrWhiteSpace(context.Lang) && !string.IsNullOrWhiteSpace(voice.Lang)) context.Lang = voice.Lang;
            }

            context.OnSwitchChannel = ChangeChannel;
            context.OnSelectedStream = SelectVoiceStream;
            context.OnNextStream = StepVoiceStream;
            context.OnLoadPlaylist = SelectSource;
            context.OnReplayProgram = DoReplayProgram;
            context.OnVolume = v => OnUi(() =>
            {
                if (_shell == null) return;
                _shell.Volume = v;
                _shell.IsMuted = v <= 0;
            });
            context.OnToggleMute = () => OnUi(() =>
            {
                if (_shell != null) _shell.IsMuted = !_shell.IsMuted;
            });
            context.OnSeek = DoSeek;
            context.OnPlay = DoPlay;
            context.OnPause = DoPause;
            context.OnStop = DoStop;
            context.OnFullscreen = ToggleFullscreen;
            context.OnExit = ExitApp;

            return string.IsNullOrWhiteSpace(action)
                ? VoiceCommandRouter.Route(context)
                : VoiceCommandRouter.Execute(context, action, target);
        }

        /// <summary>Runs <paramref name="work"/> on the dispatcher; tests and the router call this without a WPF app.</summary>
        private static void OnUi(Action work)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) work();
            else dispatcher.Invoke(work);
        }

        /// <summary>Every channel with its remote number, in the same order the page shows them.</summary>
        private static IReadOnlyList<VoiceChannel> GetVoiceChannels()
        {
            var result = new List<VoiceChannel>();
            var numbers = BuildChannelNumbers();
            if (_shell == null) return result;

            foreach (var group in _shell.ChannelGroups?.ToList() ?? new List<ChannelGroupItem>())
                foreach (var channel in group.Items ?? new List<Channel>())
                    result.Add(ToVoiceChannel(channel, group.Name ?? "", numbers));

            // Favourites are numbered last and are legitimately playable, so voice may pick them too.
            foreach (var channel in _shell.Favorites ?? new List<Channel>())
                result.Add(ToVoiceChannel(channel, GetLocalizedFavGroupName(), numbers));

            return result;
        }

        private static VoiceChannel ToVoiceChannel(Channel channel, string group, Dictionary<string, int> numbers)
        {
            var id = channel.Id ?? channel.TvgId ?? "";
            return new VoiceChannel
            {
                Id = id,
                Name = channel.Name ?? "",
                Group = group,
                Number = !string.IsNullOrEmpty(id) && numbers.TryGetValue(id, out var number) ? number : 0
            };
        }

        private static VoiceChannel? GetVoiceCurrentChannel()
        {
            var channel = _shell?.CurrentChannel;
            if (channel == null) return null;
            var id = channel.Id ?? channel.TvgId ?? "";
            return new VoiceChannel
            {
                Id = id,
                Name = channel.Name ?? "",
                Group = "",
                Number = BuildChannelNumbers().TryGetValue(id, out var number) ? number : 0
            };
        }

        /// <summary>Stream variants of the current channel: this is what "换个源" walks through.</summary>
        private static IReadOnlyList<VoiceStream> GetVoiceStreams()
        {
            var result = new List<VoiceStream>();
            if (_shell?.CurrentSources == null) return result;

            var sources = _shell.CurrentSources;
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                var url = source.Url ?? "";
                result.Add(new VoiceStream
                {
                    Id = url,
                    Name = !string.IsNullOrWhiteSpace(source.Name) ? source.Name : $"{i + 1}/{sources.Count}",
                    Extra = HostOf(url)
                });
            }
            return result;
        }

        /// <summary>Switches the current channel onto one of its streams by name or url.</summary>
        private static void SelectVoiceStream(string key)
        {
            if (string.IsNullOrEmpty(key) || _shell == null) return;
            OnUi(() =>
            {
                try
                {
                    var sources = _shell.CurrentSources;
                    if (sources == null) return;
                    var match = sources.FirstOrDefault(s =>
                        string.Equals(s.Url, key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
                    if (match == null) return;
                    Logger.Debug($"[WebRemote] Voice selected stream {match.Name}");
                    _shell.MenuActions.SwitchSourceTo(match);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WebRemote] SelectVoiceStream error: {ex.Message}");
                }
            });
        }

        /// <summary>Steps to the next (+1) or previous (-1) stream, wrapping around.</summary>
        private static void StepVoiceStream(int delta)
        {
            if (delta == 0 || _shell == null) return;
            OnUi(() =>
            {
                try
                {
                    var sources = _shell.CurrentSources;
                    if (sources == null || sources.Count < 2) return;

                    // The playing variant is identified by its url, the same way the menu cycle does it.
                    var currentUrl = _shell.SourceLoader.SanitizeUrl(_shell.CurrentUrl ?? "");
                    var index = sources.FindIndex(s => _shell.SourceLoader.SanitizeUrl(s.Url ?? "") == currentUrl);
                    var next = ((index + delta) % sources.Count + sources.Count) % sources.Count;
                    Logger.Debug($"[WebRemote] Voice stream step {delta}: {index + 1} -> {next + 1}/{sources.Count}");
                    _shell.MenuActions.SwitchSourceTo(sources[next]);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WebRemote] StepVoiceStream error: {ex.Message}");
                }
            });
        }

        private static IReadOnlyList<VoicePlaylist> GetVoicePlaylists()
        {
            var list = AppSettings.Current.SavedSources ?? new List<M3uSource>();
            return list.Select(s => new VoicePlaylist
            {
                Id = s.Url ?? "",
                Name = s.Name ?? "",
                Extra = HostOf(s.Url)
            }).ToList();
        }

        private static IReadOnlyList<VoiceRule> GetVoiceRules()
            => AppSettings.Current.WebRemote?.Voice?.CustomRules ?? new List<VoiceRule>();

        /// <summary>What a channel is showing now, resolved through the same channel lookup the EPG uses.</summary>
        private static VoiceProgram? GetVoiceNow(string channelId)
        {
            if (_shell?.EpgService == null || string.IsNullOrEmpty(channelId)) return null;
            try
            {
                var channel = FindChannel(channelId);
                var program = _shell.EpgService.GetCurrentProgram(channelId, channel?.TvgName, channel?.Name);
                if (program == null) return null;
                return new VoiceProgram
                {
                    Title = program.Title ?? "",
                    Start = program.Start,
                    End = program.End,
                    ChannelName = channel?.Name ?? ""
                };
            }
            catch (Exception ex)
            {
                Logger.Debug($"[WebRemote] voice EPG now failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Programmes of one channel inside a catch-up window, for "回看昨天的新闻联播".</summary>
        private static IReadOnlyList<VoiceProgram> GetVoiceCatchup(string channelId, DateTime start, DateTime end)
        {
            if (_shell?.EpgService == null || string.IsNullOrEmpty(channelId)) return new List<VoiceProgram>();
            try
            {
                var channel = FindChannel(channelId);
                var programs = _shell.EpgService.GetPrograms(channelId, channel?.TvgName, channel?.Name);
                if (programs == null) return new List<VoiceProgram>();
                return programs
                    .Where(p => p.End > start && p.Start < end)
                    .Select(p => new VoiceProgram
                    {
                        Title = p.Title ?? "",
                        Start = p.Start,
                        End = p.End,
                        ChannelName = channel?.Name ?? ""
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Debug($"[WebRemote] voice catch-up lookup failed: {ex.Message}");
                return new List<VoiceProgram>();
            }
        }

        private static Channel? FindChannel(string channelId)
            => (_shell?.Channels ?? Enumerable.Empty<Channel>())
                .FirstOrDefault(c => c.Id == channelId || c.TvgId == channelId);

        /// <summary>Host part of a stream url, shown as the secondary line of a spoken source pick.</summary>
        private static string HostOf(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            return Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : "";
        }

        /// <summary>Reuses the configured web remote password, or mints a stable random token once.</summary>
        private static string EnsureVoiceToken()
        {
            var config = AppSettings.Current.WebRemote;
            if (config == null) return "";
            if (!string.IsNullOrEmpty(config.Voice?.Token)) return config.Voice!.Token!;
            if (config.RequirePassword && !string.IsNullOrEmpty(config.Password)) return config.Password;

            var token = Guid.NewGuid().ToString("N")[..12];
            config.Voice ??= new VoiceConfig();
            config.Voice.Token = token;
            try { AppSettings.Current.Save(); }
            catch (Exception ex) { Logger.Debug($"[WebRemote] voice token save failed: {ex.Message}"); }
            return token;
        }

        // Reminder management
        private static List<WebRemoteReminder> GetReminders()
        {
            var reminders = AppSettings.Current.ScheduledReminders ?? new List<ScheduledReminder>();
            return reminders.Select(r => new WebRemoteReminder
            {
                Id = r.Id,
                ChannelId = r.ChannelId ?? "",
                ChannelName = r.ChannelName ?? "",
                ProgramTitle = r.Note ?? "",
                StartAt = r.StartAtUtc.ToString("o"),
                EndTime = r.EndTimeUtc.ToString("o"),
                Action = r.Action ?? "notify",
                Enabled = r.Enabled,
                Completed = r.Completed,
                RecordDurationMin = r.RecordDurationMin
            }).ToList();
        }

        private static string AddReminder(string channelId, string startAt, string endTime, string action, string programTitle, int preAlertSeconds, int? recordDurationMin)
        {
            if (string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(startAt)) return "";
            try
            {
                var reminder = new ScheduledReminder
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ChannelId = channelId,
                    ChannelName = "",
                    Note = programTitle,
                    StartAtUtc = DateTime.Parse(startAt).ToUniversalTime(),
                    EndTimeUtc = string.IsNullOrEmpty(endTime) ? DateTime.Parse(startAt).ToUniversalTime().AddHours(1) : DateTime.Parse(endTime).ToUniversalTime(),
                    Action = action,
                    PreAlertSeconds = preAlertSeconds,
                    RecordDurationMin = recordDurationMin,
                    Enabled = true,
                    Completed = false
                };
                ReminderService.Instance.Import(new[] { reminder });
                return reminder.Id;
            }
            catch { return ""; }
        }

        private static void CancelReminder(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var list = AppSettings.Current.ScheduledReminders;
                var r = list?.FirstOrDefault(x => x.Id == id);
                if (r != null) { r.Completed = true; AppSettings.Current.Save(); }
            });
        }

        // Recording management
        private static List<WebRemoteRecording> GetRecordings()
        {
            return ScheduledRecordingManager.Instance.GetAll().Select(r => new WebRemoteRecording
            {
                Id = r.Id,
                ChannelName = r.ChannelName ?? "",
                ProgramTitle = r.ProgramTitle ?? "",
                Type = r.Type.ToString(),
                Status = r.Status.ToString(),
                StatusLabel = r.StatusLabel ?? "",
                ScheduledStart = r.ScheduledStart.ToString("o"),
                ScheduledEnd = r.ScheduledEnd.ToString("o"),
                SizeLabel = r.SizeLabel ?? "",
                FilePath = r.FilePath
            }).ToList();
        }

        private static void StopRecording(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            ScheduledRecordingManager.Instance.StopRecording(id);
        }

        private static void DeleteRecording(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            ScheduledRecordingManager.Instance.RemoveCompleted(id);
        }
}
}
