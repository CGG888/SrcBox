using System;
using System.Collections.Generic;

namespace LibmpvIptvClient
{
    /// <summary>
    /// Pure layout maths of the compact programme timeline - the desktop mirror of rtp2httpd's
    /// <c>web-ui/src/lib/epg-timeline.ts</c>. Nothing here touches WPF, so the viewport sizing, the ruler
    /// step, the percentage mapping and the prev/next segment paging are unit testable without a UI thread.
    /// </summary>
    public static class TimelineBandMetrics
    {
        /// <summary>Candidate ruler steps in minutes, smallest first (the web player's TICK_MINUTE_OPTIONS).</summary>
        public static readonly int[] TickMinuteOptions = { 5, 10, 15, 30, 60 };

        /// <summary>A viewport never draws more than this many ruler ticks.</summary>
        public const int MaxTicksPerWindow = 12;

        /// <summary>Half of the default viewport: the window is the anchor +/- this many minutes.</summary>
        public const int WideWindowHalfMinutes = 90;

        /// <summary>Half of the viewport on a mid sized bar.</summary>
        public const int MediumWindowHalfMinutes = 60;

        /// <summary>Half of the viewport on a narrow bar.</summary>
        public const int NarrowWindowHalfMinutes = 45;

        /// <summary>Below this many DIPs the bar is "narrow" and the window shrinks to +/-45 minutes.</summary>
        public const double NarrowBandWidth = 480d;

        /// <summary>Below this many DIPs the bar is "medium" and the window shrinks to +/-60 minutes.</summary>
        public const double MediumBandWidth = 720d;

        /// <summary>Pointer travel below this is a tap, above it a drag (DRAG_THRESHOLD_PX).</summary>
        public const double DragThresholdPx = 4d;

        /// <summary>Short programmes still get a clickable sliver at the end of the track.</summary>
        public const double MinimumBlockWidth = 2d;

        /// <summary>Half of the viewport for a bar of <paramref name="bandWidth"/> DIPs.</summary>
        public static int WindowHalfMinutesFor(double bandWidth)
        {
            if (bandWidth < NarrowBandWidth) return NarrowWindowHalfMinutes;
            if (bandWidth < MediumBandWidth) return MediumWindowHalfMinutes;
            return WideWindowHalfMinutes;
        }

        /// <summary>Smallest candidate step that keeps the window at or below <see cref="MaxTicksPerWindow"/> ticks.</summary>
        public static int TickMinutesFor(TimeSpan windowSpan)
        {
            double minutes = windowSpan.TotalMinutes;
            if (minutes <= 0) return TickMinuteOptions[0];
            for (int i = 0; i < TickMinuteOptions.Length; i++)
            {
                if (minutes / TickMinuteOptions[i] <= MaxTicksPerWindow) return TickMinuteOptions[i];
            }
            return TickMinuteOptions[TickMinuteOptions.Length - 1];
        }

        /// <summary>Rounds a wall clock down onto the previous tick boundary so the ruler always starts on a step.</summary>
        public static DateTime SnapToTick(DateTime time, int tickMinutes)
        {
            if (tickMinutes <= 0) return time;
            long step = TimeSpan.FromMinutes(tickMinutes).Ticks;
            long remainder = time.Ticks % step;
            if (remainder < 0) remainder += step;
            return new DateTime(time.Ticks - remainder, time.Kind);
        }

        /// <summary>Position of <paramref name="time"/> inside the window as a 0..1 fraction (not clamped).</summary>
        public static double PercentFor(DateTime windowStart, TimeSpan windowSpan, DateTime time)
        {
            if (windowSpan.Ticks <= 0) return 0d;
            return (time - windowStart).Ticks / (double) windowSpan.Ticks;
        }

        /// <summary>Wall clock at a 0..1 fraction of the window.</summary>
        public static DateTime TimeAtPercent(DateTime windowStart, TimeSpan windowSpan, double percent)
            => windowStart + TimeSpan.FromTicks((long) (windowSpan.Ticks * percent));

        /// <summary>Clamps a fraction to the visible 0..1 range.</summary>
        public static double ClampPercent(double value)
        {
            if (double.IsNaN(value)) return 0d;
            if (value < 0d) return 0d;
            if (value > 1d) return 1d;
            return value;
        }

        /// <summary>Compact ruler caption; the date is carried by the window range label instead.</summary>
        public static string FormatTickLabel(DateTime time) => time.ToString("HH:mm");

        /// <summary>
        /// Ruler caption relative to the window start: the plain clock, prefixed with the date when the tick runs
        /// past midnight (so a window that spans days never shows an ambiguous "00:30" twice).
        /// </summary>
        public static string FormatTickLabel(DateTime time, DateTime windowStart)
            => time.Date == windowStart.Date && time.TimeOfDay != TimeSpan.Zero
                ? time.ToString("HH:mm")
                : time.ToString("MM-dd HH:mm");

        /// <summary>"MM-dd HH:mm-HH:mm", or with both dates when the window runs past midnight.</summary>
        public static string FormatWindowRange(DateTime windowStart, TimeSpan windowSpan)
        {
            var end = windowStart + windowSpan;
            return windowStart.Date == end.Date
                ? windowStart.ToString("MM-dd HH:mm") + " - " + end.ToString("HH:mm")
                : windowStart.ToString("MM-dd HH:mm") + " - " + end.ToString("MM-dd HH:mm");
        }

        /// <summary>Index of the programme covering <paramref name="time"/>, or -1.</summary>
        public static int FindProgrammeIndex(IReadOnlyList<TimelineProgramme>? programmes, DateTime time)
        {
            if (programmes == null) return -1;
            for (int i = 0; i < programmes.Count; i++)
            {
                var p = programmes[i];
                if (time >= p.Start && time < p.End) return i;
            }
            return -1;
        }

        /// <summary>Index of the programme whose span contains <paramref name="percent"/> of the window, or -1.</summary>
        public static int FindProgrammeIndexAtPercent(IReadOnlyList<TimelineProgramme>? programmes, DateTime windowStart,
                                                      TimeSpan windowSpan, double percent)
            => FindProgrammeIndex(programmes, TimeAtPercent(windowStart, windowSpan, ClampPercent(percent)));

        /// <summary>
        /// Anchor the window takes after pressing the prev/next segment button: the middle of the neighbouring
        /// programme on that side of <paramref name="anchor"/>. A programme that still contains the anchor is
        /// skipped, so pressing a button always moves the viewport by one slot instead of re-centring in place.
        /// Falls back to half a window when the schedule has no further programme, so the button never becomes
        /// a no-op.
        /// </summary>
        public static DateTime PageAnchor(IReadOnlyList<TimelineProgramme>? programmes, DateTime anchor,
                                          TimeSpan windowSpan, int direction)
        {
            int dir = direction >= 0 ? 1 : -1;
            TimelineProgramme? target = null;
            if (programmes != null)
            {
                for (int i = 0; i < programmes.Count; i++)
                {
                    var p = programmes[i];
                    if (p.End <= p.Start) continue;
                    if (dir > 0)
                    {
                        if (p.Start < anchor) continue;
                        if (target == null || p.Start < target.Start) target = p;
                    }
                    else
                    {
                        if (p.End > anchor) continue;
                        if (target == null || p.Start > target.Start) target = p;
                    }
                }
            }
            if (target == null) return anchor + TimeSpan.FromTicks(windowSpan.Ticks / 2 * dir);
            return target.Start + TimeSpan.FromTicks((target.End - target.Start).Ticks / 2);
        }
    }
}
