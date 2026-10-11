namespace LibmpvIptvClient.Helpers
{
    /// <summary>
    /// Window width maths for the drawer/EPG panels. The playback area must keep the width the user
    /// chose, so the panels are added on top of a stable base width instead of the window being
    /// recomputed from its current size (which used to add the drawer width on every open/close).
    /// </summary>
    public static class PanelWindowLayout
    {
        public const double EpgWidth = 320;
        public const double DefaultDrawerWidth = 380;
        public const double MinBaseWidth = 480;
        public const double ResizeTolerance = 0.5;

        public static double DrawerWidth(bool drawerCollapsed, double configuredDrawerWidth)
            => drawerCollapsed ? 0 : (configuredDrawerWidth > 0 ? configuredDrawerWidth : DefaultDrawerWidth);

        public static double EpgWidthFor(bool epgVisible) => epgVisible ? EpgWidth : 0;

        /// <summary>Window width for the given base (video area) width and panel state.</summary>
        public static double WindowWidth(double baseWidth, bool drawerCollapsed, double configuredDrawerWidth, bool epgVisible)
            => baseWidth + DrawerWidth(drawerCollapsed, configuredDrawerWidth) + EpgWidthFor(epgVisible);

        /// <summary>Base (video area) width implied by a window width and the current panel state.</summary>
        public static double BaseWidthFromWindow(double windowWidth, bool drawerCollapsed, double configuredDrawerWidth, bool epgVisible)
            => windowWidth - DrawerWidth(drawerCollapsed, configuredDrawerWidth) - EpgWidthFor(epgVisible);

        /// <summary>
        /// Minimal mode hides the chrome and both panels, so only the playback area is shown: the
        /// window must shrink to the base (video area) width instead of absorbing the freed panels.
        /// </summary>
        public static double MinimalWindowWidth(double baseWidth) => baseWidth;

        /// <summary>Panels cannot be opened in minimal mode - only the playback area is shown.</summary>
        public static bool CanOpenPanels(bool minimalMode) => !minimalMode;

        /// <summary>True when a size change came from an actual resize rather than from the width we applied
        /// for a panel toggle.
        /// </summary>
        public static bool IsUserResize(double currentWidth, double appliedWidth)
            => double.IsNaN(appliedWidth) || System.Math.Abs(currentWidth - appliedWidth) >= ResizeTolerance;

        /// <summary>
        /// Share of the usable fullscreen height an overlay panel occupies. In fullscreen the panels are
        /// floating cards, not full-height columns: the remaining share is split evenly above and below,
        /// which keeps the list readable without it running from the top bar to the bottom bar.
        /// </summary>
        public const double FullscreenPanelHeightRatio = 0.72;

        /// <summary>
        /// Gap between an overlay panel and the screen edge it is docked to. The panel frame is drawn on
        /// the outermost pixel, so without a gap it lands exactly on the edge of the fullscreen picture
        /// and the border looks cut off instead of sitting inside the fullscreen area.
        /// </summary>
        public const double FullscreenPanelOuterMargin = 16;

        /// <summary>
        /// Vertical placement of an overlay panel: a window-relative top and a height that is centred in
        /// the usable area (the screen minus the auto-hiding top bar and the floating bottom navigation
        /// bar), so the spacing above and below the panel is identical.
        /// </summary>
        public static (double Top, double Height) FullscreenPanelVertical(
            double windowTop, double windowHeight, double topOffset, double navReserve)
        {
            var available = System.Math.Max(0, windowHeight - topOffset - navReserve);
            var height = available * FullscreenPanelHeightRatio;
            return (windowTop + topOffset + (available - height) / 2, height);
        }

        /// <summary>Left edge of the fullscreen EPG panel, inset from the left screen edge.</summary>
        public static double FullscreenEpgLeft(double windowLeft)
            => windowLeft + FullscreenPanelOuterMargin;

        /// <summary>Left edge of the fullscreen drawer panel; its right edge is inset from the screen edge.</summary>
        public static double FullscreenDrawerLeft(double windowLeft, double windowWidth, double panelWidth)
            => windowLeft + windowWidth - panelWidth - FullscreenPanelOuterMargin;
    }
}
