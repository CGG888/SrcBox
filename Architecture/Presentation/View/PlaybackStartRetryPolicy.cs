using System;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    /// <summary>
    /// When a stream that never produced a picture should be re-requested. Relay served archives
    /// (RTSP/PLTV <c>playseek</c> windows) regularly answer one session with nothing at all and start fine when
    /// the very same url is asked again - the reporter who supplied the logs only got a picture on his third
    /// click of the same window - so an archive load is re-requested until <see cref="MaxArchiveAttempts"/>
    /// sessions have been tried.
    /// </summary>
    internal static class PlaybackStartRetryPolicy
    {
        /// <summary>Sessions tried for one archive window before the stream is declared dead.</summary>
        internal const int MaxArchiveAttempts = 3;

        /// <summary>Seconds a playback attempt gets to show its first frame (same budget the live path uses).</summary>
        internal static double WaitSeconds(double sourceTimeoutSec) => Math.Max(3, sourceTimeoutSec);

        /// <summary>
        /// True when an archive load with no picture yet should be asked for again. Live playback is excluded:
        /// it has its own source-timeout / auto-degrade path, and a retry there would fight it.
        /// </summary>
        internal static bool ShouldRetryArchive(bool archivePlayback, int attemptsMade, double waitedSeconds, double sourceTimeoutSec)
            => archivePlayback && attemptsMade < MaxArchiveAttempts && waitedSeconds > WaitSeconds(sourceTimeoutSec);
    }
}
