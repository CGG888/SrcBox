using System;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Which kind of playback a programme segment on the EPG timeline stands for. The mode is derived from
    /// the wall clock plus the availability of the channel's archive (catchup source or the configured
    /// replay / timeshift url format), never from the click handler, so both hosted bars paint the same
    /// thing.
    /// </summary>
    public enum TimelineSegmentMode
    {
        /// <summary>No archive behind this instant: the future, or older than the replayable span.</summary>
        Unavailable = 0,

        /// <summary>The segment is on air right now and playback is following the live edge.</summary>
        Live = 1,

        /// <summary>Inside the replayable archive span: clicking re-requests the catchup url.</summary>
        Replay = 2,

        /// <summary>Inside the timeshift buffer: clicking seeks inside the running timeshift session.</summary>
        Timeshift = 3
    }

    /// <summary>What should happen after a segment on the timeline is activated.</summary>
    public enum TimelineSegmentAction
    {
        /// <summary>A future segment: nothing to play, the badge only announces the booking.</summary>
        None = 0,

        /// <summary>Leave the timeshift session and follow the live edge again.</summary>
        GoLive = 1,

        /// <summary>Seek inside the running timeshift buffer (keeps the session alive).</summary>
        SeekTimeshift = 2,

        /// <summary>Start catchup playback at the segment start.</summary>
        PlayReplay = 3,

        /// <summary>
        /// A slot that has not started yet: nothing to play, so the host books it with the same reminder flow the
        /// EPG list's reminder button uses.
        /// </summary>
        Book = 4
    }

    /// <summary>What releasing the pointer over the band does: commit a seek, select a segment, or nothing.</summary>
    public enum TimelineReleaseIntent
    {
        /// <summary>A pan moved the visible window; the release has nothing left to commit.</summary>
        None = 0,

        /// <summary>Previewed seek position: commit it to the player.</summary>
        Seek = 1,

        /// <summary>A tap on a segment: hand it to the host, which re-resolves the click verdict.</summary>
        Select = 2
    }

    /// <summary>
    /// The resolved intent behind a click on a programme segment. The decision is pure data so both hosts
    /// (windowed bottom bar and fullscreen overlay) and the unit tests share one implementation.
    /// </summary>
    public readonly record struct TimelineSegmentVerdict(
        TimelineSegmentAction Action,
        DateTime Target,
        bool KeepTimeshiftMode,
        string Hint)
    {
        /// <summary>True when the click does anything at all: playback for the three live states, a booking for a
        /// future slot. The state word alone is not enough there, the hover hint has to spell the booking out.</summary>
        public bool CanAct => Action != TimelineSegmentAction.None;
    }

    /// <summary>
    /// Localized captions of the four segment states. The classifier stays language neutral; the shell
    /// resolves the user facing words once per push and hands them to both hosts.
    /// </summary>
    public sealed record TimelineSegmentLabels(string Live, string Replay, string Timeshift, string Scheduled)
    {
        public static TimelineSegmentLabels Default { get; } = new("直播", "回看", "时移", "预约");

        public string ForMode(TimelineSegmentMode mode) => mode switch
        {
            TimelineSegmentMode.Live => Live,
            TimelineSegmentMode.Replay => Replay,
            TimelineSegmentMode.Timeshift => Timeshift,
            _ => Scheduled
        };
    }

    /// <summary>
    /// Single source of truth for the segment state and the click verdict of the EPG timeline. The desktop
    /// mirror of the web player's segment handling: the whole decision is a pure function of the programme
    /// window, the wall clock and the archive windows, which keeps it inside the catchup availability rule
    /// (<see cref="ArchiveWindow"/>) without touching WPF.
    /// </summary>
    public static class TimelineSegmentActions
    {
        /// <summary>
        /// Classifies one programme slot. A slot that is on air right now is "live" even while a timeshift
        /// session is running (the buffer simply reaches back far enough to cover it); the timeshift mode is
        /// reserved for the part of the buffer that already aired.
        /// </summary>
        public static TimelineSegmentMode Classify(DateTime start, DateTime end, DateTime now,
                                                   bool replayAvailable, DateTime replayFloor,
                                                   bool timeshiftActive, DateTime timeshiftMin, DateTime timeshiftMax)
        {
            if (end <= start) return TimelineSegmentMode.Unavailable;
            // The slot on air right now wins over the buffer: with a running session its window reaches back
            // over the current slot too, and the user still expects to see which programme is live.
            if (now >= start && now < end) return TimelineSegmentMode.Live;
            if (timeshiftActive && replayAvailable && start < timeshiftMax && end > timeshiftMin)
                return TimelineSegmentMode.Timeshift;
            // Only a slot that has already aired can be replayed: a future slot sits above the replay floor
            // too (the floor is in the past), so it must be excluded explicitly.
            if (replayAvailable && end <= now && end > replayFloor) return TimelineSegmentMode.Replay;
            return TimelineSegmentMode.Unavailable;
        }

        /// <summary>Click label of a verdict ("回到直播" / "回看" / "时移" / "预约").</summary>
        public static string GetActionLabel(TimelineSegmentAction action, TimelineSegmentLabels? labels)
        {
            var captions = labels ?? TimelineSegmentLabels.Default;
            return action switch
            {
                TimelineSegmentAction.GoLive => captions.Live,
                TimelineSegmentAction.SeekTimeshift => captions.Timeshift,
                TimelineSegmentAction.PlayReplay => captions.Replay,
                _ => captions.Scheduled
            };
        }

        /// <summary>Badge caption of a mode, or an empty string for the modes that need no corner badge.</summary>
        public static string GetBadgeText(TimelineSegmentMode mode, TimelineSegmentLabels? labels)
        {
            if (mode == TimelineSegmentMode.Live) return string.Empty;
            return (labels ?? TimelineSegmentLabels.Default).ForMode(mode);
        }

        /// <summary>Status caption of a mode as it appears in the nav row and the hover bubble.</summary>
        public static string GetStatusLabel(TimelineSegmentMode mode, TimelineSegmentLabels? labels)
            => (labels ?? TimelineSegmentLabels.Default).ForMode(mode);

        /// <summary>"HH:mm-HH:mm" (both sides dated when the slot runs into the next day).</summary>
        public static string FormatSpan(DateTime start, DateTime end)
            => start.Date == end.Date
                ? start.ToString("HH:mm") + "-" + end.ToString("HH:mm")
                : start.ToString("MM-dd HH:mm") + "-" + end.ToString("MM-dd HH:mm");

        /// <summary>
        /// The click verdict of a segment, in the order the user expects it: the live slot goes back to the
        /// live edge, a slot inside the running buffer seeks the timeshift session, anything else that is
        /// still replayable starts catchup playback, and a slot that has not started yet is a booking.
        /// </summary>
        public static TimelineSegmentVerdict Resolve(DateTime start, DateTime end, DateTime now,
                                                     bool replayAvailable, DateTime replayFloor,
                                                     bool timeshiftActive, DateTime timeshiftMin, DateTime timeshiftMax,
                                                     TimelineSegmentLabels? labels = null)
        {
            var mode = Classify(start, end, now, replayAvailable, replayFloor, timeshiftActive, timeshiftMin, timeshiftMax);
            // A slot that has not started yet cannot be played, but it can be booked. The clock at click time
            // decides, not the mode the strip cached a tick ago: the cached "Unavailable" is shared with the slots
            // that are simply too old to replay, and only the future ones are bookable.
            if (mode == TimelineSegmentMode.Unavailable && start > now)
            {
                var captions = labels ?? TimelineSegmentLabels.Default;
                return new TimelineSegmentVerdict(TimelineSegmentAction.Book, start, false,
                    "点击：" + captions.Scheduled + " " + FormatSpan(start, end));
            }
            return VerdictFor(mode, start, end, labels);
        }

        /// <summary>The verdict of an already classified segment (the hosts cache the mode on the model).</summary>
        public static TimelineSegmentVerdict VerdictFor(TimelineSegmentMode mode, DateTime start, DateTime end,
                                                        TimelineSegmentLabels? labels = null)
        {
            var captions = labels ?? TimelineSegmentLabels.Default;
            switch (mode)
            {
                case TimelineSegmentMode.Live:
                    return new TimelineSegmentVerdict(TimelineSegmentAction.GoLive, start, false,
                        "点击：" + captions.Live);
                case TimelineSegmentMode.Timeshift:
                    return new TimelineSegmentVerdict(TimelineSegmentAction.SeekTimeshift, start, true,
                        "点击：" + captions.Timeshift + " " + FormatSpan(start, end));
                case TimelineSegmentMode.Replay:
                    return new TimelineSegmentVerdict(TimelineSegmentAction.PlayReplay, start, false,
                        "点击：" + captions.Replay + " " + FormatSpan(start, end));
                default:
                    return new TimelineSegmentVerdict(TimelineSegmentAction.None, start, false,
                        captions.Scheduled);
            }
        }

        /// <summary>
        /// Snaps a hover instant down onto a ruler step and keeps it inside the visible window, so the hover
        /// cursor sits exactly on the tick it reports and never draws past the track edges.
        /// </summary>
        public static DateTime SnapHover(DateTime time, int tickMinutes, DateTime windowStart, TimeSpan windowSpan)
        {
            var snapped = TimelineBandMetrics.SnapToTick(time, tickMinutes);
            var end = windowStart + windowSpan;
            if (snapped < windowStart) return windowStart;
            if (snapped > end) return end;
            return snapped;
        }

        /// <summary>Formats a target instant as HH:mm, carrying the date when it runs into another day.</summary>
        public static string FormatClock(DateTime value, DateTime reference)
            => value.Date == reference.Date ? value.ToString("HH:mm") : value.ToString("MM-dd HH:mm");

        /// <summary>
        /// What releasing the pointer over the band means. A tap always selects the segment under the pointer -
        /// waiting for the seek drag to arm itself first would swallow every click on a segment that is not
        /// seekable yet, which is exactly the live state.
        /// </summary>
        public static TimelineReleaseIntent ResolveRelease(bool dragIsSeek, bool seekStarted, bool dragMoved)
        {
            if (dragIsSeek && seekStarted) return TimelineReleaseIntent.Seek;
            if (dragMoved) return TimelineReleaseIntent.None; // a pan moved the window; nothing to commit
            return TimelineReleaseIntent.Select;
        }

        public static string BuildStatusText(string name, string timeText, TimelineSegmentMode mode,
                                             TimelineSegmentLabels? labels = null)
            => timeText + "  " + name + "  ·  " + GetStatusLabel(mode, labels);
    }
}
