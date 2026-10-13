using System;
using System.Collections.Generic;
using System.Windows;
using LibmpvIptvClient.Architecture.Application.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The fullscreen overlay hosts both progress bars and shows exactly one of them. These tests pin the rule that
    /// picks it: the configured style AND a schedule pushed by the shell, re-evaluated on every snapshot.
    /// Regression: the overlay used to demote itself to the standard bar before the first schedule existed and could
    /// then only be promoted by re-applying the settings, so the EPG timeline only appeared after a settings save
    /// (which is what a theme switch did) - it looked theme dependent on the first fullscreen entry.
    /// </summary>
    [TestClass]
    public class OverlayBarModeTests
    {
        static OverlayTimelineState Schedule()
        {
            var now = new DateTime(2026, 10, 13, 20, 0, 0);
            var programmes = new List<TimelineProgramme>
            {
                new TimelineProgramme { Start = now, End = now.AddMinutes(30), Name = "A", IsLive = true },
                new TimelineProgramme { Start = now.AddMinutes(30), End = now.AddMinutes(60), Name = "B" }
            };
            return new OverlayTimelineState
            {
                Programmes = programmes,
                Cursor = now,
                Now = now,
                Signature = OverlayTimelineState.BuildSignature(programmes)
            };
        }

        static System.Windows.Controls.Control Bar(OverlayControls overlay, string name)
            => (System.Windows.Controls.Control)overlay.FindName(name);

        /// <summary>Runs the body with the EPG timeline style selected and always restores the previous setting.</summary>
        static void WithStyle(SeekBarStyle style, Action body)
        {
            WpfTestHost.EnsureAppResources();
            var previous = AppSettings.Current.SeekBar;
            try
            {
                AppSettings.Current.SeekBar = style;
                body();
            }
            finally
            {
                AppSettings.Current.SeekBar = previous;
            }
        }

        [TestMethod]
        public void FirstSchedule_PromotesTheOverlayToTheEpgTimeline()
        {
            WpfTestHost.Invoke(() => WithStyle(SeekBarStyle.EpgTimeline, () =>
            {
                var overlay = new OverlayControls();
                // The host applies the configured style as soon as the overlay is built (MainWindowOverlayManager),
                // which happens long before the first channel has produced a schedule.
                overlay.ApplySeekBarStyle();

                // No schedule yet, so the standard bar owns the overlay.
                Assert.AreEqual(Visibility.Collapsed, overlay.TimelineControl.Visibility);
                Assert.AreEqual(Visibility.Visible, Bar(overlay, "SeekBar").Visibility);
                double standardHeight = overlay.Height;

                // The shell pushes the first snapshot while the overlay already exists (which is what happens on the
                // first fullscreen entry): the timeline has to take over without any settings being re-applied.
                overlay.SetTimelineState(Schedule());

                Assert.AreEqual(Visibility.Visible, overlay.TimelineControl.Visibility);
                Assert.AreEqual(Visibility.Collapsed, Bar(overlay, "SeekBar").Visibility);
                Assert.AreEqual(TimeshiftTimeline.BandRowHeight, overlay.TimelineControl.RequiredHeight);
                Assert.IsTrue(overlay.Height > standardHeight,
                    $"overlay grew from {standardHeight} to {overlay.Height} for the programme band");
            }));
        }

        [TestMethod]
        public void LostSchedule_FallsBackToTheStandardBar()
        {
            WpfTestHost.Invoke(() => WithStyle(SeekBarStyle.EpgTimeline, () =>
            {
                var overlay = new OverlayControls();
                overlay.ApplySeekBarStyle();
                overlay.SetTimelineState(Schedule());
                Assert.AreEqual(Visibility.Visible, overlay.TimelineControl.Visibility);

                // A channel without EPG data must not leave the empty programme band on screen.
                overlay.SetTimelineState(null);

                Assert.AreEqual(Visibility.Collapsed, overlay.TimelineControl.Visibility);
                Assert.AreEqual(Visibility.Visible, Bar(overlay, "SeekBar").Visibility);
                Assert.IsTrue(overlay.TimelineControl.RequiredHeight < TimeshiftTimeline.BandRowHeight,
                    "the compact meter row is shorter than the programme band");
            }));
        }

        [TestMethod]
        public void StandardStyle_KeepsTheStandardBarEvenWithASchedule()
        {
            WpfTestHost.Invoke(() => WithStyle(SeekBarStyle.Standard, () =>
            {
                var overlay = new OverlayControls();
                overlay.ApplySeekBarStyle();
                overlay.SetTimelineState(Schedule());

                Assert.AreEqual(Visibility.Collapsed, overlay.TimelineControl.Visibility);
                Assert.AreEqual(Visibility.Visible, Bar(overlay, "SeekBar").Visibility);

                // Re-applying the settings (which the theme switch does) must not change the chosen style either.
                overlay.ApplySeekBarStyle();
                Assert.AreEqual(Visibility.Collapsed, overlay.TimelineControl.Visibility);
            }));
        }
    }
}
