using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Controls;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public class MainWindowTimeshiftOverlaySyncActionsViewModel : ViewModelBase
    {
        public struct TimeshiftState
        {
            public bool IsActive { get; set; }
            public DateTime Min { get; set; }
            public DateTime Max { get; set; }
            public double CurrentSec { get; set; }
            public double TotalSec { get; set; }
            public string ElapsedText { get; set; }
            public string DurationText { get; set; }
            public double SliderMax { get; set; }
            public double SliderValue { get; set; }
        }

        public TimeshiftState BuildState(
            bool isActive,
            DateTime min,
            DateTime max,
            double cursorSec,
            MpvInterop mpv,
            bool isSeeking,
            double currentSliderValue)
        {
            var state = new TimeshiftState
            {
                IsActive = isActive,
                Min = min,
                Max = max,
                CurrentSec = cursorSec
            };

            if (isActive)
            {
                // In timeshift mode, Max is usually Now
                state.Max = DateTime.Now;
                var total = Math.Max(1, (state.Max - state.Min).TotalSeconds);
                state.TotalSec = total;
                state.SliderMax = total;

                // Clamp cursor
                var clampedCursor = Math.Max(0, Math.Min(total, cursorSec));
                state.CurrentSec = clampedCursor;

                var current = state.Min.AddSeconds(clampedCursor);
                state.ElapsedText = current.ToString("yyyy-MM-dd HH:mm:ss");
                state.DurationText = state.Max.ToString("yyyy-MM-dd HH:mm:ss");

                if (!isSeeking)
                {
                    state.SliderValue = clampedCursor;
                }
                else
                {
                    state.SliderValue = currentSliderValue; // Keep dragging value
                }
            }
            else
            {
                // Normal playback mode
                var pos = mpv?.GetTimePos() ?? 0;
                var dur = mpv?.GetDuration() ?? 0;
                
                state.CurrentSec = pos;
                state.TotalSec = dur;
                state.ElapsedText = FormatTime(pos);
                state.DurationText = FormatTime(dur);

                if (!isSeeking)
                {
                    state.SliderMax = dur <= 0 ? 1 : dur;
                    state.SliderValue = Math.Max(0, Math.Min(state.SliderMax, pos));
                }
                else
                {
                    state.SliderMax = dur <= 0 ? 1 : dur; // Update max even when seeking
                    state.SliderValue = currentSliderValue;
                }
            }

            return state;
        }

        public void Sync(
            TimeshiftState state,
            Action<double, double> updateSlider,
            Action<string, string> updateLabels,
            Action<double, double> updateOverlayTime,
            Action<DateTime, DateTime> updateOverlayTimeshiftRange,
            Action<DateTime, DateTime, bool> updateOverlayTimeshiftLabels)
        {
            // Sync Slider
            updateSlider?.Invoke(state.SliderValue, state.SliderMax);

            // Sync Text Labels
            updateLabels?.Invoke(state.ElapsedText, state.DurationText);

            // Sync Overlay
            if (state.IsActive)
            {
                updateOverlayTimeshiftRange?.Invoke(state.Min, state.Max);
                updateOverlayTime?.Invoke(state.CurrentSec, state.TotalSec);
                
                var current = state.Min.AddSeconds(state.CurrentSec);
                updateOverlayTimeshiftLabels?.Invoke(current, state.Max, false);
            }
            else
            {
                updateOverlayTime?.Invoke(state.CurrentSec, state.TotalSec);
            }
        }

        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return ts.ToString(@"h\:mm\:ss");
            return ts.ToString(@"mm\:ss");
        }
        public (DateTime Min, DateTime Max, DateTime Start, double CursorSec) CalculateTimeshiftRange(
            Channel currentChannel,
            EpgService epgService,
            int durationHours)
        {
            var max = DateTime.Now;
            var min = max.AddHours(-Math.Max(0, durationHours));

            if (currentChannel != null)
            {
                try
                {
                    var programs = epgService?.GetPrograms(currentChannel.TvgId, currentChannel.TvgName, currentChannel.Name);
                    if (programs != null && programs.Count > 0)
                    {
                        var earliest = programs.Min(p => p.Start);
                        if (earliest < min) min = earliest;
                    }
                }
                catch { }
            }

            var start = max;
            var cursor = Math.Max(0, (max - min).TotalSeconds);

            return (min, max, start, cursor);
        }
    }
}
