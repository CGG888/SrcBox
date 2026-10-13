using System;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Single source of truth for how far back the archive (replay / timeshift) may reach.
    /// The span is what the user configures in the settings, but no operator back end here
    /// keeps more than a week: asking the relay for an instant it no longer archives is
    /// answered with a one second window, and playback stopped a few seconds after the seek.
    /// Everything therefore goes through this class and is clamped into [1 hour, 7 days].
    /// </summary>
    public static class ArchiveWindow
    {
        /// <summary>Deepest archive an operator back end here serves: 7 days.</summary>
        public const int MaxHours = 168;

        /// <summary>Configured replay span in hours, clamped to the archive limit.</summary>
        public static int ReplayHours => Clamp(AppSettings.Current?.Replay?.DurationHours);

        /// <summary>Configured timeshift span in hours, clamped to the archive limit.</summary>
        public static int TimeshiftHours => Clamp(AppSettings.Current?.Timeshift?.DurationHours);

        /// <summary>Oldest instant that can still be replayed.</summary>
        public static DateTime ReplayFloor => DateTime.Now.AddHours(-ReplayHours);

        /// <summary>Oldest instant that can still be timeshifted to.</summary>
        public static DateTime TimeshiftFloor => DateTime.Now.AddHours(-TimeshiftHours);

        /// <summary>True while a programme is (at least partly) inside the replayable span.</summary>
        public static bool IsReplayable(DateTime end) => end > ReplayFloor;

        private static int Clamp(int? hours) => Math.Max(1, Math.Min(MaxHours, hours ?? MaxHours));
    }
}
