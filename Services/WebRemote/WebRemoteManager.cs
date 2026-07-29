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

        private static string MakeBadgeHtml(string? badge)
        {
            if (string.IsNullOrEmpty(badge)) return "";
            return badge switch
            {
                "live" => "<span class=\"epg-badge epg-badge-current\">正在播出</span>",
                "replay" => "<span class=\"epg-badge epg-badge-replay\">回看</span>",
                "reminder" => "<span class=\"epg-badge epg-badge-reminder\">预约</span>",
                "next" => "<span class=\"epg-badge epg-badge-next\">下一节目</span>",
                _ => ""
            };
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
            if (_shell.IsTimeshiftActive)
            {
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
                    Channels = group.Items?.Select(c => new WebRemoteChannel
                    {
                        Id = c.Id ?? c.TvgId ?? "",
                        Name = c.Name ?? "",
                        Logo = c.Logo
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
                    Channels = favorites.Select(c => new WebRemoteChannel
                    {
                        Id = c.Id ?? c.TvgId ?? "",
                        Name = c.Name ?? "",
                        Logo = c.Logo
                    }).ToList()
                };
                groups.Insert(0, favGroup);
            }

            return groups;
        }

        private static List<WebRemoteProgram> GetEpg(string channelId)
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
                            if (r.ChannelName == p2.Title &&
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

                return programs.Take(AppSettings.Current.WebRemote.MaxEpgItems).Select((p, idx) => new WebRemoteProgram
                {
                    Name = p.Title ?? "",
                    Start = p.Start.ToString("HH:mm"),
                    End = p.End.ToString("HH:mm"),
                    IsCurrent = p.Start <= now && p.End > now,
                    Badge =
                        (replayTitle != null && p.Title == replayTitle) ? "replay" :
                        (reminderTitles.Contains(p.Title)) ? "reminder" :
                        (idx == currentIdx ? "live" : (idx == nextIdx ? "next" : null)),
                    BadgeHtml = MakeBadgeHtml(
                        (replayTitle != null && p.Title == replayTitle) ? "replay" :
                        (reminderTitles.Contains(p.Title)) ? "reminder" :
                        (idx == currentIdx ? "live" : (idx == nextIdx ? "next" : null)))
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
}
}
