using System;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Covers the archive depth rule. An operator back end keeps a week of catch-up / timeshift at
    /// most, so the configured span is clamped instead of being trusted blindly: a range wider than
    /// the archive made the relay answer with a one second window, and playback stopped a few seconds
    /// after the seek (the timeshift range also used to be widened to the whole EPG, which offered
    /// dates the operator no longer serves).
    /// </summary>
    [TestClass]
    public class ArchiveWindowTests
    {
        [TestMethod]
        public void ConfiguredSpansAreClampedToTheArchiveDepth()
        {
            var replay = AppSettings.Current.Replay.DurationHours;
            var timeshift = AppSettings.Current.Timeshift.DurationHours;
            try
            {
                AppSettings.Current.Replay.DurationHours = 999;
                AppSettings.Current.Timeshift.DurationHours = 0;

                Assert.AreEqual(ArchiveWindow.MaxHours, ArchiveWindow.ReplayHours,
                    "the replay range must not reach past the 7 days the operator keeps");
                Assert.AreEqual(1, ArchiveWindow.TimeshiftHours, "the timeshift range keeps a one hour floor");
            }
            finally
            {
                AppSettings.Current.Replay.DurationHours = replay;
                AppSettings.Current.Timeshift.DurationHours = timeshift;
            }
        }

        [TestMethod]
        public void OnlyProgrammesInsideTheConfiguredSpanAreReplayable()
        {
            var replay = AppSettings.Current.Replay.DurationHours;
            var timeshift = AppSettings.Current.Timeshift.DurationHours;
            try
            {
                AppSettings.Current.Replay.DurationHours = 72;
                AppSettings.Current.Timeshift.DurationHours = 6;

                Assert.IsTrue(ArchiveWindow.IsReplayable(DateTime.Now.AddHours(-71)),
                    "a programme inside the replay span stays replayable");
                Assert.IsFalse(ArchiveWindow.IsReplayable(DateTime.Now.AddHours(-73)),
                    "a programme older than the replay span is expired");
                Assert.IsTrue(ArchiveWindow.ReplayFloor > DateTime.Now.AddHours(-72.1));
                Assert.IsTrue(ArchiveWindow.TimeshiftFloor > DateTime.Now.AddHours(-6.1));
            }
            finally
            {
                AppSettings.Current.Replay.DurationHours = replay;
                AppSettings.Current.Timeshift.DurationHours = timeshift;
            }
        }
    }
}
