using System;
using LibmpvIptvClient.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Geometry of the fullscreen EPG / channel-list overlays. They are floating cards: shorter than the
    /// video area, centred between the top bar and the bottom navigation bar, and inset from the screen
    /// edge they are docked to so the panel frame stays inside the fullscreen picture.
    /// </summary>
    [TestClass]
    public class FullscreenPanelLayoutTests
    {
        // A 4K fullscreen: the top overlay is 40 tall, the bottom navigation overlay reserves 80.
        private const double WindowTop = 0;
        private const double WindowHeight = 2160;
        private const double TopOffset = 40;
        private const double NavReserve = 80;
        private const double Available = WindowHeight - TopOffset - NavReserve; // 2040

        [TestMethod]
        public void Panel_IsShorterThanTheUsableFullscreenArea()
        {
            var (_, height) = PanelWindowLayout.FullscreenPanelVertical(WindowTop, WindowHeight, TopOffset, NavReserve);

            Assert.AreEqual(Available * PanelWindowLayout.FullscreenPanelHeightRatio, height, 0.001);
            Assert.IsTrue(height < Available, "a fullscreen overlay panel must not run the full height of the screen");
        }

        [TestMethod]
        public void Panel_HasTheSameSpaceAboveAndBelow()
        {
            var (top, height) = PanelWindowLayout.FullscreenPanelVertical(WindowTop, WindowHeight, TopOffset, NavReserve);

            var above = top - (WindowTop + TopOffset);
            var below = (WindowTop + TopOffset + Available) - (top + height);

            Assert.AreEqual(above, below, 0.001, "the panel has to be centred in the usable fullscreen area");
            Assert.IsTrue(above > 0, "the panel needs breathing room below the top bar");
        }

        [TestMethod]
        public void Panel_StaysInsideTheScreenEdgeItIsDockedTo()
        {
            var epgLeft = PanelWindowLayout.FullscreenEpgLeft(0);
            var drawerLeft = PanelWindowLayout.FullscreenDrawerLeft(0, 3840, 380);

            Assert.AreEqual(PanelWindowLayout.FullscreenPanelOuterMargin, epgLeft,
                "the left EPG panel must be inset from the left screen edge");
            Assert.AreEqual(3840 - 380 - PanelWindowLayout.FullscreenPanelOuterMargin, drawerLeft,
                "the right drawer panel must be inset from the right screen edge");
            Assert.IsTrue(drawerLeft + 380 < 3840, "the drawer frame must not land on the last screen pixel");
        }

        [TestMethod]
        public void Panel_CollapsesInsteadOfGoingNegative_WhenTheScreenIsTooShort()
        {
            var (top, height) = PanelWindowLayout.FullscreenPanelVertical(0, 100, 40, 80);

            Assert.AreEqual(0, height, "a usable height below zero must clamp to zero, not flip the panel upside down");
            Assert.AreEqual(40, top, 0.001, "the panel still starts below the top bar");
        }

        [TestMethod]
        public void Panel_FollowsTheWindowOrigin()
        {
            var (top, _) = PanelWindowLayout.FullscreenPanelVertical(120, WindowHeight, TopOffset, NavReserve);

            Assert.AreEqual(120 + TopOffset + (Available - Available * PanelWindowLayout.FullscreenPanelHeightRatio) / 2, top, 0.001);
            Assert.AreEqual(120 + PanelWindowLayout.FullscreenPanelOuterMargin, PanelWindowLayout.FullscreenEpgLeft(120), 0.001);
        }
    }
}
