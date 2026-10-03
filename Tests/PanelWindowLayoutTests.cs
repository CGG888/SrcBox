using LibmpvIptvClient.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class PanelWindowLayoutTests
    {
        const double Base = 1280;
        const double Drawer = 380;

        // Regression: opening and closing the channel list used to grow the window by the drawer
        // width on every cycle, because the base was re-derived from the window size while the panel
        // flag had not been updated yet.
        [TestMethod]
        public void PanelToggles_KeepTheBaseWidthStable()
        {
            var baseWidth = Base;

            for (int i = 0; i < 3; i++)
            {
                // open the drawer
                var opened = PanelWindowLayout.WindowWidth(baseWidth, drawerCollapsed: false, Drawer, epgVisible: false);
                Assert.IsFalse(PanelWindowLayout.IsUserResize(opened, opened),
                    "our own panel resize must not be mistaken for a user resize");
                baseWidth = PanelWindowLayout.BaseWidthFromWindow(opened, drawerCollapsed: false, Drawer, epgVisible: false);

                // close it again
                var closed = PanelWindowLayout.WindowWidth(baseWidth, drawerCollapsed: true, Drawer, epgVisible: false);
                Assert.IsFalse(PanelWindowLayout.IsUserResize(closed, closed));
                baseWidth = PanelWindowLayout.BaseWidthFromWindow(closed, drawerCollapsed: true, Drawer, epgVisible: false);

                Assert.AreEqual(Base, baseWidth, 0.01, $"cycle {i + 1} must not grow the base width");
            }
        }

        // A real resize while a panel is open still redefines the playback area.
        [TestMethod]
        public void UserResize_RedefinesBaseWidth()
        {
            var applied = PanelWindowLayout.WindowWidth(Base, drawerCollapsed: false, Drawer, epgVisible: false);
            var dragged = applied + 100;

            Assert.IsTrue(PanelWindowLayout.IsUserResize(dragged, applied));

            var baseWidth = PanelWindowLayout.BaseWidthFromWindow(dragged, drawerCollapsed: false, Drawer, epgVisible: false);
            Assert.AreEqual(Base + 100, baseWidth, 0.01);
        }

        [TestMethod]
        public void EpgAndDrawer_AddOnTopOfBase()
        {
            Assert.AreEqual(Base + Drawer + 320,
                PanelWindowLayout.WindowWidth(Base, drawerCollapsed: false, Drawer, epgVisible: true), 0.01);
            Assert.AreEqual(Base,
                PanelWindowLayout.WindowWidth(Base, drawerCollapsed: true, 0, epgVisible: false), 0.01);
            Assert.AreEqual(Base,
                PanelWindowLayout.BaseWidthFromWindow(Base + Drawer + 320, drawerCollapsed: false, Drawer, epgVisible: true), 0.01);
        }

        // Minimal mode shows the playback area only: the window shrinks to the video area width (it
        // used to keep the full width and hand the freed panel space to the video), the panels cannot
        // be opened there, and leaving minimal mode restores the full layout.
        [TestMethod]
        public void MinimalMode_UsesPlaybackAreaWidthOnly()
        {
            var full = PanelWindowLayout.WindowWidth(Base, drawerCollapsed: false, Drawer, epgVisible: true);
            Assert.AreEqual(Base + Drawer + 320, full, 0.01);

            var minimal = PanelWindowLayout.MinimalWindowWidth(Base);
            Assert.AreEqual(Base, minimal, 0.01);

            Assert.IsFalse(PanelWindowLayout.CanOpenPanels(minimalMode: true), "panels stay closed in minimal mode");
            Assert.IsTrue(PanelWindowLayout.CanOpenPanels(minimalMode: false));

            var restored = PanelWindowLayout.WindowWidth(Base, drawerCollapsed: false, Drawer, epgVisible: true);
            Assert.AreEqual(full, restored, 0.01, "leaving minimal mode must restore the full layout width");
        }
    }
}
