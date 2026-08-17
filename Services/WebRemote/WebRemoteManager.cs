using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services.WebRemote
{
    public static class WebRemoteManager
    {
        private static WebRemoteServer? _server;
        private static MainShellViewModel? _shell;

        public static bool IsRunning => _server?.IsRunning ?? false;

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

                _server.Start(config.HttpPort, config.RequirePassword, config.Password);
                Logger.Debug($"[WebRemote] Manager initialized on port {config.HttpPort}");
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
            if (config.Enabled && !IsRunning)
            {
                Initialize(_shell);
            }
            else if (!config.Enabled && IsRunning)
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
                status.Channel = new WebRemoteChannel
                {
                    Id = _shell.CurrentChannel.Id ?? _shell.CurrentChannel.TvgId ?? "",
                    Name = _shell.CurrentChannel.Name ?? "",
                    Logo = _shell.CurrentChannel.Logo
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
                    var liveProg = _shell.EpgService.GetCurrentProgram(_shell.CurrentChannel.TvgId, _shell.CurrentChannel.Name);
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
                        var prog = _shell.EpgService?.GetPrograms(c.Id ?? c.TvgId ?? "", c.Name, c.Name)?.FirstOrDefault(p => p.Start <= DateTime.Now && p.End > DateTime.Now);
                        return new WebRemoteChannel
                        {
                            Id = c.Id ?? c.TvgId ?? "",
                            Name = c.Name ?? "",
                            Logo = c.Logo,
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
                    Channels = favorites.Select(c => {
                        var prog = _shell.EpgService?.GetPrograms(c.Id ?? c.TvgId ?? "", c.Name, c.Name)?.FirstOrDefault(p => p.Start <= DateTime.Now && p.End > DateTime.Now);
                        return new WebRemoteChannel
                        {
                            Id = c.Id ?? c.TvgId ?? "",
                            Name = c.Name ?? "",
                            Logo = c.Logo,
                            CurrentProgram = prog?.Title,
                            CurrentTime = prog != null ? prog.Start.ToString("HH:mm") + "-" + prog.End.ToString("HH:mm") : ""
                        };
                    }).ToList()
                };
                groups.Insert(0, favGroup);
            }

            return groups;
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
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WebRemote] ChangeChannel error: {ex.Message}");
                }
            });
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
                if (src != null) _shell?.MenuActions.LoadM3u(src);
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
                _shell?.PlaybackActions.TrySeekRelative(_shell.PlayerEngine, (int)seconds);
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
                    _shell?.ChannelPlaybackActions.PlayCatchupAt(channel, startTime);
                    Logger.Debug($"[WebRemote] DoReplayProgram success: {channel.Name} at {startTime}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WebRemote] DoReplayProgram error: {ex.Message}");
                }
            });
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
