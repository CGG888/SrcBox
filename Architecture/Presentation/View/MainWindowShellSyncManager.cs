using System;
using System.ComponentModel;
using System.Windows;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Services;
using LibmpvIptvClient.Helpers;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowShellSyncManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;
        private readonly MainWindowOverlayManager _overlayManager;

        public MainWindowShellSyncManager(MainWindow window, MainShellViewModel shell, MainWindowOverlayManager overlayManager)
        {
            _window = window;
            _shell = shell;
            _overlayManager = overlayManager;
        }

        public void OnShellPropertyChanged(PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainShellViewModel.SeekValue))
            {
                if (_shell.IsSeeking)
                {
                    if (_shell.IsTimeshiftActive)
                    {
                        var t = _shell.TimeshiftMin.AddSeconds(Math.Max(0, _shell.SeekValue));
                        _shell.ElapsedTimeText = t.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    else
                    {
                        var ts = TimeSpan.FromSeconds(Math.Max(0, _shell.SeekValue));
                        _shell.ElapsedTimeText = ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
                    }
                }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.Volume))
            {
                try { _overlayManager.OverlayWpf?.SetVolume(_shell.Volume); } catch { }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.IsMuted))
            {
                try { _overlayManager.OverlayWpf?.SetMuted(_shell.IsMuted); } catch { }
                try { _window.UpdateMuteIcon(_shell.IsMuted); } catch { }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.IsDrawerCollapsed))
            {
                var collapsed = _shell.IsDrawerCollapsed;
                try { _overlayManager.OverlayWpf?.SetDrawerVisible(!collapsed); } catch { }
                try { _window.CbDrawer.IsChecked = !collapsed; } catch { }
                if (collapsed)
                {
                    if (_shell.WindowStateActions.FullscreenDrawer != null) _shell.WindowStateActions.FullscreenDrawer.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (_shell.WindowStateActions.FullscreenDrawer != null) _shell.WindowStateActions.FullscreenDrawer.Visibility = Visibility.Visible;
                    if (_shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.FullscreenDrawer == null) _window.ShowFullscreenDrawer();
                }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.IsTimeshiftActive))
            {
                var on = _shell.IsTimeshiftActive;
                try { _overlayManager.OverlayWpf?.SetTimeshift(on); } catch { }
                if (!on) _shell.ClearRecordingPlayingIndicator();
                if (!on)
                {
                    try
                    {
                        var timePos = _window.PlayerInterop?.GetTimePos();
                        var duration = _window.PlayerInterop?.GetDuration();
                        _shell.HandlePlaybackTick(timePos, duration, false);
                    }
                    catch { }
                }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.IsSpeedEnabled))
            {
                try { _overlayManager.OverlayWpf?.SetSpeedEnabled(_shell.IsSpeedEnabled); } catch { }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.PlaybackMode))
            {
                if (_shell.PlaybackMode == PlaybackMode.Stopped) return;
                if (_shell.PlaybackMode == PlaybackMode.RecordingPlayback &&
                    _shell.PlaybackFocusTime == null &&
                    _shell.CurrentPlayingProgram == null)
                {
                    return;
                }
                _window.UpdatePlayPauseIconFromManager();
                try
                {
                    if (_window.EpgPanel.Visibility == Visibility.Visible && _shell.CurrentChannel != null)
                    {
                        var playbackLocal = _shell.GetPlaybackLocalTime();
                        var programStart = _shell.CurrentPlayingProgram?.Start;
                        var focus = _shell.PlaybackMode switch
                        {
                            PlaybackMode.Timeshift => _shell.PlaybackFocusTime ?? playbackLocal ?? programStart ?? _shell.CurrentEpgDate,
                            PlaybackMode.Replay => _shell.PlaybackFocusTime ?? programStart ?? playbackLocal ?? _shell.CurrentEpgDate,
                            PlaybackMode.RecordingPlayback => _shell.PlaybackFocusTime ?? programStart ?? playbackLocal ?? _shell.CurrentEpgDate,
                            _ => DateTime.Now
                        };
                        if (focus == default) focus = DateTime.Now;
                        _window.EpgManagerForManager?.ResetAutoScrollSuppression();
                        _window.EpgManagerForManager?.RefreshEpgListWithFocus(_shell.CurrentChannel, focus, true, true);
                    }
                }
                catch { }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.PlaybackWindowForm))
            {
                try
                {
                    if (_window.EpgPanel.Visibility == Visibility.Visible && _shell.CurrentChannel != null)
                    {
                        var focus = _shell.PlaybackFocusTime ?? DateTime.Now;
                        _window.EpgManagerForManager?.RefreshEpgListWithFocus(_shell.CurrentChannel, focus, true, true);
                    }
                }
                catch { }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.IsPaused))
            {
                _window.UpdatePlayPauseIconFromManager();
            }
            else if (e.PropertyName == nameof(MainShellViewModel.PlayPauseSymbol))
            {
                try { _overlayManager.OverlayWpf?.SetPlaySymbol(_shell.PlayPauseSymbol); } catch { }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.PlaybackSpeed))
            {
                try { _overlayManager.OverlayWpf?.SetSpeed(_shell.PlaybackSpeed); } catch { }
            }
            else if (e.PropertyName == nameof(MainShellViewModel.PlaybackStatusText) || e.PropertyName == nameof(MainShellViewModel.PlaybackStatusBrush))
            {
                try { _overlayManager.OverlayWpf?.SetPlaybackStatus(_shell.PlaybackStatusText, _shell.PlaybackStatusBrush); } catch { }
            }
            if (e.PropertyName == nameof(MainShellViewModel.PlaybackMode)
                || e.PropertyName == nameof(MainShellViewModel.CurrentChannel)
                || e.PropertyName == nameof(MainShellViewModel.CurrentPlayingProgram)
                || e.PropertyName == nameof(MainShellViewModel.PlaybackFocusTime)
                || e.PropertyName == nameof(MainShellViewModel.IsTimeshiftActive))
            {
                RefreshTrayTooltip();
            }
        }

        private void RefreshTrayTooltip()
        {
            try
            {
                var channel = _shell.CurrentChannel?.Name;
                var program = _shell.CurrentPlayingProgram?.Title;
                var start = _shell.CurrentPlayingProgram?.Start;
                var end = _shell.CurrentPlayingProgram?.End;

                if (string.IsNullOrWhiteSpace(program) && _shell.PlaybackFocusTime.HasValue)
                {
                    var t = _shell.PlaybackFocusTime.Value;
                    program = _shell.CurrentChannel != null
                        ? _shell.EpgService?.GetProgramAt(_shell.CurrentChannel.TvgId, t, _shell.CurrentChannel.Name)?.Title
                        : null;
                }

                if ((!start.HasValue || !end.HasValue) && _shell.PlaybackFocusTime.HasValue && _shell.CurrentChannel != null)
                {
                    var hit = _shell.EpgService?.GetProgramAt(_shell.CurrentChannel.TvgId, _shell.PlaybackFocusTime.Value, _shell.CurrentChannel.Name);
                    if (hit != null)
                    {
                        start = hit.Start;
                        end = hit.End;
                        if (string.IsNullOrWhiteSpace(program)) program = hit.Title;
                    }
                }

                var baseText = string.IsNullOrWhiteSpace(channel) ? "SrcBox" : channel!;
                if (!string.IsNullOrWhiteSpace(program))
                {
                    baseText = $"{baseText} | {program}";
                }
                if (start.HasValue && end.HasValue)
                {
                    baseText = $"{baseText} | {start.Value:HH:mm}-{end.Value:HH:mm}";
                }
                var statusText = ResolvePlaybackStatusText();
                if (!string.IsNullOrWhiteSpace(statusText))
                {
                    baseText = $"{statusText} | {baseText}";
                }
                NotificationService.Instance.SetTrayTooltip(baseText);
            }
            catch { }
        }

        private string ResolvePlaybackStatusText()
        {
            return _shell.PlaybackMode switch
            {
                PlaybackMode.Live => ResxLocalizer.Get("EPG_Status_Live", "直播"),
                PlaybackMode.Replay => ResxLocalizer.Get("EPG_Status_Playback", "回放"),
                PlaybackMode.Timeshift => ResxLocalizer.Get("EPG_Status_Timeshift", "时移"),
                PlaybackMode.RecordingPlayback => ResxLocalizer.Get("EPG_Status_Record", "录播"),
                _ => ""
            };
        }
    }
}
