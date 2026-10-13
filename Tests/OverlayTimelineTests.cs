using System;
using LibmpvIptvClient;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;
using LibmpvIptvClient.Services;
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
        /// The segment mode of the band (live / replay / timeshift / scheduled) is one pure decision, so both
        /// host bars and the unit tests read the same answer. The rules: the live slot stays live, a slot inside
        /// the running buffer is timeshift, an aired slot inside the replayable span is replay, and the rest
        /// (future slots, expired slots, channels without an archive) is unavailable.
        /// </summary>
        [TestMethod]
        public void SegmentModeFollowsTheClockTheBufferAndTheArchive()
        {
            var now = new DateTime(2026, 10, 13, 20, 0, 0);
            var replayFloor = now.AddHours(-24);
            var tsMin = now.AddHours(-2);
            var tsMax = now;

            // No timeshift session: the live slot is live, the aired slot is replay, the future slot is not playable.
            Assert.AreEqual(Services.TimelineSegmentMode.Live,
                Services.TimelineSegmentActions.Classify(now.AddMinutes(-30), now.AddMinutes(30), now, true, replayFloor, false, tsMin, tsMax));
            Assert.AreEqual(Services.TimelineSegmentMode.Replay,
                Services.TimelineSegmentActions.Classify(now.AddHours(-3), now.AddHours(-2), now, true, replayFloor, false, tsMin, tsMax));
            Assert.AreEqual(Services.TimelineSegmentMode.Unavailable,
                Services.TimelineSegmentActions.Classify(now.AddHours(1), now.AddHours(2), now, true, replayFloor, false, tsMin, tsMax));
            Assert.AreEqual(Services.TimelineSegmentMode.Unavailable,
                Services.TimelineSegmentActions.Classify(now.AddHours(-40), now.AddHours(-39), now, true, replayFloor, false, tsMin, tsMax));

            // With a running buffer, everything the buffer still covers becomes timeshift (that part already aired).
            Assert.AreEqual(Services.TimelineSegmentMode.Timeshift,
                Services.TimelineSegmentActions.Classify(now.AddMinutes(-90), now.AddMinutes(-30), now, true, replayFloor, true, tsMin, tsMax));
            Assert.AreEqual(Services.TimelineSegmentMode.Replay,
                Services.TimelineSegmentActions.Classify(now.AddHours(-6), now.AddHours(-5), now, true, replayFloor, true, tsMin, tsMax),
                "a slot outside the buffer stays a plain replay");
            Assert.AreEqual(Services.TimelineSegmentMode.Live,
                Services.TimelineSegmentActions.Classify(now.AddMinutes(-5), now.AddMinutes(25), now, true, replayFloor, true, tsMin, tsMax),
                "the slot that is on air right now stays live even while the buffer runs behind it");

            // A channel without an archive can only serve the live slot; the past is not replayable.
            Assert.AreEqual(Services.TimelineSegmentMode.Unavailable,
                Services.TimelineSegmentActions.Classify(now.AddHours(-3), now.AddHours(-2), now, false, replayFloor, false, tsMin, tsMax));
            Assert.AreEqual(Services.TimelineSegmentMode.Unavailable,
                Services.TimelineSegmentActions.Classify(now, now, now, true, replayFloor, false, tsMin, tsMax),
                "a slot with no length is never playable");
        }

        /// <summary>
        /// The click verdict of a segment: the live slot goes back to the live edge, the buffered slot seeks the
        /// timeshift session (and keeps it alive), an aired slot starts catchup, and the future slot does nothing.
        /// </summary>
        [TestMethod]
        public void SegmentVerdictPicksTheActionTheUserExpects()
        {
            var start = new DateTime(2026, 10, 13, 20, 30, 0);
            var end = start.AddHours(1);
            var labels = new Services.TimelineSegmentLabels("Live", "Replay", "Timeshift", "Booked");

            var live = Services.TimelineSegmentActions.VerdictFor(Services.TimelineSegmentMode.Live, start, end, labels);
            Assert.AreEqual(Services.TimelineSegmentAction.GoLive, live.Action);
            Assert.IsFalse(live.KeepTimeshiftMode, "returning to the live edge leaves the timeshift session");

            var timeshift = Services.TimelineSegmentActions.VerdictFor(Services.TimelineSegmentMode.Timeshift, start, end, labels);
            Assert.AreEqual(Services.TimelineSegmentAction.SeekTimeshift, timeshift.Action);
            Assert.IsTrue(timeshift.KeepTimeshiftMode, "seeking inside the buffer must keep the session alive");
            Assert.AreEqual(start, timeshift.Target);
            StringAssert.Contains(timeshift.Hint, "Timeshift");
            StringAssert.Contains(timeshift.Hint, "20:30-21:30");

            var replay = Services.TimelineSegmentActions.VerdictFor(Services.TimelineSegmentMode.Replay, start, end, labels);
            Assert.AreEqual(Services.TimelineSegmentAction.PlayReplay, replay.Action);
            Assert.IsFalse(replay.KeepTimeshiftMode);

            var future = Services.TimelineSegmentActions.VerdictFor(Services.TimelineSegmentMode.Unavailable, start, end, labels);
            Assert.AreEqual(Services.TimelineSegmentAction.None, future.Action);
            Assert.IsFalse(future.CanAct, "a future slot has nothing to play yet");
            Assert.AreEqual("Booked", future.Hint);
        }

        /// <summary>
        /// The resolved click verdict (the one the two bars actually use) books a slot that has not started yet:
        /// that is what makes a tap on the EPG timeline do exactly what the EPG list's reminder button does. A slot
        /// that already aired but sits outside the archive is not bookable, and the live slot keeps its own action
        /// so the ruler row does not turn a plain time click into a booking.
        /// </summary>
        [TestMethod]
        public void ResolvedFutureSlotBooksLikeTheEpgListButton()
        {
            var now = new DateTime(2026, 10, 13, 20, 0, 0);
            var replayFloor = now.AddHours(-72);
            var labels = new Services.TimelineSegmentLabels("Live", "Replay", "Timeshift", "Booked");

            var start = now.AddHours(2);
            var future = Services.TimelineSegmentActions.Resolve(start, start.AddHours(1), now, true, replayFloor, false, now, now, labels);
            Assert.AreEqual(Services.TimelineSegmentAction.Book, future.Action);
            Assert.AreEqual(start, future.Target, "the booking is for the slot start, not for the click instant");
            Assert.IsFalse(future.KeepTimeshiftMode, "booking never leaves the ticker in the timeshift state");
            Assert.IsTrue(future.CanAct, "the click opens the reminder dialog, so it is an action");
            StringAssert.Contains(future.Hint, "Booked");
            StringAssert.Contains(future.Hint, "22:00-23:00");

            var stale = Services.TimelineSegmentActions.Resolve(now.AddHours(-100), now.AddHours(-99), now, true, replayFloor, false, now, now, labels);
            Assert.AreEqual(Services.TimelineSegmentAction.None, stale.Action,
                "a slot older than the archive has nothing to play and nothing to book");
            Assert.IsFalse(stale.CanAct);

            var live = Services.TimelineSegmentActions.Resolve(now.AddMinutes(-5), now.AddMinutes(25), now, true, replayFloor, false, now, now, labels);
            Assert.AreEqual(Services.TimelineSegmentAction.GoLive, live.Action,
                "the slot on air keeps returning to the live edge instead of booking");
        }

        /// <summary>
        /// Both bars (windowed bottom bar and fullscreen overlay) ask the control for the verdict, and the control
        /// asks the same control for the ruler row, so the control itself has to answer a future slot with the
        /// booking action.
        /// </summary>
        [TestMethod]
        public void TimelineControlResolvesAFutureSegmentAsABooking()
        {
            WpfTestHost.EnsureAppResources();
            WpfTestHost.Invoke(() =>
            {
                var timeline = new TimeshiftTimeline();
                var start = DateTime.Now.AddHours(3);
                var verdict = timeline.ResolveSegment(new TimelineProgramme
                {
                    Name = "later",
                    Start = start,
                    End = start.AddHours(1)
                });
                Assert.AreEqual(Services.TimelineSegmentAction.Book, verdict.Action);
                Assert.AreEqual(start, verdict.Target);
            });
        }

        /// <summary>
        /// The hover cursor snaps down onto the ruler step so the bubble reports the instant the cursor draws,
        /// and it is clamped to the visible window so it can never point past either edge of the band.
        /// </summary>
        [TestMethod]
        public void HoverCursorSnapsToTheRulerStepAndStaysInsideTheWindow()
        {
            var windowStart = new DateTime(2026, 10, 13, 19, 0, 0);
            var span = TimeSpan.FromMinutes(180);

            Assert.AreEqual(new DateTime(2026, 10, 13, 19, 20, 0),
                Services.TimelineSegmentActions.SnapHover(new DateTime(2026, 10, 13, 19, 27, 0), 10, windowStart, span));
            Assert.AreEqual(windowStart,
                Services.TimelineSegmentActions.SnapHover(new DateTime(2026, 10, 13, 18, 0, 0), 10, windowStart, span),
                "hovering left of the window clamps onto its start");
            Assert.AreEqual(windowStart.Add(span),
                Services.TimelineSegmentActions.SnapHover(new DateTime(2026, 10, 13, 23, 59, 0), 10, windowStart, span),
                "hovering right of the window clamps onto its end");
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

        // -----------------------------------------------------------------
        // Viewport sizing, ruler stepping and the prev/next segment paging
        // (the desktop mirror of web-ui/src/lib/epg-timeline.ts).
        // -----------------------------------------------------------------

        /// <summary>Narrow bars look closer in time, wide bars further out, exactly like the web viewport rules.</summary>
        [TestMethod]
        public void ViewportShrinksOnNarrowBars()
        {
            Assert.AreEqual(45, TimelineBandMetrics.WindowHalfMinutesFor(320), "a phone-narrow bar shows 90 minutes");
            Assert.AreEqual(45, TimelineBandMetrics.WindowHalfMinutesFor(479.9));
            Assert.AreEqual(60, TimelineBandMetrics.WindowHalfMinutesFor(480), "the 480 DIP breakpoint is inclusive");
            Assert.AreEqual(60, TimelineBandMetrics.WindowHalfMinutesFor(719.9));
            Assert.AreEqual(90, TimelineBandMetrics.WindowHalfMinutesFor(720), "the 720 DIP breakpoint is inclusive");
            Assert.AreEqual(90, TimelineBandMetrics.WindowHalfMinutesFor(1920));
        }

        /// <summary>Step in minutes that keeps the ruler at 12 ticks or fewer for every viewport width.</summary>
        [DataTestMethod]
        [DataRow(90, 10, DisplayName = "the narrow viewport uses a ten minute step")]
        [DataRow(120, 10, DisplayName = "the medium viewport uses a ten minute step")]
        [DataRow(180, 15, DisplayName = "the wide viewport uses a fifteen minute step")]
        public void RulerStepKeepsTheWindowUnderTwelveTicks(int windowMinutes, int expectedStep)
        {
            var span = TimeSpan.FromMinutes(windowMinutes);
            Assert.AreEqual(expectedStep, TimelineBandMetrics.TickMinutesFor(span));
            Assert.IsTrue(windowMinutes / (double) expectedStep <= TimelineBandMetrics.MaxTicksPerWindow);
        }

        [TestMethod]
        public void RulerStepSurvivesDegenerateWindows()
        {
            Assert.AreEqual(5, TimelineBandMetrics.TickMinutesFor(TimeSpan.Zero));
            Assert.AreEqual(5, TimelineBandMetrics.TickMinutesFor(TimeSpan.FromMinutes(-30)));
            Assert.AreEqual(60, TimelineBandMetrics.TickMinutesFor(TimeSpan.FromDays(7)),
                "a very long window falls back to the coarsest step rather than drawing thousands of ticks");
        }

        /// <summary>
        /// Minor ticks subdivide a major step only while the notches stay far enough apart: the finest cadence
        /// that divides the step and leaves at least <see cref="TimelineBandMetrics.MinimumMinorTickSpacing"/>
        /// between two notches wins, and a step that is already minute-fine gets none.
        /// </summary>
        [DataTestMethod]
        [DataRow(15, 120d, 1, DisplayName = "a wide fifteen minute step allows the finest notch")]
        [DataRow(10, 90d, 1, DisplayName = "a ten minute step on a wide bar also subdivides to minutes")]
        [DataRow(15, 30d, 5, DisplayName = "a narrow fifteen minute step settles for five minute notches")]
        [DataRow(60, 240d, 2, DisplayName = "the hourly step falls back to two minute notches")]
        [DataRow(5, 90d, 1, DisplayName = "the finest major step can still subdivide once")]
        public void MinorTicksSubdivideAMajorStepWhileItStaysReadable(int majorMinutes, double spacing, int expected)
        {
            Assert.AreEqual(expected, TimelineBandMetrics.MinorTickMinutesFor(majorMinutes, spacing));
            int minor = TimelineBandMetrics.MinorTickMinutesFor(majorMinutes, spacing);
            Assert.AreEqual(0, majorMinutes % minor, "a minor cadence must divide the major step evenly");
            Assert.IsTrue(spacing * minor / majorMinutes >= TimelineBandMetrics.MinimumMinorTickSpacing,
                "minor notches must never be drawn closer than the readable minimum");
        }

        [TestMethod]
        public void MinorTicksStayOffForDegenerateRulers()
        {
            Assert.AreEqual(0, TimelineBandMetrics.MinorTickMinutesFor(0, 200));
            Assert.AreEqual(0, TimelineBandMetrics.MinorTickMinutesFor(1, 400),
                "a one minute step has nothing left to subdivide");
            Assert.AreEqual(0, TimelineBandMetrics.MinorTickMinutesFor(15, 0));
            Assert.AreEqual(0, TimelineBandMetrics.MinorTickMinutesFor(15, -40));
        }

        [TestMethod]
        public void MinorNotchesStayQuieterThanTheFaintestMajorNotch()
        {
            Assert.IsTrue(TimelineBandMetrics.MinorNotchHeight < TimelineBandMetrics.StepNotchHeight,
                "a minor notch is shorter than the plain step notch");
            Assert.IsTrue(TimelineBandMetrics.StepNotchHeight < TimelineBandMetrics.HalfHourNotchHeight,
                "the plain step is shorter than the half hour notch");
            Assert.IsTrue(TimelineBandMetrics.HalfHourNotchHeight < TimelineBandMetrics.HourNotchHeight,
                "the half hour notch is shorter than the hour notch");
            Assert.IsTrue(TimelineBandMetrics.MinorNotchOpacity < TimelineBandMetrics.StepNotchOpacity
                && TimelineBandMetrics.StepNotchOpacity < TimelineBandMetrics.HalfHourNotchOpacity
                && TimelineBandMetrics.HalfHourNotchOpacity < TimelineBandMetrics.HourNotchOpacity,
                "the ladder gets fainter the finer the notch");
            Assert.IsTrue(TimelineBandMetrics.MinorNotchOpacity >= 0.4d,
                "but the finest notch stays visible rather than fading into the bar");
        }

        /// <summary>
        /// The ruler ladder has to stay readable in both palettes: every rung is tall enough to see, and the
        /// tallest one still leaves the 13 px ruler row's label band alone.
        /// </summary>
        [TestMethod]
        public void RulerLadderStaysVisibleInsideTheRulerRow()
        {
            Assert.IsTrue(TimelineBandMetrics.MinorNotchHeight >= 4d, "the minute subdivision must be visible");
            Assert.IsTrue(TimelineBandMetrics.StepNotchHeight >= 6d, "the plain step must be visible");
            Assert.IsTrue(TimelineBandMetrics.HalfHourNotchHeight >= 8d, "the half hour notch must be visible");
            Assert.IsTrue(TimelineBandMetrics.HourNotchHeight >= 10d, "the hour notch must be visible");
            Assert.IsTrue(TimelineBandMetrics.MinorNotchOpacity >= 0.5d, "the finest rung keeps half its contrast");
            Assert.IsTrue(TimelineBandMetrics.HourNotchHeight <= 12d,
                "the tallest notch still fits under the ruler labels inside the 13 px ruler row");
        }

        /// <summary>
        /// A tap on a segment always selects it, whether or not the band can seek yet: the seek drag only takes
        /// over once it has really started, and a pan never selects anything.
        /// </summary>
        [DataTestMethod]
        [DataRow(false, false, false, TimelineReleaseIntent.Select, DisplayName = "a live tap selects the segment")]
        [DataRow(true, false, false, TimelineReleaseIntent.Select, DisplayName = "a tap in timeshift still selects")]
        [DataRow(true, true, true, TimelineReleaseIntent.Seek, DisplayName = "a seek drag commits the preview")]
        [DataRow(false, false, true, TimelineReleaseIntent.None, DisplayName = "a pan commits nothing")]
        [DataRow(true, false, true, TimelineReleaseIntent.None, DisplayName = "a broken seek drag commits nothing")]
        public void ReleasingThePointerSelectsEveryTappedSegment(bool dragIsSeek, bool seekStarted, bool dragMoved,
                                                                TimelineReleaseIntent expected)
        {
            Assert.AreEqual(expected, TimelineSegmentActions.ResolveRelease(dragIsSeek, seekStarted, dragMoved));
        }

        [TestMethod]
        public void WindowStartSnapsBackOntoATickBoundary()
        {
            var value = new DateTime(2026, 10, 13, 9, 37, 42, DateTimeKind.Local);
            Assert.AreEqual(new DateTime(2026, 10, 13, 9, 30, 0, DateTimeKind.Local), TimelineBandMetrics.SnapToTick(value, 15));
            Assert.AreEqual(new DateTime(2026, 10, 13, 9, 35, 0, DateTimeKind.Local), TimelineBandMetrics.SnapToTick(value, 5));
            Assert.AreEqual(new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Local), TimelineBandMetrics.SnapToTick(value, 60));
            Assert.AreEqual(value, TimelineBandMetrics.SnapToTick(value, 0), "a broken step must not move the window");
            Assert.AreEqual(DateTimeKind.Local, TimelineBandMetrics.SnapToTick(value, 15).Kind,
                "snapping must not turn a local wall clock into an unspecified one");
        }

        [TestMethod]
        public void PercentAndTimeRoundTripInsideTheWindow()
        {
            var start = new DateTime(2026, 10, 13, 8, 0, 0);
            var span = TimeSpan.FromMinutes(180);

            Assert.AreEqual(0d, TimelineBandMetrics.PercentFor(start, span, start), 0.0001);
            Assert.AreEqual(0.5, TimelineBandMetrics.PercentFor(start, span, start.AddMinutes(90)), 0.0001);
            Assert.AreEqual(1d, TimelineBandMetrics.PercentFor(start, span, start.AddMinutes(180)), 0.0001);
            Assert.AreEqual(-0.5, TimelineBandMetrics.PercentFor(start, span, start.AddMinutes(-90)), 0.0001,
                "a programme that starts before the window keeps a negative fraction so it can be clipped");

            Assert.AreEqual(start.AddMinutes(45), TimelineBandMetrics.TimeAtPercent(start, span, 0.25));
            Assert.AreEqual(start.AddMinutes(180), TimelineBandMetrics.TimeAtPercent(start, span, 1d));
            Assert.AreEqual(0d, TimelineBandMetrics.PercentFor(start, TimeSpan.Zero, start));
        }

        [DataTestMethod]
        [DataRow(-4.5, 0d)]
        [DataRow(0d, 0d)]
        [DataRow(0.42, 0.42)]
        [DataRow(1d, 1d)]
        [DataRow(3.5, 1d)]
        public void FractionsAreClampedToTheTrack(double input, double expected)
        {
            Assert.AreEqual(expected, TimelineBandMetrics.ClampPercent(input), 0.0001);
            Assert.AreEqual(0d, TimelineBandMetrics.ClampPercent(double.NaN), 0.0001);
        }

        /// <summary>A click is resolved to the programme segment drawn under that pixel of the track.</summary>
        [TestMethod]
        public void PointerFractionResolvesToTheProgrammeUnderIt()
        {
            var start = new DateTime(2026, 10, 13, 9, 0, 0);
            var programmes = new System.Collections.Generic.List<TimelineProgramme>
            {
                new TimelineProgramme { Start = start, End = start.AddHours(1) },
                new TimelineProgramme { Start = start.AddHours(1), End = start.AddHours(3) }
            };
            var span = TimeSpan.FromHours(2);

            Assert.AreEqual(0, TimelineBandMetrics.FindProgrammeIndexAtPercent(programmes, start, span, 0.10));
            Assert.AreEqual(0, TimelineBandMetrics.FindProgrammeIndexAtPercent(programmes, start, span, 0.49));
            Assert.AreEqual(1, TimelineBandMetrics.FindProgrammeIndexAtPercent(programmes, start, span, 0.51));
            Assert.AreEqual(1, TimelineBandMetrics.FindProgrammeIndexAtPercent(programmes, start, span, 2.0),
                "an out of range fraction is clamped onto the last visible segment");
            Assert.AreEqual(1, TimelineBandMetrics.FindProgrammeIndexAtPercent(programmes, start, span, 0.5),
                "the exact boundary belongs to the programme that starts there");
            Assert.AreEqual(-1, TimelineBandMetrics.FindProgrammeIndex(null, start));
            Assert.AreEqual(-1, TimelineBandMetrics.FindProgrammeIndex(
                new System.Collections.Generic.List<TimelineProgramme>
                {
                    new TimelineProgramme { Start = start.AddHours(3), End = start.AddHours(4) }
                }, start), "a time that no programme covers resolves to nothing");
        }

        /// <summary>
        /// Prev/next pages the viewport by one programme: the window is re-anchored on the middle of the
        /// neighbouring slot and the playback position is never touched.
        /// </summary>
        [TestMethod]
        public void SegmentPagingAnchorsOnTheNeighbouringProgramme()
        {
            var start = new DateTime(2026, 10, 13, 9, 0, 0);
            var programmes = new System.Collections.Generic.List<TimelineProgramme>
            {
                new TimelineProgramme { Start = start.AddHours(-1), End = start },
                new TimelineProgramme { Start = start, End = start.AddHours(1) },
                new TimelineProgramme { Start = start.AddHours(1), End = start.AddHours(3) }
            };
            var span = TimeSpan.FromMinutes(180);

            Assert.AreEqual(start.AddHours(1) + TimeSpan.FromHours(1),
                TimelineBandMetrics.PageAnchor(programmes, start.AddMinutes(10), span, 1),
                "next anchors on the middle of the following programme");
            Assert.AreEqual(start.AddMinutes(-30),
                TimelineBandMetrics.PageAnchor(programmes, start.AddMinutes(10), span, -1),
                "prev anchors on the middle of the previous programme");
            Assert.AreEqual(start.AddMinutes(-30),
                TimelineBandMetrics.PageAnchor(programmes, start.AddMinutes(10), span, -7),
                "any negative direction pages backwards");
        }

        [TestMethod]
        public void SegmentPagingStillMovesWhenTheScheduleEnds()
        {
            var start = new DateTime(2026, 10, 13, 9, 0, 0);
            var programmes = new System.Collections.Generic.List<TimelineProgramme>
            {
                new TimelineProgramme { Start = start, End = start.AddHours(1) }
            };
            var span = TimeSpan.FromMinutes(180);

            Assert.AreEqual(start.AddMinutes(120),
                TimelineBandMetrics.PageAnchor(programmes, start.AddMinutes(30), span, 1),
                "with nothing ahead the window still advances by half a viewport");
            Assert.AreEqual(start.AddMinutes(-60),
                TimelineBandMetrics.PageAnchor(programmes, start.AddMinutes(30), span, -1));
            Assert.AreEqual(start.AddMinutes(120),
                TimelineBandMetrics.PageAnchor(null, start.AddMinutes(30), span, 1),
                "an empty schedule must not turn the buttons into no-ops");
            Assert.AreEqual(start.AddMinutes(120),
                TimelineBandMetrics.PageAnchor(
                    new System.Collections.Generic.List<TimelineProgramme>
                    {
                        new TimelineProgramme { Start = start.AddHours(5), End = start.AddHours(5) }
                    },
                    start.AddMinutes(30), span, 1),
                "an inverted programme is skipped and the fallback applies");
        }

        [TestMethod]
        public void RulerLabelsCarryTheDateOnlyAcrossMidnight()
        {
            var windowStart = new DateTime(2026, 10, 13, 22, 0, 0);
            Assert.AreEqual("22:30", TimelineBandMetrics.FormatTickLabel(new DateTime(2026, 10, 13, 22, 30, 0), windowStart));
            Assert.AreEqual("10-14 00:00", TimelineBandMetrics.FormatTickLabel(new DateTime(2026, 10, 14, 0, 0, 0), windowStart));
            Assert.AreEqual("10-14 01:00", TimelineBandMetrics.FormatTickLabel(new DateTime(2026, 10, 14, 1, 0, 0), windowStart));
            Assert.AreEqual("23:59", TimelineBandMetrics.FormatTickLabel(new DateTime(2026, 10, 13, 23, 59, 0), windowStart),
                "a tick on the window's own day needs no date prefix");
            Assert.AreEqual("10-14 00:00", TimelineBandMetrics.FormatTickLabel(new DateTime(2026, 10, 14, 0, 0, 0), windowStart),
                "midnight always carries the date even when the window started that same day");
        }

        [TestMethod]
        public void WindowRangeLabelSpansOneOrTwoDates()
        {
            var sameDay = new DateTime(2026, 10, 13, 20, 0, 0);
            Assert.AreEqual("10-13 20:00 - 23:00", TimelineBandMetrics.FormatWindowRange(sameDay, TimeSpan.FromHours(3)));
            Assert.AreEqual("10-13 22:00 - 10-14 01:00", TimelineBandMetrics.FormatWindowRange(sameDay.AddHours(2), TimeSpan.FromHours(3)));
        }

        /// <summary>
        /// The compactness contract: the whole programme band (name/nav row + ruler + segments) must stay in the
        /// same height league as the plain 20 DIP seek bar it replaces, otherwise the user's complaint returns.
        /// </summary>
        [TestMethod]
        public void ProgrammeBandStaysInTheHeightLeagueOfThePlainSeekBar()
        {
            Assert.IsTrue(TimeshiftTimeline.BandRowHeight <= 56,
                $"the programme band grew to {TimeshiftTimeline.BandRowHeight} DIP again");
            Assert.IsTrue(TimeshiftTimeline.BandRowHeight >= 20,
                "the band still has to fit a name row, a ruler and a clickable segment row");
        }

        /// <summary>Settings-Playback switch: the EPG timeline stays the default, so an upgrade changes nothing.</summary>
        [TestMethod]
        public void SeekBarStyleDefaultsToTheEpgTimeline()
        {
            Assert.AreEqual(SeekBarStyle.EpgTimeline, new PlaybackSettings().SeekBar);
        }

        /// <summary>The chosen style survives the settings dialog round trip, and an unknown value falls back.</summary>
        [TestMethod]
        public void SeekBarStyleSurvivesTheSettingsRoundTrip()
        {
            var viewModel = new SettingsPlaybackViewModel();

            var form = viewModel.BuildFormState(new PlaybackSettings { SeekBar = SeekBarStyle.Standard });
            Assert.AreEqual(SeekBarStyle.Standard, form.SeekBar);

            var copy = new PlaybackSettings();
            viewModel.UpdateSettings(copy, form);
            Assert.AreEqual(SeekBarStyle.Standard, copy.SeekBar);

            var broken = form with { SeekBar = (SeekBarStyle) 42 };
            var fallback = new PlaybackSettings();
            viewModel.UpdateSettings(fallback, broken);
            Assert.AreEqual(SeekBarStyle.EpgTimeline, fallback.SeekBar,
                "a hand edited settings file must not be able to poison the seek bar style");
        }

        /// <summary>Both styles are known by name to the combo tags used by the settings window.</summary>
        [TestMethod]
        public void SeekBarStyleNamesMatchTheSettingsComboTags()
        {
            Assert.AreEqual("EpgTimeline", SeekBarStyle.EpgTimeline.ToString());
            Assert.AreEqual("Standard", SeekBarStyle.Standard.ToString());
        }
    }
}
