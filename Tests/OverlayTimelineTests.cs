using System;
using LibmpvIptvClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Covers the sizing rule of the EPG timeline strip in the desktop overlay bar. The card width is
    /// proportional to the programme length but clamped, so a five minute news flash stays readable and a
    /// three hour film never pushes its neighbours off the strip. The same rule is used by the web remote
    /// (TS_PX_PER_MIN = 2, 84 .. 220 px), so these numbers are a contract rather than a taste decision.
    /// </summary>
    [TestClass]
    public class OverlayTimelineTests
    {
        [TestMethod]
        public void CardWidthIsProportionalToTheProgrammeLength()
        {
            Assert.AreEqual(120d, TimelineStripMetrics.CardWidth(TimeSpan.FromMinutes(60)), 0.001,
                "an hour long programme is two pixels per minute");
            Assert.AreEqual(180d, TimelineStripMetrics.CardWidth(TimeSpan.FromMinutes(90)), 0.001);
        }

        [DataTestMethod]
        [DataRow(0, 84d, DisplayName = "an empty slot keeps the minimum width")]
        [DataRow(5, 84d, DisplayName = "a five minute news flash stays readable")]
        [DataRow(42, 84d, DisplayName = "the exact minimum width is kept")]
        [DataRow(110, 220d, DisplayName = "the exact maximum width is kept")]
        [DataRow(180, 220d, DisplayName = "a long film is capped")]
        [DataRow(1440, 220d, DisplayName = "even a full day slot is capped")]
        public void CardWidthIsClampedToTheReadableRange(int minutes, double expected)
        {
            Assert.AreEqual(expected, TimelineStripMetrics.CardWidth(TimeSpan.FromMinutes(minutes)), 0.001);
        }

        [TestMethod]
        public void CardWidthNeverGoesBelowTheMinimumForBrokenEpgs()
        {
            // Operators do publish programmes with a missing or inverted end time; those must not produce a
            // zero width card that would make the strip jump while it is scrolled.
            Assert.AreEqual(TimelineStripMetrics.MinCardWidth, TimelineStripMetrics.CardWidth(TimeSpan.Zero), 0.001);
            Assert.AreEqual(TimelineStripMetrics.MinCardWidth, TimelineStripMetrics.CardWidth(TimeSpan.FromMinutes(-30)), 0.001);
        }

        [TestMethod]
        public void StripConstantsMatchTheWebRemote()
        {
            Assert.AreEqual(84d, TimelineStripMetrics.MinCardWidth, 0.001);
            Assert.AreEqual(220d, TimelineStripMetrics.MaxCardWidth, 0.001);
            Assert.AreEqual(2d, TimelineStripMetrics.PixelsPerMinute, 0.001);
            Assert.AreEqual(2d, TimelineStripMetrics.CardGap, 0.001);
        }

        [TestMethod]
        public void FormatClockOnlyCarriesTheDateWhenItMatters()
        {
            var value = new DateTime(2026, 10, 13, 9, 5, 0);
            Assert.AreEqual("09:05", TimelineStripMetrics.FormatClock(value, false));
            Assert.AreEqual("10-13 09:05", TimelineStripMetrics.FormatClock(value, true));
        }

        [TestMethod]
        public void CardWidthFollowsTheProgrammeWindow()
        {
            var programme = new TimelineProgramme
            {
                Start = new DateTime(2026, 10, 13, 9, 0, 0),
                End = new DateTime(2026, 10, 13, 10, 0, 0)
            };
            Assert.AreEqual(120d, programme.CardWidth, 0.001, "an hour long programme is two pixels per minute");
        }

        /// <summary>
        /// The hosted control keeps the card instances it already laid out and copies the mutating flags onto them
        /// every tick, so those flags have to raise change notifications or the strip would freeze on the first paint.
        /// </summary>
        [TestMethod]
        public void ProgrammeStateFlagsNotifyTheStrip()
        {
            var programme = new TimelineProgramme();
            var changed = new System.Collections.Generic.List<string>();
            programme.PropertyChanged += (s, e) => changed.Add(e.PropertyName ?? string.Empty);

            programme.Name = "News";
            programme.IsCurrent = true;
            programme.IsLive = true;
            programme.CanPlay = false;
            programme.TimeText = "09:05";

            CollectionAssert.AreEquivalent(
                new[] { nameof(TimelineProgramme.Name), nameof(TimelineProgramme.IsCurrent), nameof(TimelineProgramme.IsLive), nameof(TimelineProgramme.CanPlay), nameof(TimelineProgramme.TimeText) },
                changed);
        }

        /// <summary>
        /// The strip is only rebuilt when this signature changes, so it must depend on the programme starts and
        /// nothing else: an identical schedule has to keep the signature (no flicker) and a shifted one must not.
        /// </summary>
        [TestMethod]
        public void ScheduleSignatureIsStableForUnchangedProgrammesAndChangesWithTheStarts()
        {
            var start = new DateTime(2026, 10, 13, 9, 0, 0);
            var build = new Func<DateTime, System.Collections.Generic.List<TimelineProgramme>>(offset => new System.Collections.Generic.List<TimelineProgramme>
            {
                new TimelineProgramme { Start = offset, End = offset.AddHours(1) },
                new TimelineProgramme { Start = offset.AddHours(1), End = offset.AddHours(2) }
            });

            var first = OverlayTimelineState.BuildSignature(build(start));
            var sameScheduleDifferentObjects = OverlayTimelineState.BuildSignature(build(start));
            var shifted = OverlayTimelineState.BuildSignature(build(start.AddMinutes(1)));

            Assert.AreEqual(first, sameScheduleDifferentObjects, "an unchanged schedule must not rebuild the strip");
            Assert.AreNotEqual(first, shifted, "a shifted schedule must rebuild the strip");
            Assert.AreEqual(string.Empty, OverlayTimelineState.BuildSignature(null));
            Assert.AreEqual(string.Empty, OverlayTimelineState.BuildSignature(new System.Collections.Generic.List<TimelineProgramme>()));
        }

        /// <summary>
        /// The control falls back to the plain 0..duration meter when no programme covers the cursor, so the
        /// snapshot's anchor flag is part of the contract between the shell and both hosted bars.
        /// </summary>
        [TestMethod]
        public void TimelineStateDefaultsToAnUnanchoredEmptySchedule()
        {
            var state = new OverlayTimelineState();
            Assert.AreEqual(0, state.Programmes.Count);
            Assert.IsFalse(state.ProgrammeAnchored, "live playback must not anchor the meter to a programme");
            Assert.AreEqual(string.Empty, state.Signature);
        }
    }
}
