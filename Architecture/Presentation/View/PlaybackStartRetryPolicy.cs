using System;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    /// <summary>
    /// When a stream that never produced a picture should be re-requested. Relay served archives
    /// (RTSP/PLTV <c>playseek</c> windows) regularly answer the first session with nothing and start fine
    /// when the very same url is asked again - users worked around it by clicking the programme a second
    /// time - so the archive load gets exactly one automatic retry.
    /// </summary>
    internal static class PlaybackStartRetryPolicy
    {
        /// <summary>Seconds a playback attempt gets to show its first frame (same budget the live path uses).</summary>
        internal static double WaitSeconds(double sourceTimeoutSec) => Math.Max(3, sourceTimeoutSec);

        /// <summary>
        /// True when an archive load with no picture yet should be retried once. Live playback is excluded:
        /// it has its own source-timeout / auto-degrade path, and a retry there would fight it.
        /// </summary>
        internal static bool ShouldRetryArchive(bool archivePlayback, bool alreadyRetried, double waitedSeconds, double sourceTimeoutSec)
            => archivePlayback && !alreadyRetried && waitedSeconds > WaitSeconds(sourceTimeoutSec);
    }
}
