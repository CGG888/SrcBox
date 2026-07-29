using System;
using System.Linq;
using System.Windows;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowPlaybackTickManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;
        private readonly MainWindowOverlayManager _overlayManager;

        public MainWindowPlaybackTickManager(MainWindow window, MainShellViewModel shell, MainWindowOverlayManager overlayManager)
        {
            _window = window;
            _shell = shell;
            _overlayManager = overlayManager;
        }

        public void Timer_Tick(object? sender, EventArgs e)
        {
            if (_window.PlayerInterop == null) return;
            try
            {
                try
                {
                    var w1 = _window.PlayerInterop.GetInt("width") ?? 0;
                    var h1 = _window.PlayerInterop.GetInt("height") ?? 0;
                    var fps1 = _window.PlayerInterop.GetDouble("estimated-vf-fps") ?? _window.PlayerInterop.GetDouble("fps") ?? 0;
                    if (!_window.FirstFrameLoggedForManager && w1 > 0 && h1 > 0)
                    {
                        Logger.Info("首帧出现");
                        _window.FirstFrameLoggedForManager = true;
                    }
                    if (!_window.FirstFrameLoggedForManager)
                    {
                        var waited = (DateTime.Now - _window.PlayStartTimeForManager).TotalSeconds;
                        if (waited > Math.Max(3, AppSettings.Current.SourceTimeoutSec))
                        {
                            Logger.Warn($"[Playback] 起播{waited:0.#}s未见画面 time-pos={_window.PlayerInterop.GetTimePos() ?? 0:0.##} URL={_shell.CurrentUrl}");
                            _window.FirstFrameLoggedForManager = true;
                        }
                    }
                }
                catch { }

                var tpos = _window.PlayerInterop.GetTimePos();
                var dur = _window.PlayerInterop.GetDuration();
                _shell.HandlePlaybackTick(tpos, dur, _window.FirstFrameLoggedForManager);
            }
            catch { }
        }

        public void OnTick(object? sender, EventArgs e)
        {
            if (_shell.IsTimeshiftActive)
            {
                try
                {
                    if (AppSettings.Current.Epg.StrictMatchByPlaybackTime && _window.EpgPanel.Visibility == Visibility.Visible && _shell.CurrentChannel != null)
                    {
                        var t = _shell.GetPlaybackLocalTime();
                        if (t.HasValue)
                        {
                            var suppress = _window.EpgManagerForManager?.IsAutoScrollSuppressed ?? false;
                            var seekedForUi = _shell.ConsumeTimeshiftSeekedForUi();
                            var allowAutoScroll = !suppress && seekedForUi;
                            var forceScroll = allowAutoScroll;
                            _window.EpgManagerForManager?.RefreshEpgListWithFocus(_shell.CurrentChannel, t.Value, forceScroll, allowAutoScroll);
                        }
                    }
                }
                catch { }
                return;
            }
            if (_window.PlayerInterop == null) return;
            var player = _shell.PlayerEngine;
            if (player == null) return;
            var tpos = player.GetTimePos();
            if (tpos.HasValue && tpos.Value > 0) _shell.ChannelPlaybackActions.CheckPlaybackStarted(tpos.Value);

            try
            {
                if (_shell.PlaybackMode == PlaybackMode.Live &&
                    _shell.CurrentPlayingProgram != null &&
                    AppSettings.Current.Epg.StrictMatchByPlaybackTime &&
                    _window.EpgPanel.Visibility == Visibility.Visible &&
                    _shell.CurrentChannel != null)
                {
                    var tt = _shell.GetPlaybackLocalTime();
                    if (tt.HasValue)
                    {
                        var suppress = _window.EpgManagerForManager?.IsAutoScrollSuppressed ?? false;
                        if (!suppress)
                        {
                            _window.EpgManagerForManager?.RefreshEpgListWithFocus(_shell.CurrentChannel, tt.Value, false, true);
                        }
                    }
                }
            }
            catch { }

            try
            {
                _shell.MediaInfo.Update(player);
                if (_overlayManager.OverlayWpf != null)
                {
                    _overlayManager.OverlayWpf.SetInfo(_shell.MediaInfo.InfoText);
                    _overlayManager.OverlayWpf.SetTags(_shell.MediaInfo.Tags.ToList());
                }
            }
            catch { }
        }
    }
}
