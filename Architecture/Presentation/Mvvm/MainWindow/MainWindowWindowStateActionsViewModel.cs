using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Helpers;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public class FullscreenContext
    {
        public Window MainWindow { get; set; } = null!;
        public Action SyncTimeshiftUi { get; set; } = delegate { };
        public Action ResetOverlayForOwner { get; set; } = delegate { };
        public Action ShowFsOverlayNow { get; set; } = delegate { };
        public Action<int> TryArrowSeek { get; set; } = delegate { };
        public Action TogglePlayPause { get; set; } = delegate { };
        public Action PositionOverlay { get; set; } = delegate { };
        public Action ShowOverlayWithDelay { get; set; } = delegate { };
        public Action CreateTopOverlay { get; set; } = delegate { };
        public Action SyncEpgVisibility { get; set; } = delegate { };
        /// <summary>While fullscreen the detached drawer / EPG must not reserve grid space.</summary>
        public Action CollapsePanelColumns { get; set; } = delegate { };
        /// <summary>Gives the drawer / EPG columns back to the windowed layout.</summary>
        public Action RestorePanelColumns { get; set; } = delegate { };
        /// <summary>Height of the floating bottom navigation bar, so the fullscreen panels can stop above it.</summary>
        public Func<double>? GetNavigationHeight { get; set; }
        public Func<Key, ModifierKeys, bool>? HandleShortcutKey { get; set; }
    }

    public class MainWindowWindowStateActionsViewModel : ViewModelBase
    {
        private bool _isFullscreen;
        public bool IsFullscreen
        {
            get => _isFullscreen;
            private set => SetProperty(ref _isFullscreen, value);
        }

        public Window? FullscreenEpg { get; set; }
        public Window? FullscreenDrawer { get; set; }
        public TopOverlay? TopOverlay { get; set; }

        /// <summary>Raised when the channel number badge should flash over the video area, which only
        /// happens for channel switches made from the web remote.</summary>
        public event Action<int>? RequestChannelNumberOverlay;

        /// <summary>Asks the shell to flash the given channel number over the video area.</summary>
        public void ShowChannelNumberOverlay(int number) => RequestChannelNumberOverlay?.Invoke(number);

        /// <summary>
        /// Window form captured when fullscreen was entered (null while windowed). Geometry persistence
        /// uses it to store the pre-fullscreen rectangle when the app closes while still fullscreen.
        /// </summary>
        internal WindowFullscreenState? FullscreenRestoreState => _fullscreenState;

        private System.Windows.Controls.Panel? _originalDrawerParent;
        private int _originalDrawerIndex;
        private System.Windows.Controls.Panel? _originalEpgParent;
        private int _originalEpgIndex;

        // Fullscreen now happens on the main window itself; these keep what is needed to undo it.
        private Window? _fullscreenOwner;
        // Context of the running fullscreen session (null while windowed); panel geometry reads it.
        private FullscreenContext? _activeContext;
        private WindowFullscreenState? _fullscreenState;
        private EventHandler? _fsActivated;
        private EventHandler? _fsDeactivated;
        private SizeChangedEventHandler? _fsSizeChanged;
        private EventHandler? _fsLocationChanged;
        private System.Windows.Input.MouseEventHandler? _fsMouseMove;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public double GetTopOverlayOffset()
        {
            // Height of the floating top bar: below it the drawer / EPG panels start.
            var height = 40.0;
            if (TopOverlay != null)
            {
                height = TopOverlay.ActualHeight > 0 ? TopOverlay.ActualHeight : TopOverlay.Height;
                if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0) height = 40;
            }
            return height;
        }

        /// <summary>Runs one step of a window state change without letting a failure escape into a UI event
        /// handler (WPF terminates the process on an unhandled exception there).</summary>
        static void TryRun(Action action, string what)
        {
            try { action(); }
            catch (Exception ex)
            {
                try { LibmpvIptvClient.Diagnostics.Logger.Warn($"[Fullscreen] {what} 失败: {ex.Message}"); } catch { }
            }
        }

        /// <summary>Undoes a failed switch into fullscreen so the main window stays visible and usable.</summary>
        void RollBackFullscreenEntry(FullscreenContext ctx)
        {
            try { IsFullscreen = false; } catch { }
            try { DetachFullscreenEvents(); } catch { }
            try { LibmpvIptvClient.Helpers.WindowFullscreenPresenter.Exit(ctx.MainWindow, _fullscreenState); } catch { }
            _fullscreenState = null;
            try { ctx.MainWindow?.Show(); } catch { }
            try { ctx.MainWindow?.Activate(); } catch { }
        }

        void AttachFullscreenEvents(FullscreenContext ctx)
        {
            var window = ctx.MainWindow;
            if (window == null) return;

            _fullscreenOwner = window;
            _activeContext = ctx;
            _fsActivated = (s, e) => EnsureTopmostZOrder();
            _fsDeactivated = (s, e) =>
            {
                // A deactivated topmost window can end up behind another topmost window; re-assert it.
                try { if (!window.Topmost) window.Topmost = true; } catch { }
            };
            _fsSizeChanged = (s, e) => ctx.PositionOverlay();
            _fsLocationChanged = (s, e) => ctx.PositionOverlay();
            _fsMouseMove = (s, e) => ctx.ShowOverlayWithDelay();

            window.Activated += _fsActivated;
            window.Deactivated += _fsDeactivated;
            window.SizeChanged += _fsSizeChanged;
            window.LocationChanged += _fsLocationChanged;
            window.MouseMove += _fsMouseMove;
        }

        void DetachFullscreenEvents()
        {
            var window = _fullscreenOwner;
            if (window != null)
            {
                try
                {
                    if (_fsActivated != null) window.Activated -= _fsActivated;
                    if (_fsDeactivated != null) window.Deactivated -= _fsDeactivated;
                    if (_fsSizeChanged != null) window.SizeChanged -= _fsSizeChanged;
                    if (_fsLocationChanged != null) window.LocationChanged -= _fsLocationChanged;
                    if (_fsMouseMove != null) window.MouseMove -= _fsMouseMove;
                }
                catch { }
            }

            _fsActivated = null;
            _fsDeactivated = null;
            _fsSizeChanged = null;
            _fsLocationChanged = null;
            _fsMouseMove = null;
            _fullscreenOwner = null;
            _activeContext = null;
        }

        public void ToggleFullscreen(bool on, FullscreenContext ctx)
        {
            if (on == IsFullscreen) return;

            if (on)
            {
                IsFullscreen = true;

                // The main window becomes the fullscreen window: no second HWND is created and the mpv
                // 'wid' (VideoPanel.Handle) never changes, so the player is not reinitialised (I1/I3).
                _fullscreenState = LibmpvIptvClient.Helpers.WindowFullscreenPresenter.Enter(
                    ctx.MainWindow, message => LibmpvIptvClient.Diagnostics.Logger.Info(message));
                if (_fullscreenState == null)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Error("[Fullscreen] 进入全屏失败，正在回滚");
                    RollBackFullscreenEntry(ctx);
                    return;
                }

                // The drawer / EPG are detached into their own windows while fullscreen: their columns
                // must not keep reserving space, otherwise the video is squeezed by an empty column.
                TryRun(() => ctx.CollapsePanelColumns(), "CollapsePanelColumns");

                AttachFullscreenEvents(ctx);

                // Overlay work is decorative: a failure here must not take the whole switch down.
                TryRun(() => ctx.CreateTopOverlay(), "CreateTopOverlay");
                TryRun(() => ctx.ResetOverlayForOwner(), "ResetOverlayForOwner");
                TryRun(() => ctx.ShowFsOverlayNow(), "ShowFsOverlayNow");
                TryRun(() => ctx.SyncTimeshiftUi(), "SyncTimeshiftUi");
            }
            else
            {
                IsFullscreen = false;

                try
                {
                    // Fix: Close sidebars FIRST to ensure panels are returned to main window
                    if (FullscreenDrawer != null && FullscreenDrawer.Content is FrameworkElement drawerPanel)
                    {
                        CloseFullscreenDrawer(ctx.MainWindow, drawerPanel);
                    }
                    if (FullscreenEpg != null && FullscreenEpg.Content is FrameworkElement epgPanel)
                    {
                        CloseFullscreenEpg(ctx.MainWindow, epgPanel);
                    }

                    if (TopOverlay != null)
                    {
                        TopOverlay.Close();
                        TopOverlay = null;
                    }
                }
                catch (Exception ex)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Error($"[Fullscreen] 退出全屏清理失败: {ex}");
                }
                finally
                {
                    // The window form has to come back whatever happened above, otherwise the app keeps
                    // running as a fullscreen window with a broken layout.
                    DetachFullscreenEvents();
                    try
                    {
                        LibmpvIptvClient.Helpers.WindowFullscreenPresenter.Exit(
                            ctx.MainWindow, _fullscreenState, message => LibmpvIptvClient.Diagnostics.Logger.Info(message));
                    }
                    catch (Exception ex)
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Error($"[Fullscreen] 还原窗口形态失败: {ex}");
                    }
                    _fullscreenState = null;
                }

                TryRun(() => ctx.RestorePanelColumns(), "RestorePanelColumns");
                TryRun(() => ctx.ResetOverlayForOwner(), "ResetOverlayForOwner");
                TryRun(() => ctx.SyncTimeshiftUi(), "SyncTimeshiftUi");

                // Fix: Explicitly sync EPG visibility when returning to windowed mode
                TryRun(() => ctx.SyncEpgVisibility?.Invoke(), "SyncEpgVisibility");

                // Ensure focus returns to main window
                try { ctx.MainWindow?.Focus(); } catch { }
            }
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        private void EnsureTopmostZOrder()
        {
            // Fullscreen is applied to the main window itself now, so the window that has to be pushed
            // back to the top of the top-most band is always the main window.
            if (_fullscreenOwner == null) return;
            try
            {
                _fullscreenOwner.Topmost = false;
                _fullscreenOwner.Topmost = true;
            }
            catch { }
        }

        /// <summary>
        /// Aligns the fullscreen EPG / drawer with the main window: they start below the auto-hiding top
        /// bar and stop above the floating bottom navigation bar, which overlays the bottom of the video.
        /// </summary>
        public void PositionFullscreenPanels() => PositionFullscreenPanelsFor(_fullscreenOwner);

        void PositionFullscreenPanelsFor(Window? window)
        {
            if (window == null) return;
            try
            {
                var topOffset = GetTopOverlayOffset();
                var navReserve = _activeContext?.GetNavigationHeight?.Invoke() ?? 0;
                var height = Math.Max(0, window.ActualHeight - topOffset - navReserve);

                if (FullscreenEpg != null)
                {
                    FullscreenEpg.Left = window.Left;
                    FullscreenEpg.Top = window.Top + topOffset;
                    FullscreenEpg.Height = height;
                }

                if (FullscreenDrawer != null)
                {
                    FullscreenDrawer.Left = window.Left + window.ActualWidth - FullscreenDrawer.Width;
                    FullscreenDrawer.Top = window.Top + topOffset;
                    FullscreenDrawer.Height = height;
                }
            }
            catch { }
        }

        public void ShowFullscreenDrawer(Window mainWindow, FrameworkElement drawerPanel, double width)
        {
            if (FullscreenDrawer != null) return;
            System.Windows.Controls.Panel? p = drawerPanel.Parent as System.Windows.Controls.Panel;
            if (p != null)
            {
                _originalDrawerParent = p;
                _originalDrawerIndex = p.Children.IndexOf(drawerPanel);
                p.Children.Remove(drawerPanel);
            }

            FullscreenDrawer = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Topmost = true,
                ShowInTaskbar = false,
                Width = width > 0 ? width : 380,
                ResizeMode = ResizeMode.NoResize,
                Content = drawerPanel,
                Owner = mainWindow,
                DataContext = mainWindow.DataContext
            };
            
            // Same translucent scrim as the floating bottom navigation bar, so the video keeps showing
            // through the panel instead of the opaque card colour used by the windowed layout.
            FullscreenDrawer.SetResourceReference(Window.BackgroundProperty, "TopOverlayScrimBrush");
            FullscreenDrawer.Resources["CardBackgroundBrush"] = System.Windows.Media.Brushes.Transparent;
            PositionFullscreenPanelsFor(mainWindow);
            
            try 
            { 
                drawerPanel.UpdateLayout(); 
                if (drawerPanel is FrameworkElement fe)
                {
                    if (fe.DataContext == null) fe.DataContext = mainWindow.DataContext;
                    fe.Visibility = Visibility.Visible;
                }
            } 
            catch { }
            
            FullscreenDrawer.Show();
        }

        public void CloseFullscreenDrawer(Window mainWindow, FrameworkElement drawerPanel)
        {
            if (FullscreenDrawer == null) return;
            
            // Fix: Detach content before closing to avoid "Element is already child of another"
            FullscreenDrawer.Content = null;
            FullscreenDrawer.Close();
            FullscreenDrawer = null;

            // Fix: Ensure we put it back EXACTLY where it was
            // And force update layout to prevent "Empty" look
            if (_originalDrawerParent != null)
            {
                if (!_originalDrawerParent.Children.Contains(drawerPanel))
                {
                    try 
                    {
                        if (_originalDrawerIndex >= 0 && _originalDrawerIndex <= _originalDrawerParent.Children.Count)
                            _originalDrawerParent.Children.Insert(_originalDrawerIndex, drawerPanel);
                        else
                            _originalDrawerParent.Children.Add(drawerPanel);
                            
                        // Fix: Restore DataContext explicitly if needed
                        // Usually it inherits, but if reparenting broke it...
                        // drawerPanel.DataContext = mainWindow.DataContext; 
                        
                        // Fix: Reset local visibility to ensure binding takes over
                        drawerPanel.ClearValue(UIElement.VisibilityProperty);
                        Grid.SetRow(drawerPanel, 1);
                        Grid.SetColumn(drawerPanel, 2);
                        Grid.SetRowSpan(drawerPanel, 1);
                    }
                    catch { }
                }
                _originalDrawerParent = null;
            }
        }

        public void ShowFullscreenEpg(Window mainWindow, FrameworkElement epgPanel)
        {
            if (FullscreenEpg != null) return;
            System.Windows.Controls.Panel? p = epgPanel.Parent as System.Windows.Controls.Panel;
            if (p != null)
            {
                _originalEpgParent = p;
                _originalEpgIndex = p.Children.IndexOf(epgPanel);
                p.Children.Remove(epgPanel);
            }

            FullscreenEpg = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Topmost = true,
                ShowInTaskbar = false,
                Width = 320,
                ResizeMode = ResizeMode.NoResize,
                Content = epgPanel,
                Owner = mainWindow,
                DataContext = mainWindow.DataContext
            };
            
            // Same translucent scrim as the floating bottom navigation bar, so the video keeps showing
            // through the panel instead of the opaque card colour used by the windowed layout.
            FullscreenEpg.SetResourceReference(Window.BackgroundProperty, "TopOverlayScrimBrush");
            FullscreenEpg.Resources["CardBackgroundBrush"] = System.Windows.Media.Brushes.Transparent;
            PositionFullscreenPanelsFor(mainWindow);
            
            try 
            { 
                epgPanel.UpdateLayout(); 
                if (epgPanel is FrameworkElement fe)
                {
                    if (fe.DataContext == null) fe.DataContext = mainWindow.DataContext;
                    fe.Visibility = Visibility.Visible;
                }
            } 
            catch { }
            
            FullscreenEpg.Show();
        }

        public void CloseFullscreenEpg(Window mainWindow, FrameworkElement epgPanel)
        {
            if (FullscreenEpg == null) return;
            
            // Fix: Detach content before closing
            FullscreenEpg.Content = null;
            FullscreenEpg.Close();
            FullscreenEpg = null;

            if (_originalEpgParent != null)
            {
                if (!_originalEpgParent.Children.Contains(epgPanel))
                {
                    try
                    {
                        if (_originalEpgIndex >= 0 && _originalEpgIndex <= _originalEpgParent.Children.Count)
                            _originalEpgParent.Children.Insert(_originalEpgIndex, epgPanel);
                        else
                            _originalEpgParent.Children.Add(epgPanel);
                            
                        // Fix: Restore DataContext explicitly if needed
                        // epgPanel.DataContext = mainWindow.DataContext;
                        
                        // Fix: Reset local visibility to ensure binding takes over
                        epgPanel.ClearValue(UIElement.VisibilityProperty);
                        Grid.SetRow(epgPanel, 1);
                        Grid.SetColumn(epgPanel, 0);
                        Grid.SetRowSpan(epgPanel, 1);
                    }
                    catch { }
                }
                _originalEpgParent = null;
            }
        }
    }
}
