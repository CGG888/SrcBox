using LibmpvIptvClient.Architecture.Presentation.View;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The first-frame watchdog: an archive (replay / timeshift) that stays black gets one automatic retry,
    /// because the relay routinely serves the second identical request while leaving the first silent
    /// (issue #6: users worked around it by clicking the programme twice). Live playback keeps its own
    /// source-timeout path and must never be retried from here.
    /// </summary>
    [TestClass]
    public class PlaybackStartRetryPolicyTests
    {
        const double Timeout = 5;

        [TestMethod]
        public void ArchiveWithNoPicture_IsRetriedOnce()
        {
            Assert.IsTrue(PlaybackStartRetryPolicy.ShouldRetryArchive(archivePlayback: true, alreadyRetried: false, waitedSeconds: 5.1, sourceTimeoutSec: Timeout));
        }

        [TestMethod]
        public void ArchiveInsideTheWaitBudget_IsNotRetriedYet()
        {
            Assert.IsFalse(PlaybackStartRetryPolicy.ShouldRetryArchive(true, false, 2.0, Timeout));
        }

        [TestMethod]
        public void ArchiveThatWasAlreadyRetried_IsNotRetriedAgain()
        {
            Assert.IsFalse(PlaybackStartRetryPolicy.ShouldRetryArchive(true, true, 30, Timeout));
        }

        [TestMethod]
        public void LivePlayback_IsNeverRetried()
        {
            Assert.IsFalse(PlaybackStartRetryPolicy.ShouldRetryArchive(false, false, 30, Timeout));
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
