using System;
using System.Windows;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowSeekInteractionManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;

        public MainWindowSeekInteractionManager(MainWindow window, MainShellViewModel shell)
        {
            _window = window;
            _shell = shell;
        }

        public void SliderSeek_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _shell.IsSeeking = true;
            try
            {
                var slider = _window.SeekSliderForManager;
                var p = e.GetPosition(slider);
                var ratio = Math.Max(0, Math.Min(1, slider.ActualWidth > 0 ? p.X / slider.ActualWidth : 0));
                var sec = ratio * (_shell.SeekMaximum - slider.Minimum) + slider.Minimum;
                _shell.SeekValue = sec;
            }
            catch { }
        }

        public void SliderSeek_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_window.PlayerInterop == null) return;
            _shell.IsSeeking = false;
            var v = _shell.SeekValue;
            if (_shell.IsTimeshiftActive && _shell.CurrentChannel != null)
            {
                _shell.TimeshiftCursorSec = Math.Max(0, v);
                var t = _shell.TimeshiftMin.AddSeconds(_shell.TimeshiftCursorSec);
                _shell.ChannelPlaybackActions.PlayCatchupAt(_shell.CurrentChannel, t, keepTimeshiftMode: true);
                _shell.TimeshiftStart = t;
            }
            else if (_shell.PlaybackMode == PlaybackMode.Replay && _shell.CurrentChannel != null && _shell.CurrentPlayingProgram != null)
            {
                // The slider is program-relative in replay: convert back to a wall-clock time and
                // re-request the archive url (these streams cannot be seeked inside mpv).
                var target = _shell.CurrentPlayingProgram.Start.AddSeconds(Math.Max(0, v));
                _shell.ChannelPlaybackActions.PlayCatchupAt(_shell.CurrentChannel, target);
            }
            else
            {
                _shell.PlayerEngine?.SeekAbsolute(v);
            }
        }

        public void SliderSeek_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                var slider = _window.SeekSliderForManager;
                var p = e.GetPosition(slider);
                var ratio = Math.Max(0, Math.Min(1, slider.ActualWidth > 0 ? p.X / slider.ActualWidth : 0));
                var sec = ratio * (_shell.SeekMaximum - slider.Minimum) + slider.Minimum;
                if (_shell.IsTimeshiftActive)
                {
                    var t = _shell.TimeshiftMin.AddSeconds(Math.Max(0, sec));
                    slider.ToolTip = t.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else
                {
                    var tip = _shell.CurrentPlayingProgram != null
                        ? _shell.CurrentPlayingProgram.Start.AddSeconds(Math.Max(0, sec)).ToString("yyyy-MM-dd HH:mm:ss")
                        : TimeSpan.FromSeconds(Math.Max(0, sec)).ToString(TimeSpan.FromSeconds(sec).TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
                    slider.ToolTip = tip;
                }
            }
            catch { }
        }

        public void SliderSeek_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try { _window.SeekSliderForManager.ClearValue(FrameworkElement.ToolTipProperty); } catch { }
        }
    }
}
