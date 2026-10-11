using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace LibmpvIptvClient.Helpers
{
    /// <summary>The window form captured before a fullscreen switch, so it can be restored exactly.</summary>
    public sealed record WindowFullscreenState(
        WindowStyle Style,
        ResizeMode ResizeMode,
        WindowChrome? Chrome,
        WindowState State,
        double Left,
        double Top,
        double Width,
        double Height,
        bool Topmost);

    /// <summary>
    /// Switches the main window itself to fullscreen without replacing its HWND: only the window chrome,
    /// the resize mode and the geometry change, so every child HWND (in particular the mpv 'wid') stays
    /// valid and the player is never reinitialised. The P0 spike measured that none of these properties
    /// rebuilds the window handle (see tasks/fullscreen-single-window/P1-window-form.md, "P0 结论").
    /// </summary>
    public static class WindowFullscreenPresenter
    {
        private static readonly WindowChrome FramelessChrome = new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        };

        /// <summary>Makes <paramref name="window"/> cover its screen. Returns the saved form, or null when the
        /// switch failed (the caller has to roll back).</summary>
        public static WindowFullscreenState? Enter(Window window, Action<string>? log = null)
        {
            if (window == null) return null;
            try
            {
                var state = new WindowFullscreenState(
                    window.WindowStyle,
                    window.ResizeMode,
                    WindowChrome.GetWindowChrome(window),
                    window.WindowState,
                    window.Left,
                    window.Top,
                    window.Width,
                    window.Height,
                    window.Topmost);

                var handle = new WindowInteropHelper(window).Handle;
                var bounds = System.Windows.Forms.Screen.FromHandle(handle).Bounds;

                // Screen.Bounds is reported in device pixels while WPF positions windows in DIP.
                var dpi = VisualTreeHelper.GetDpi(window);
                var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
                var left = bounds.Left / scaleX;
                var top = bounds.Top / scaleY;
                var width = bounds.Width / scaleX;
                var height = bounds.Height / scaleY;

                WindowChrome.SetWindowChrome(window, FramelessChrome);
                window.ResizeMode = ResizeMode.NoResize;

                // A maximized window ignores Left/Top/Width/Height, so normalise the state first.
                window.WindowState = WindowState.Normal;
                window.Left = left;
                window.Top = top;
                window.Width = width;
                window.Height = height;
                window.Topmost = true;

                log?.Invoke($"[Fullscreen] enter: screen={bounds.Left},{bounds.Top} {bounds.Width}x{bounds.Height} dpi={scaleX:F2} dip={width:F0}x{height:F0}");
                return state;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[Fullscreen] enter failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Restores the window form captured by <see cref="Enter"/>.</summary>
        public static void Exit(Window window, WindowFullscreenState? state, Action<string>? log = null)
        {
            if (window == null || state == null) return;
            try
            {
                window.Topmost = state.Topmost;

                // The style and the resize mode are restored *before* the chrome, never after: changing
                // them makes Windows repaint the non-client frame, and only the chrome set afterwards
                // hides the system caption buttons again. With the chrome set first, Windows draws its own
                // minimize/maximize/close buttons (and the caption bottom border) on top of the app's own
                // title bar as soon as the window returns from fullscreen.
                window.WindowStyle = state.Style;
                window.ResizeMode = state.ResizeMode;
                WindowChrome.SetWindowChrome(window, state.Chrome!);

                if (state.State == WindowState.Maximized)
                {
                    // Go through Normal: assigning the saved bounds while maximized would corrupt them.
                    window.WindowState = WindowState.Normal;
                    window.Left = state.Left;
                    window.Top = state.Top;
                    window.Width = state.Width;
                    window.Height = state.Height;
                    window.WindowState = WindowState.Maximized;
                }
                else
                {
                    window.WindowState = state.State;
                    window.Left = state.Left;
                    window.Top = state.Top;
                    window.Width = state.Width;
                    window.Height = state.Height;
                }

                // Recalculate the non-client frame once the restored geometry has settled: this is what
                // makes Windows drop the system caption buttons for good.
                RefreshWindowFrame(window);

                log?.Invoke("[Fullscreen] exit: window form restored");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[Fullscreen] exit failed: {ex.Message}");
            }
        }

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        /// <summary>Forces one non-client frame recalculation of <paramref name="window"/> without touching its
        /// geometry. It runs after the layout pass so the restored bounds are already applied.</summary>
        private static void RefreshWindowFrame(Window window)
        {
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    if (handle == IntPtr.Zero) return;
                    SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                        SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                }
                catch
                {
                    // A missed frame refresh only leaves the window usable but slightly miss-styled.
                }
            }), DispatcherPriority.Loaded);
        }
    }
}
