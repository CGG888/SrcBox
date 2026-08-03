using System;
using System.Windows;
using System.Windows.Threading;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Controls;
using System.Runtime.InteropServices;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowOverlayManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;
        
        private OverlayControls? _overlayWpf;
        private DispatcherTimer _overlayHideTimer = new DispatcherTimer();
        private DispatcherTimer _overlayPollTimer = new DispatcherTimer();
        private DispatcherTimer? _timeshiftResyncTimer;
        
        private DateTime _lastOverlayEval = DateTime.MinValue;
        private bool _lastBottomVisible = false;
        private bool _lastTopVisible = false;

        public OverlayControls? OverlayWpf => _overlayWpf;

        public MainWindowOverlayManager(MainWindow window, MainShellViewModel shell)
        {
            _window = window;
            _shell = shell;

            _overlayHideTimer.Interval = TimeSpan.FromSeconds(2);
            _overlayHideTimer.Tick += (s, e) => 
            { 
                _overlayWpf?.Hide(); 
                _shell.WindowStateActions.TopOverlay?.Hide();
                _overlayHideTimer.Stop(); 
            };

            _overlayPollTimer.Interval = TimeSpan.FromMilliseconds(120);
            _overlayPollTimer.Tick += (s, e) => ShowOverlayWithDelay();
        }

        public void Initialize()
        {
            _window.VideoPanel.MouseMove += (s, e) => ShowOverlayWithDelay();
            _window.SizeChanged += (s, e) => PositionOverlay();
            _window.LocationChanged += (s, e) => PositionOverlay();
        }

        public void Close()
        {
            try
            {
                _overlayWpf?.Close();
                _overlayWpf = null;
                _overlayHideTimer.Stop();
                _overlayPollTimer.Stop();
            }
            catch { }
        }

        public void BuildOverlayWindow(Action playPause, Action stop, Action rew, Action fwd, Action openSourceMenu)
        {
            _overlayWpf = new OverlayControls();
            _overlayWpf.Owner = _window;
            
            BindOverlay(playPause, stop, rew, fwd, openSourceMenu);
            
            _overlayWpf.Topmost = true;
            try { _overlayWpf.CurrentAspect = _shell.CurrentAspect; } catch { }
            
            try 
            { 
                _overlayWpf.SetDrawerVisible(!_shell.IsDrawerCollapsed); 
            } 
            catch { }
            
            SyncInitialState();

            _overlayWpf.Show();
            PositionOverlay();
            _overlayWpf.Hide();
            
            _overlayHideTimer.Stop();
            _overlayPollTimer.Start();
            ShowOverlayWithDelay();
        }

        public void ResetOverlayForOwner(Window owner, bool fullscreen, Action playPause, Action stop, Action rew, Action fwd, Action openSourceMenu)
        {
            try { _overlayWpf?.Close(); } catch { }
            _overlayWpf = new OverlayControls();
            _overlayWpf.Owner = owner;
            
            BindOverlay(playPause, stop, rew, fwd, openSourceMenu);
            
            _overlayWpf.Topmost = true;
            try { _overlayWpf.SetDrawerVisible(!_shell.IsDrawerCollapsed); } catch { }
            try { _overlayWpf.SetEpgVisible(_window.CbEpg.IsChecked == true); } catch { }
            
            // Sync Play/Pause
            try { _overlayWpf?.SetPlaySymbol(_shell.PlayPauseSymbol); } catch { }

            try { _overlayWpf.CurrentAspect = _shell.CurrentAspect; } catch { }
            
            SyncInitialState();

            // 初始化时移状态到新创建的悬浮条（窗口/全屏一致）
            try
            {
                _overlayWpf.SetTimeshift(_shell.IsTimeshiftActive);
                if (_shell.IsTimeshiftActive)
                {
                    if (_shell.TimeshiftMax == default) _shell.TimeshiftMax = DateTime.Now;
                    if (_shell.TimeshiftMin == default)
                    {
                        var hours = Math.Max(0, AppSettings.Current.TimeshiftHours);
                        _shell.TimeshiftMin = _shell.TimeshiftMax.AddHours(-hours);
                    }
                    var total = Math.Max(1, (_shell.TimeshiftMax - _shell.TimeshiftMin).TotalSeconds);
                    _shell.UpdateTimeshiftUi();
                    // 如果当前是回到窗口模式（fullscreen == false），同时把底部进度条与标签立即同步一次
                    if (!fullscreen)
                    {
                        _shell.UpdateTimeshiftUi();
                    }
                }
            }
            catch { }
            
            _overlayWpf.Show();
            PositionOverlay();
            _overlayWpf.Hide();
            
            // 刚创建悬浮条后，再异步同步一次时移 UI（避免创建过程中的布局延迟导致的不同步）
            try
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_shell.IsTimeshiftActive) SyncTimeshiftUi();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
            // 再设定一次轻微延迟的二次同步，确保 mpv.pos 在切换后稳定
            try
            {
                _timeshiftResyncTimer?.Stop();
                _timeshiftResyncTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _timeshiftResyncTimer.Tick += (s, e2) =>
                {
                    _timeshiftResyncTimer?.Stop();
                    _timeshiftResyncTimer = null;
                    if (_shell.IsTimeshiftActive) SyncTimeshiftUi();
                };
                _timeshiftResyncTimer.Start();
            }
            catch { }
        }

        private void BindOverlay(Action playPause, Action stop, Action rew, Action fwd, Action openSourceMenu)
        {
            if (_overlayWpf == null) return;
            
            _shell.OverlayBindingActions.BindOverlay(_overlayWpf, new OverlayBindingContext(
                PlayPause: playPause,
                Stop: stop,
                Rew: rew,
                Fwd: fwd,
                AspectRatioChanged: (ratio) => _shell.PlayerEngine?.SetAspectRatio(ratio),
                SpeedSelected: (sp) =>
                {
                    try
                    {
                        _shell.PlaybackSpeed = sp;
                        _shell.IsSpeedEnabled = _shell.PlaybackSpeedOverlaySyncActions.ResolveEnabled(_shell.IsTimeshiftActive, _shell.CurrentPlayingProgram != null, _shell.CurrentRecordingPlaying != null);
                    }
                    catch { }
                },
                PreviewRequested: () =>
                {
                    try
                    {
                        var result = _shell.OverlayPreviewActions.BuildPreviewResult(
                            _shell.CurrentChannel,
                            _shell.IsTimeshiftActive,
                            _shell.TimeshiftMin,
                            _shell.TimeshiftMax,
                            _shell.TimeshiftCursorSec,
                            _shell.CurrentPlayingProgram);
                        if (result == null) return;
                        LibmpvIptvClient.ModernMessageBox.Show(_window, result.Message, result.Title, MessageBoxButton.OK);
                    }
                    catch { }
                },
                DrawerToggled: (visible) => {
                    _window.SetDrawerCollapsed(!visible);
                    try { _overlayWpf?.SetDrawerVisible(visible); } catch { }
                },
                EpgToggled: (visible) => {
                    _window.CbEpg.IsChecked = visible;
                    _window.CbEpg_Click(_window, new RoutedEventArgs());
                    try { _overlayWpf?.SetEpgVisible(visible); } catch { }
                },
                SeekStart: () => _shell.IsSeeking = true,
                SeekEnd: (val) =>
                {
                    _shell.IsSeeking = false;
                    if (_shell.PlayerEngine == null) return;
                    if (_shell.IsTimeshiftActive && _shell.CurrentChannel != null)
                    {
                        var secs = Math.Max(0, val);
                        _shell.TimeshiftCursorSec = secs;
                        var t = _shell.TimeshiftMin.AddSeconds(secs);
                        try { LibmpvIptvClient.Diagnostics.Logger.Info($"时移定位到 {t:yyyy-MM-dd HH:mm:ss}"); } catch { }
                        _shell.ChannelPlaybackActions.PlayCatchupAt(_shell.CurrentChannel, t);
                        _shell.TimeshiftStart = t;
                    }
                    else
                    {
                        _shell.PlayerEngine?.SeekAbsolute(val);
                    }
                },
                VolumeChanged: (val) => _shell.Volume = val,
                MuteChanged: (on) => { _shell.IsMuted = on; },
                SourceMenuRequested: openSourceMenu,
                TimeshiftToggled: (on) => {
                    _shell.IsTimeshiftActive = on;
                    try { _overlayWpf?.SetTimeshift(on); } catch { }
                }
            ));
        }

        private void SyncInitialState()
        {
            if (_overlayWpf == null) return;
            try
            {
                if (!string.IsNullOrEmpty(_shell.PlaybackStatusText))
                {
                    _overlayWpf.SetPlaybackStatus(_shell.PlaybackStatusText, _shell.PlaybackStatusBrush);
                }
            }
            catch { }
            try { _overlayWpf.SetVolume(_shell.Volume); } catch { }
            try { _overlayWpf.SetMuted(_shell.IsMuted); } catch { }
            try { _overlayWpf.SetSpeedEnabled(_shell.IsSpeedEnabled); } catch { }
            try { _overlayWpf.SetSpeed(_shell.PlaybackSpeed); } catch { }
            try { _overlayWpf.SetTimeshift(_shell.IsTimeshiftActive); } catch { }
            try { _overlayWpf.SetEpgVisible(_window.CbEpg.IsChecked == true); } catch { }
            try { _overlayWpf.SetDrawerVisible(_window.CbDrawer.IsChecked == true); } catch { }
            try
            {
                _overlayWpf.SetInfo(_shell.MediaInfo.InfoText);
                _overlayWpf.SetTags(System.Linq.Enumerable.ToList(_shell.MediaInfo.Tags));
            }
            catch { }
        }

        public void SyncTimeshiftUi()
        {
            if (_shell.PlayerEngine == null || !_shell.IsTimeshiftActive) return;
            _shell.UpdateTimeshiftUi();
        }

        public void PositionOverlay()
        {
            if (_overlayWpf == null) return;
            var rect = (_shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.FullscreenPanel != null)
                ? _shell.WindowStateActions.FullscreenPanel.RectangleToScreen(_shell.WindowStateActions.FullscreenPanel.ClientRectangle)
                : _window.VideoPanel.RectangleToScreen(_window.VideoPanel.ClientRectangle);
            
            var visual = _shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.FullscreenWindow != null 
                ? (System.Windows.Media.Visual)_shell.WindowStateActions.FullscreenWindow 
                : (System.Windows.Media.Visual)_window;
                
            var source = PresentationSource.FromVisual(visual);
            if (source != null)
            {
                var m = source.CompositionTarget.TransformFromDevice;
                var pt1 = m.Transform(new System.Windows.Point(rect.Left, rect.Top));
                var pt2 = m.Transform(new System.Windows.Point(rect.Right, rect.Bottom));
                _overlayWpf.Left = pt1.X;
                _overlayWpf.Top = pt2.Y - _overlayWpf.Height;
                _overlayWpf.Width = Math.Max(320, pt2.X - pt1.X);
                return;
            }
            // Fallback
            _overlayWpf.Left = rect.Left;
            _overlayWpf.Top = rect.Bottom - _overlayWpf.Height;
            _overlayWpf.Width = Math.Max(320, rect.Width);
        }

        public void PositionTopOverlay()
        {
            if (_shell.WindowStateActions.TopOverlay == null) return;
            var isFs = _shell.WindowStateActions.IsFullscreen;
            if (isFs && _shell.WindowStateActions.FullscreenPanel == null) return;
            System.Drawing.Rectangle rect;
            if (isFs)
            {
                rect = _shell.WindowStateActions.FullscreenPanel!.RectangleToScreen(_shell.WindowStateActions.FullscreenPanel!.ClientRectangle);
            }
            else
            {
                // 当未播放或 VideoHost 折叠时，VideoPanel 可能为 0，改为回退到 WPF 视频区域容器
                var panelRect = _window.VideoPanel.RectangleToScreen(_window.VideoPanel.ClientRectangle);
                if (panelRect.Width <= 0 || panelRect.Height <= 0 || !_window.VideoHost.IsVisible)
                {
                    try
                    {
                        var container = System.Windows.Media.VisualTreeHelper.GetParent(_window.VideoHost) as System.Windows.FrameworkElement;
                        if (container != null)
                        {
                            var tl = container.PointToScreen(new System.Windows.Point(0, 0));
                            var br = container.PointToScreen(new System.Windows.Point(container.ActualWidth, container.ActualHeight));
                            rect = new System.Drawing.Rectangle((int)tl.X, (int)tl.Y, Math.Max(0, (int)(br.X - tl.X)), Math.Max(0, (int)(br.Y - tl.Y)));
                        }
                        else
                        {
                            rect = panelRect;
                        }
                    }
                    catch
                    {
                        rect = panelRect;
                    }
                }
                else
                {
                    rect = panelRect;
                }
            }
            var visual = isFs && _shell.WindowStateActions.FullscreenWindow != null
                ? (System.Windows.Media.Visual)_shell.WindowStateActions.FullscreenWindow
                : (System.Windows.Media.Visual)_window;
            var source = PresentationSource.FromVisual(visual);
            if (source != null)
            {
                var m = source.CompositionTarget.TransformFromDevice;
                var pt1 = m.Transform(new System.Windows.Point(rect.Left, rect.Top));
                var pt2 = m.Transform(new System.Windows.Point(rect.Right, rect.Bottom));
                var vw = Math.Max(320, pt2.X - pt1.X);
                _shell.WindowStateActions.TopOverlay.Left = pt1.X;
                _shell.WindowStateActions.TopOverlay.Top = pt1.Y;
                _shell.WindowStateActions.TopOverlay.Width = vw;
                try { _shell.WindowStateActions.TopOverlay.SyncWindowVisual(isFs, isFs ? WindowState.Maximized : _window.WindowState, _shell.IsMinimalMode); } catch { }
                return;
            }
            _shell.WindowStateActions.TopOverlay.Left = rect.Left;
            _shell.WindowStateActions.TopOverlay.Top = rect.Top;
            _shell.WindowStateActions.TopOverlay.Width = rect.Width;
            try { _shell.WindowStateActions.TopOverlay.SyncWindowVisual(isFs, isFs ? WindowState.Maximized : _window.WindowState, _shell.IsMinimalMode); } catch { }
        }

        public void ShowOverlayWithDelay()
        {
            if (!_shell.WindowStateActions.IsFullscreen || _shell.WindowStateActions.FullscreenWindow == null || !_shell.WindowStateActions.FullscreenWindow.IsLoaded)
            {
                if (_shell.IsMinimalMode)
                {
                    try
                    {
                        // 仅在窗口精简且非最小化状态显示精简顶部；全屏由全屏顶部处理
                        if (_window.WindowState == WindowState.Minimized)
                        {
                            _shell.WindowStateActions.TopOverlay?.Hide();
                            return;
                        }
                        PositionTopOverlay();
                        if (GetCursorPos(out POINT p1))
                        {
                            var rel = _window.PointFromScreen(new System.Windows.Point(p1.X, p1.Y));
                            var edge = 8.0;
                            if (rel.X <= edge || rel.X >= Math.Max(edge, _window.ActualWidth - edge) || rel.Y <= edge || rel.Y >= Math.Max(edge, _window.ActualHeight - edge))
                            {
                                _shell.WindowStateActions.TopOverlay?.Hide();
                                return;
                            }
                            var topZone = 48.0;
                            if (rel.Y >= 0 && rel.Y < topZone)
                            {
                                if (_shell.WindowStateActions.TopOverlay != null && !_shell.WindowStateActions.TopOverlay.IsVisible)
                                {
                                    _shell.WindowStateActions.TopOverlay.Show();
                                    _shell.WindowStateActions.TopOverlay.Topmost = true;
                                    _shell.WindowStateActions.TopOverlay.Activate();
                                }
                                _overlayHideTimer.Stop();
                                _overlayHideTimer.Start();
                            }
                            else
                            {
                                if (_shell.WindowStateActions.TopOverlay != null && _shell.WindowStateActions.TopOverlay.IsVisible)
                                {
                                    _shell.WindowStateActions.TopOverlay.Hide();
                                }
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    if (_window.VideoHost.IsMouseOver || (_window.BottomBar.IsMouseOver && _window.BottomBar.Visibility == Visibility.Visible))
                    {
                        _overlayWpf?.Hide();
                        _shell.WindowStateActions.TopOverlay?.Hide();
                    }
                }
                return;
            }
            
            // 监控并强制全屏窗口置顶
            try
            {
                if (_shell.WindowStateActions.FullscreenWindow != null && !_shell.WindowStateActions.FullscreenWindow.Topmost)
                {
                    _shell.WindowStateActions.FullscreenWindow.Topmost = true;
                }
            }
            catch { }

            // Fullscreen Logic
            PositionOverlay();
            PositionTopOverlay();

            if (GetCursorPos(out POINT p))
            {
                var fsWindow = _shell.WindowStateActions.FullscreenWindow;
                System.Windows.Point relPoint;
                try
                {
                    relPoint = fsWindow.PointFromScreen(new System.Windows.Point(p.X, p.Y));
                }
                catch
                {
                    relPoint = new System.Windows.Point(p.X - fsWindow.Left, p.Y - fsWindow.Top);
                }

                var relX = relPoint.X;
                var relY = relPoint.Y;
                var screenW = fsWindow.ActualWidth;
                var screenH = fsWindow.ActualHeight;

                if (screenW <= 0 || screenH <= 0) return;

                // 1. Bottom Zone (Overlay Controls)
                double bottomZone = Math.Max(160, screenH * 0.26);
                bool inBottomZone = relY > screenH - bottomZone;
                bool keepBottomForMenu = _overlayWpf != null && _overlayWpf.IsAnyMenuOpen;
                if (inBottomZone) 
                {
                    if (_overlayWpf != null && !_overlayWpf.IsVisible)
                    {
                        _overlayWpf.Show();
                        BringOverlayToTop();
                        _overlayHideTimer.Stop();
                        _overlayHideTimer.Start();
                    }
                    else if (_overlayWpf != null && _overlayWpf.IsVisible)
                    {
                        BringOverlayToTop();
                        _overlayHideTimer.Stop();
                        _overlayHideTimer.Start();
                    }
                    _lastBottomVisible = true;
                    _lastOverlayEval = DateTime.UtcNow;
                }
                else
                {
                    var now = DateTime.UtcNow;
                    if (_overlayWpf != null && _overlayWpf.IsVisible && !keepBottomForMenu)
                    {
                        if ((now - _lastOverlayEval).TotalMilliseconds > 60 || _lastBottomVisible)
                        {
                            _overlayWpf.Hide();
                            
                            // 修复：当底部控制栏隐藏时，强制刷新主窗口的置顶状态
                            try
                            {
                                if (_shell.WindowStateActions.FullscreenWindow != null)
                                {
                                    _shell.WindowStateActions.FullscreenWindow.Topmost = true;
                                    var h = new System.Windows.Interop.WindowInteropHelper(_shell.WindowStateActions.FullscreenWindow).Handle;
                                    SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
                                }
                            }
                            catch { }

                            _lastBottomVisible = false;
                            _lastOverlayEval = now;
                        }
                    }
                }

                // 2. Top Zone (Top Overlay)
                bool inTopZone = relY < 120;
                bool keepTopForMenu = _shell.WindowStateActions.TopOverlay != null && _shell.WindowStateActions.TopOverlay.IsMenuOpen;
                bool preferTopOverlay = inTopZone || keepTopForMenu;
                try
                {
                    var hideClose = _window.CbEpg.IsChecked == true || !_shell.IsDrawerCollapsed;
                    _shell.WindowStateActions.TopOverlay?.SetCloseVisible(!hideClose);
                }
                catch { }
                if (preferTopOverlay)
                {
                    try
                    {
                        if (_shell.WindowStateActions.FullscreenEpg != null && _shell.WindowStateActions.FullscreenEpg.IsVisible) _shell.WindowStateActions.FullscreenEpg.Topmost = false;
                        if (_shell.WindowStateActions.FullscreenDrawer != null && _shell.WindowStateActions.FullscreenDrawer.IsVisible) _shell.WindowStateActions.FullscreenDrawer.Topmost = false;
                    }
                    catch { }
                }
                if (inTopZone || keepTopForMenu)
                {
                    if (_shell.WindowStateActions.TopOverlay != null && !_shell.WindowStateActions.TopOverlay.IsVisible)
                    {
                        _shell.WindowStateActions.TopOverlay.Show();
                        _shell.WindowStateActions.TopOverlay.Topmost = true;
                        _shell.WindowStateActions.TopOverlay.Activate();
                    }
                    if (_shell.WindowStateActions.TopOverlay != null && _shell.WindowStateActions.TopOverlay.IsVisible)
                    {
                        _overlayHideTimer.Stop();
                        _overlayHideTimer.Start();
                    }
                    _lastTopVisible = true;
                }
                else
                {
                    if (_shell.WindowStateActions.TopOverlay != null && _shell.WindowStateActions.TopOverlay.IsVisible && !keepTopForMenu)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastOverlayEval).TotalMilliseconds > 60 || _lastTopVisible)
                        {
                            _shell.WindowStateActions.TopOverlay.Hide();
                            _lastTopVisible = false;
                            _lastOverlayEval = now;
                        }
                    }
                }

                // 3. EPG & 4. Drawer Logic -> Delegated to MainWindow helpers via FullscreenContext usually, 
                // but here we can call them if they are internal.
                // MainWindow's ShowFullscreenEpg etc are private in MainWindow.xaml.cs.
                // We need to access them.
                // Option A: Make them internal in MainWindow.
                // Option B: Re-implement them here (they call _shell.WindowStateActions anyway).
                
                HandleFullscreenSidebars(relX, screenW, preferTopOverlay);
            }
        }

        private void HandleFullscreenSidebars(double relX, double screenW, bool preferTopOverlay)
        {
            try
            {
                // 3. EPG (Left)
                bool epgEnabled = _window.CbEpg.IsChecked == true;
                if (epgEnabled)
                {
                    if (_shell.WindowStateActions.FullscreenEpg == null) _window.ShowFullscreenEpg();
                    
                    if (_shell.WindowStateActions.FullscreenEpg != null)
                    {
                        // Fix: Re-introduce Auto-Hide logic but keep "Pinned" if user toggled it?
                        // Actually, if user toggled "EPG" button (epgEnabled == true), they expect it to be visible.
                        // If they want it to auto-hide, they wouldn't have clicked the button?
                        // OR, maybe the button just enables the "Feature", and mouse hover triggers visibility?
                        // In Window mode, it's a static panel.
                        // User request: "全屏模式下 epg和频道列表 不能隐藏... 没有自动隐藏，这个需要调整一下！"
                        // This means they WANT auto-hide behavior back!
                        // So: If mouse is near edge -> Show. If mouse leaves -> Hide.
                        // BUT, if user *explicitly* opened it via Overlay Button, should it stay open?
                        // Usually "Auto Hide" means: Default Hidden. Hover -> Show. Leave -> Hide.
                        // If user clicks button, maybe they want to "Pin" it?
                        // User says: "Open/Close normal... but no auto hide".
                        // This implies when they Open it (via button?), it stays open forever. They want it to hide when mouse leaves.
                        // So we should revert to the "Zone" check logic, BUT ensure it can still be opened.
                        // The problem before was it wouldn't open or load.
                        // Now it loads. We just need to add back the "Hide if not in zone" logic.
                        
                        bool isVisible = _shell.WindowStateActions.FullscreenEpg.Visibility == Visibility.Visible;
                        bool inZone = relX <= 320; 
                        bool onEdge = relX <= 20;

                        // If user toggled it ON, we treat it as "Enabled for Auto-Hide interaction"
                        // If user toggled it OFF, we close it completely (handled by else block).
                        
                        if (inZone || onEdge)
                        {
                            if (!isVisible)
                            {
                                _shell.WindowStateActions.FullscreenEpg.Show();
                                _shell.WindowStateActions.FullscreenEpg.Visibility = Visibility.Visible;
                                _shell.WindowStateActions.FullscreenWindow.Topmost = true;
                                _shell.WindowStateActions.FullscreenEpg.Topmost = !preferTopOverlay;
                                if (!preferTopOverlay) _shell.WindowStateActions.FullscreenEpg.Activate();
                                
                                var topOffset = _shell.WindowStateActions.GetTopOverlayOffset();
                                _shell.WindowStateActions.FullscreenEpg.Left = _shell.WindowStateActions.FullscreenWindow.Left;
                                _shell.WindowStateActions.FullscreenEpg.Top = _shell.WindowStateActions.FullscreenWindow.Top + topOffset;
                                _shell.WindowStateActions.FullscreenEpg.Height = Math.Max(0, _shell.WindowStateActions.FullscreenWindow.ActualHeight - topOffset);
                            }
                        }
                        else 
                        {
                            // Mouse is NOT in zone.
                            // If user clicked the button, maybe they want it to stay for a bit?
                            // But user explicitly asked for "Auto Hide".
                            // So we should Hide it.
                            if (isVisible)
                            {
                                _shell.WindowStateActions.FullscreenEpg.Hide();
                                _shell.WindowStateActions.FullscreenEpg.Visibility = Visibility.Collapsed;
                                if (_shell.WindowStateActions.FullscreenWindow != null) _shell.WindowStateActions.FullscreenWindow.Topmost = _window.Topmost;
                            }
                        }
                    }
                }
                else if (_shell.WindowStateActions.FullscreenEpg != null)
                {
                     _window.CloseFullscreenEpg();
                }

                // 4. Drawer (Right)
                if (!_shell.IsDrawerCollapsed && _shell.WindowStateActions.FullscreenWindow != null) 
                {
                    if (_shell.WindowStateActions.FullscreenDrawer == null) _window.ShowFullscreenDrawer(); 
                    
                    if (_shell.WindowStateActions.FullscreenDrawer != null)
                    {
                        bool isVisible = _shell.WindowStateActions.FullscreenDrawer.Visibility == Visibility.Visible;
                        double w = _shell.DrawerWidth > 0 ? _shell.DrawerWidth : 380;
                        bool inZone = relX >= screenW - w;
                        bool onEdge = relX >= screenW - 20;

                        if (inZone || onEdge)
                        {
                            if (!isVisible)
                            {
                                _shell.WindowStateActions.FullscreenDrawer.Show();
                                _shell.WindowStateActions.FullscreenDrawer.Visibility = Visibility.Visible;
                                _shell.WindowStateActions.FullscreenWindow.Topmost = true;
                                _shell.WindowStateActions.FullscreenDrawer.Topmost = !preferTopOverlay;
                                if (!preferTopOverlay) _shell.WindowStateActions.FullscreenDrawer.Activate();
                                
                                var topOffset = _shell.WindowStateActions.GetTopOverlayOffset();
                                _shell.WindowStateActions.FullscreenDrawer.Left = _shell.WindowStateActions.FullscreenWindow.Left + _shell.WindowStateActions.FullscreenWindow.ActualWidth - w;
                                _shell.WindowStateActions.FullscreenDrawer.Top = _shell.WindowStateActions.FullscreenWindow.Top + topOffset;
                                _shell.WindowStateActions.FullscreenDrawer.Height = Math.Max(0, _shell.WindowStateActions.FullscreenWindow.ActualHeight - topOffset);
                            }
                        }
                        else
                        {
                            if (isVisible)
                            {
                                _shell.WindowStateActions.FullscreenDrawer.Hide();
                                _shell.WindowStateActions.FullscreenDrawer.Visibility = Visibility.Collapsed;
                                if (_shell.WindowStateActions.FullscreenWindow != null) _shell.WindowStateActions.FullscreenWindow.Topmost = _window.Topmost;
                            }
                        }
                    }
                }
                else if (_shell.WindowStateActions.FullscreenDrawer != null)
                {
                    _window.CloseFullscreenDrawer();
                }
            }
            catch { }
        }

        public void BringOverlayToTop()
        {
            try
            {
                if (_overlayWpf == null) return;
                _overlayWpf.Topmost = true;
                _overlayWpf.Topmost = false;
                _overlayWpf.Topmost = true;
                var h = new System.Windows.Interop.WindowInteropHelper(_overlayWpf).Handle;
                SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
            }
            catch { }
        }

        public void ShowFsOverlayNow()
        {
            if (!_shell.WindowStateActions.IsFullscreen || _shell.WindowStateActions.FullscreenWindow == null || _overlayWpf == null) return;
            try
            {
                PositionOverlay();
                _overlayWpf.Show();
                BringOverlayToTop();
                _overlayHideTimer.Stop();
                _overlayHideTimer.Start();
            }
            catch { }
        }
        
        public void OnFullscreenLoaded(object? sender, RoutedEventArgs e)
        {
            if (!_shell.WindowStateActions.IsFullscreen) return;
            if (_overlayWpf == null) return;
            if (sender is not Window win) return;
            
            if (_shell.WindowStateActions.FullscreenDrawer == null && !_shell.IsDrawerCollapsed)
            {
                _window.ShowFullscreenDrawer();
            }

            win.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_shell.WindowStateActions.IsFullscreen) return;
                if (_overlayWpf == null) return;
                try
                {
                    _overlayWpf.Owner = null;
                    _overlayWpf.Visibility = Visibility.Collapsed;
                    _overlayWpf.Hide();

                    if (!ReferenceEquals(_overlayWpf.Owner, win))
                    {
                        _overlayWpf.Owner = win;
                    }
                    _overlayWpf.Topmost = true;
                    _overlayWpf.InvalidateVisual();
                    _overlayWpf.Show();
                    _overlayWpf.Visibility = Visibility.Visible;
                    ShowFsOverlayNow();
                    
                    PositionOverlay();
                    _overlayHideTimer.Stop();
                    _overlayHideTimer.Start();
                    ShowOverlayWithDelay();

                    // _timer is in MainWindow, need to access it or expose method.
                    // But _timer is mainly for PlaybackTick.
                    // We can expose StartPlaybackTimer/StopPlaybackTimer on MainWindow.
                    // Or access _window._timer if internal.
                    // MainWindow._timer is private.
                    // I should expose internal methods on MainWindow: StartPlaybackTimer(), StopPlaybackTimer().
                    _window.StartPlaybackTimer();
                    StartTimers();
                    
                    if (_shell.IsTimeshiftActive) _shell.UpdateTimeshiftUi();
                }
                catch (Exception ex)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Error("Overlay reparent error: " + ex.Message);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void StopTimers()
        {
            _overlayHideTimer.Stop();
            _overlayPollTimer.Stop();
        }
        
        public void StartTimers()
        {
            _overlayPollTimer.Start();
        }

        public void UpdateOverlayIconBrushes()
        {
            try
            {
                _overlayWpf?.UpdateIconBrushes();
            }
            catch { }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
