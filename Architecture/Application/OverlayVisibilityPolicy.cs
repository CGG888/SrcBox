using System;

namespace LibmpvIptvClient
{
    internal static class OverlayVisibilityPolicy
    {
        public static bool ShouldShowBottom(double screenHeight, double mouseY, double minPixels, double percent, bool isMenuActive)
        {
            var bottomZone = Math.Max(minPixels, screenHeight * percent);
            if (screenHeight <= 0) return false;
            if (mouseY > screenHeight - bottomZone) return true;
            if (isMenuActive) return true;
            return false;
        }
        public static bool ShouldShowTop(double mouseY, double topPixels, bool isMenuActive)
        {
            if (mouseY < topPixels) return true;
            if (isMenuActive) return true;
            return false;
        }
    }
}
