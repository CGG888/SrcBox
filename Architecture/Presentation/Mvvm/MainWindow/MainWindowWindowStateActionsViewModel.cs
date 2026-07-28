using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public class FullscreenContext
    {
        public Window MainWindow { get; set; } = null!;
        public MpvInterop? Mpv { get; set; }
        public System.Windows.Forms.Panel? WindowedPanel { get; set; }
        public Action SyncTimeshiftUi { get; set; } = delegate { };
        public Action ResetOverlayForOwner { get; set; } = delegate { };
        public Action ShowFsOverlayNow { get; set; } = delegate { };
        public Action<int> TryArrowSeek { get; set; } = delegate { };
        public Action TogglePlayPause { get; set; } = delegate { };
        public Action PositionOverlay { get; set; } = delegate { };
        public Action ShowOverlayWithDelay { get; set; } = delegate { };
        public Action<System.Windows.Input.MouseWheelEventArgs> FsHostPreviewMouseWheel { get; set; } = delegate { };
        public Action<System.Windows.Forms.MouseEventArgs> FsPanelMouseWheel { get; set; } = delegate { };
        public Action<System.Windows.Forms.MouseEventArgs> FsPanelMouseUp { get; set; } = delegate { };
        public Action FsPanelDoubleClick { get; set; } = delegate { };
        public Action CreateTopOverlay { get; set; } = delegate { };
        public RoutedEventHandler OnLoaded { get; set; } = delegate { };
        public Action SyncEpgVisibility { get; set; } = delegate { };
        public Func<Key, bool>? HandleShortcutKey { get; set; }
    }

    public class MainWindowWindowStateActionsViewModel : ViewModelBase
    {
        private bool _isFullscreen;
        public bool IsFullscreen
        {
            get => _isFullscreen;
            private set => SetProperty(ref _isFullscreen, value);
        }

        public FullscreenWindow? FullscreenWindow { get; set; }
        public System.Windows.Forms.Panel? FullscreenPanel { get; set; }
        public Window? FullscreenEpg { get; set; }
        public Window? FullscreenDrawer { get; set; }
        public TopOverlay? TopOverlay { get; set; }

        private System.Windows.Controls.Panel? _originalDrawerParent;
        private int _originalDrawerIndex;
        private System.Windows.Controls.Panel? _originalEpgParent;
        private int _originalEpgIndex;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public double GetTopOverlayOffset()
        {
            if (FullscreenWindow == null) return 40;
            if (TopOverlay == null) return 40;
            var height = TopOverlay.ActualHeight;
            if (height <= 0) height = TopOverlay.Height;
            if (height <= 0) height = 40;
            var offset = (TopOverlay.Top + height) - FullscreenWindow.Top;
            if (double.IsNaN(offset) || double.IsInfinity(offset) || offset <= 0) offset = height;
            return offset;
        }

        public void ToggleFullscreen(bool on, FullscreenContext ctx)
        {
            if (on == IsFullscreen) return;

            if (on)
            {
                IsFullscreen = true;
                // updateUiState(true); // Removed, handled by binding

                FullscreenWindow = new FullscreenWindow();
                FullscreenWindow.Topmost = true;
                FullscreenWindow.Owner = ctx.MainWindow;
                FullscreenWindow.Loaded += ctx.OnLoaded;
                FullscreenPanel = FullscreenWindow.VideoPanel;

                if (ctx.Mpv != null && FullscreenPanel != null)
                {
                    try
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Log("[Fullscreen] Setting mpv parent to FullscreenPanel");
                        ctx.Mpv.SetWid(FullscreenPanel.Handle);
                    }
                    catch (Exception ex)
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Error($"[Fullscreen] Failed to set mpv parent: {ex}");
                    }
                }

                // Event binding
                FullscreenWindow.ExitRequested += () => ToggleFullscreen(false, ctx);
                FullscreenWindow.PlayPauseRequested += ctx.TogglePlayPause;
                FullscreenWindow.SeekRequested += ctx.TryArrowSeek;
                FullscreenWindow.ShortcutKeyPressed += (key) => ctx.HandleShortcutKey?.Invoke(key) ?? false;

                if (FullscreenPanel != null)
                {
                    FullscreenPanel.DoubleClick += (s, e) => ctx.FsPanelDoubleClick();
                    FullscreenPanel.MouseWheel += (s, e) => ctx.FsPanelMouseWheel(e);
                    FullscreenPanel.MouseUp += (s, e) => ctx.FsPanelMouseUp(e);
                    FullscreenPanel.MouseMove += (s, e) => ctx.ShowOverlayWithDelay();
                    FullscreenPanel.MouseEnter += (s, e) => ctx.ShowOverlayWithDelay();
                }

                FullscreenWindow.Host.PreviewMouseWheel += (s, e) => ctx.FsHostPreviewMouseWheel(e);
                FullscreenWindow.Host.MouseMove += (s, e) => ctx.ShowOverlayWithDelay();
                FullscreenWindow.Host.PreviewMouseMove += (s, e) => ctx.ShowOverlayWithDelay();

                FullscreenWindow.Activated += (s, e) => EnsureTopmostZOrder();
                FullscreenWindow.StateChanged += (s, e) =>
                {
                    if (FullscreenWindow.WindowState != WindowState.Maximized)
                    {
                        try { FullscreenWindow.WindowState = WindowState.Maximized; } catch { }
                    }
                };
                FullscreenWindow.Deactivated += (s, e) =>
                {
                    try { if (FullscreenWindow != null && !FullscreenWindow.Topmost) FullscreenWindow.Topmost = true; } catch { }
                };
                FullscreenWindow.SizeChanged += (s, e) => ctx.PositionOverlay();
                FullscreenWindow.LocationChanged += (s, e) => ctx.PositionOverlay();
                FullscreenWindow.MouseMove += (s, e) => ctx.ShowOverlayWithDelay();

                FullscreenWindow.Show();
                FullscreenWindow.Focus();
                
                ctx.CreateTopOverlay();
                
                ctx.ResetOverlayForOwner();
                ctx.ShowFsOverlayNow();
                ctx.SyncTimeshiftUi();
            }
            else
            {
                IsFullscreen = false;
                // updateUiState(false); // Removed, handled by binding

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

                if (ctx.Mpv != null && ctx.WindowedPanel != null)
                {
                    try
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Log("[Fullscreen] Restoring mpv parent to WindowedPanel");
                        ctx.Mpv.SetWid(ctx.WindowedPanel.Handle);
                    }
                    catch (Exception ex)
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Error($"[Fullscreen] Failed to restore mpv parent: {ex}");
                    }
                }

                if (FullscreenWindow != null)
                {
                    FullscreenWindow.Close();
                    FullscreenWindow = null;
                    FullscreenPanel = null;
                }
                
                ctx.ResetOverlayForOwner();
                ctx.SyncTimeshiftUi();
                
                // Fix: Explicitly sync EPG visibility when returning to windowed mode
                ctx.SyncEpgVisibility?.Invoke();
                
                // Ensure focus returns to main window
                ctx.MainWindow.Focus();
            }
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        private void EnsureTopmostZOrder()
        {
            // Logic remains same but simplified if possible
            if (FullscreenWindow == null) return;
            try
            {
                FullscreenWindow.Topmost = false;
                FullscreenWindow.Topmost = true;
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
                Owner = FullscreenWindow ?? mainWindow,
                DataContext = mainWindow.DataContext
            };
            
            FullscreenDrawer.SetResourceReference(Window.BackgroundProperty, "CardBackgroundBrush");
            if (FullscreenWindow != null)
            {
                var topOffset = GetTopOverlayOffset();
                FullscreenDrawer.Left = FullscreenWindow.Left + FullscreenWindow.ActualWidth - FullscreenDrawer.Width;
                FullscreenDrawer.Top = FullscreenWindow.Top + topOffset;
                FullscreenDrawer.Height = Math.Max(0, FullscreenWindow.ActualHeight - topOffset);
            }
            
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
                Owner = FullscreenWindow ?? mainWindow,
                DataContext = mainWindow.DataContext
            };
            
            FullscreenEpg.SetResourceReference(Window.BackgroundProperty, "CardBackgroundBrush");
            if (FullscreenWindow != null)
            {
                var topOffset = GetTopOverlayOffset();
                FullscreenEpg.Left = FullscreenWindow.Left;
                FullscreenEpg.Top = FullscreenWindow.Top + topOffset;
                FullscreenEpg.Height = Math.Max(0, FullscreenWindow.ActualHeight - topOffset);
            }
            
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
