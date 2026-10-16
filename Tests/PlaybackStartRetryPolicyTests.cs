using LibmpvIptvClient.Architecture.Presentation.View;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The first-frame watchdog: an archive (replay / timeshift) that stays black is asked for again, because
    /// the relay routinely serves a later identical request while leaving the first silent (issue #6: the
    /// reporter only got a picture on the third click of the same window). Live playback keeps its own
    /// source-timeout path and must never be retried from here.
    /// </summary>
    [TestClass]
    public class PlaybackStartRetryPolicyTests
    {
        const double Timeout = 5;

        [TestMethod]
        public void ArchiveWithNoPicture_IsRetried()
        {
            Assert.IsTrue(PlaybackStartRetryPolicy.ShouldRetryArchive(archivePlayback: true, attemptsMade: 1, waitedSeconds: 5.1, sourceTimeoutSec: Timeout));
            Assert.IsTrue(PlaybackStartRetryPolicy.ShouldRetryArchive(true, 2, 5.1, Timeout));
        }

        [TestMethod]
        public void ArchiveInsideTheWaitBudget_IsNotRetriedYet()
        {
            Assert.IsFalse(PlaybackStartRetryPolicy.ShouldRetryArchive(true, 1, 2.0, Timeout));
        }

        [TestMethod]
        public void ArchiveThatUsedEveryAttempt_IsNotRetriedAgain()
        {
            Assert.IsFalse(PlaybackStartRetryPolicy.ShouldRetryArchive(true, PlaybackStartRetryPolicy.MaxArchiveAttempts, 30, Timeout));
            Assert.IsFalse(PlaybackStartRetryPolicy.ShouldRetryArchive(true, PlaybackStartRetryPolicy.MaxArchiveAttempts + 1, 30, Timeout));
        }

        [TestMethod]
        public void LivePlayback_IsNeverRetried()
        {
            Assert.IsFalse(PlaybackStartRetryPolicy.ShouldRetryArchive(false, 1, 30, Timeout));
        }

        [TestMethod]
        public void WaitBudget_KeepsAThreeSecondFloorAndFollowsTheSetting()
        {
            Assert.AreEqual(3, PlaybackStartRetryPolicy.WaitSeconds(0));
            Assert.AreEqual(3, PlaybackStartRetryPolicy.WaitSeconds(2));
            Assert.AreEqual(5, PlaybackStartRetryPolicy.WaitSeconds(5));
            Assert.AreEqual(12, PlaybackStartRetryPolicy.WaitSeconds(12));
        }
    }
}
