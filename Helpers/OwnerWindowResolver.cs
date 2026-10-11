using System.Linq;

namespace LibmpvIptvClient.Helpers
{
    /// <summary>
    /// Resolves the window that auxiliary top-level windows (M3U list, reminder list, toasts) should be
    /// owned by. Fullscreen is applied to the main window itself, so there is a single answer: the main
    /// window.
    /// </summary>
    public static class OwnerWindowResolver
    {
        /// <summary>The running main window, or <c>null</c> while it is not shown yet.</summary>
        public static MainWindow? MainWindow
        {
            get
            {
                try { return System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault(); }
                catch { return null; }
            }
        }

        /// <summary>
        /// True while the main window sits in the top-most band - which it always does in fullscreen.
        /// An owned window has to be top-most as well then: the video is a WinForms child HWND inside the
        /// main window, so a non-top-most owned window would end up behind it.
        /// </summary>
        public static bool IsOwnerTopmost
        {
            get
            {
                try { return MainWindow?.Topmost == true; }
                catch { return false; }
            }
        }
    }
}
