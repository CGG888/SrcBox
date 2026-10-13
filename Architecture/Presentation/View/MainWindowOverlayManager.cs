using System;
using System.Windows;
using System.Windows.Threading;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Controls;
using LibmpvIptvClient.Helpers;
using System.Runtime.InteropServices;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowOverlayManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;
        
        private OverlayControls? _overlayWpf;
        private ChannelNumberOverlay? _channelNumberWpf;
        private DispatcherTimer? _channelNumberHideTimer;
        private DispatcherTimer _overlayHideTimer = new DispatcherTimer();
        private DispatcherTimer _overlayPollTimer = new DispatcherTimer();
        private DispatcherTimer? _timeshiftResyncTimer;
        
        private DateTime _lastOverlayEval = DateTime.MinValue;
        private bool _lastBottomVisible = false;
        private bool _lastTopVisible = false;

        /// <summary>Distance (DIPs) between the channel number badge and the video area corner.</summary>
        private const double ChannelNumberMarginX = 38;
        private const double ChannelNumberMarginY = 32;

        /// <summary>Transparent room the overlay window keeps around the badge so its drop shadow is not
        /// clipped; it is subtracted from the margins above when the window is anchored.</summary>
        private const double ChannelNumberShadowRoom = 16;

        /// <summary>How long the channel number badge stays on screen.</summary>
        private static readonly TimeSpan ChannelNumberVisibleTime = TimeSpan.FromSeconds(3);

        public OverlayControls? OverlayWpf => _overlayWpf;

        /// <summary>
        /// Height (in DIPs) of the floating bottom navigation bar. The fullscreen EPG / drawer stop above
        /// it so it never covers them; falls back to the design height while the bar is not laid out yet.
        /// </summary>
        public double BottomBarHeight
        {
            get
            {
                var h = _overlayWpf?.ActualHeight ?? 0;
                if (h <= 0 || double.IsNaN(h)) h = _overlayWpf?.Height ?? 0;
                return h > 0 && !double.IsNaN(h) ? h : 80;
            }
        }

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
                _channelNumberHideTimer?.Stop();
                _channelNumberHideTimer = null;
                _channelNumberWpf?.Close();
                _channelNumberWpf = null;
                _overlayHideTimer.Stop();
                _overlayPollTimer.Stop();
            }
            catch { }
        }

        public void SetOverlayAspectRatio(string ratio)
        {
            try
            {
                if (_overlayWpf != null)
                {
                    _overlayWpf.CurrentAspect = ratio;
                    _overlayWpf.UpdateRatioMenuItems();
                }
            }
            catch { }
        }

        public void BuildOverlayWindow(Action playPause, Action stop, Action rew, Action fwd, Action openSourceMenu)
        {
            _overlayWpf = new OverlayControls();
            _overlayWpf.Owner = _window;

            // The bar is built lazily, so it has to pick the fullscreen palette up here when it happens to
            // be created while fullscreen is already active.
            FullscreenOverlayTheme.UseDarkPalette(_overlayWpf, _shell.WindowStateActions.IsFullscreen);
            
            BindOverlay(playPause, stop, rew, fwd, openSourceMenu);

            // The bar grows taller once the EPG timeline is populated, so keep the windowed placement anchored to
            // the video area (fullscreen already repositions itself from the 120 ms overlay poll).
            try { _overlayWpf.SizeChanged += (s, e) => PositionOverlay(); } catch { }
            
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

        /// <summary>Re-syncs the floating control bar after a fullscreen switch. The bar lives in the main
        /// window's overlay layer and its owner never changes any more, so the existing window is re-bound
        /// instead of being closed and recreated (that rebuild used to flash the top-most layer).</summary>
        public void ResetOverlayForOwner(Window owner, bool fullscreen, Action playPause, Action stop, Action rew, Action fwd, Action openSourceMenu)
        {
            if (_overlayWpf == null)
            {
                BuildOverlayWindow(playPause, stop, rew, fwd, openSourceMenu);
                if (owner != null && !ReferenceEquals(_overlayWpf.Owner, owner))
                {
                    try { _overlayWpf.Owner = owner; } catch { }
                }
                PositionOverlay();
                return;
            }

            // The bar keeps the handlers it was built with. Re-binding here would subscribe them a second
            // time (BindOverlay uses +=), so a single click would fire the action twice.
            try { _overlayWpf.Topmost = true; } catch { }
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
            
            // Keep the bar hidden after the switch; entering fullscreen shows it again explicitly.
            try
            {
                PositionOverlay();
                _overlayWpf.Hide();
            }
            catch { }
            
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
                AspectRatioChanged: (ratio) => { _shell.CurrentAspect = ratio; MenuBuilder.SetCurrentAspectRatio(ratio); MenuBuilder.RefreshAllRatioChecks(ratio); _shell.PlayerEngine?.SetAspectRatio(ratio); try { if (_overlayWpf != null) { _overlayWpf.CurrentAspect = ratio; _overlayWpf.UpdateRatioMenuItems(); } } catch { } },
                SpeedSelected: (sp) =>
                {
                    try
                    {
                        _shell.PlaybackSpeed = sp;
                        _shell.IsSpeedEnabled = _shell.PlaybackSpeedOverlaySyncActions.ResolveEnabled(_shell.IsTimeshiftActive, _shell.CurrentPlayingProgram != null, _shell.CurrentRecordingPlaying != null);
                        MenuBuilder.SetCurrentSpeed(sp);
                        MenuBuilder.RefreshAllSpeedChecks(sp);
                        try { if (_overlayWpf != null) _overlayWpf.SetSpeed(sp); } catch { }
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
                        _shell.ChannelPlaybackActions.PlayCatchupAt(_shell.CurrentChannel, t, keepTimeshiftMode: true);
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
                    try { _window.SetWindowedTimelineTimeshift(on); } catch { }
                },
                ShortcutKeyPressed: (key, modifiers) => {
                    var action = _shell.ShortcutActions.ResolveAction(key, modifiers);
                    if (action != MainWindowShortcutAction.None)
                    {
                        _shell.ShortcutActions.ExecuteAction(action);
                        return true;
                    }
                    return false;
                }
            ));

            // The EPG timeline card click is not part of the shared binding context, so it is wired here and
            // mirrors the timeshift branch of SeekEnd above (keep the timeshift mode when it is already active).
            _overlayWpf.ProgrammeSelected += (start) =>
            {
                try
                {
                    var channel = _shell.CurrentChannel;
                    if (channel == null) return;
                    LibmpvIptvClient.Diagnostics.Logger.Info($"节目单跳转到 {start:yyyy-MM-dd HH:mm:ss}");
                    if (_shell.IsTimeshiftActive)
                    {
                        _shell.TimeshiftCursorSec = Math.Max(0, (start - _shell.TimeshiftMin).TotalSeconds);
                        _shell.TimeshiftStart = start;
                    }
                    _shell.ChannelPlaybackActions.PlayCatchupAt(channel, start, keepTimeshiftMode: _shell.IsTimeshiftActive);
                }
                catch { }
            };
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

        /// <summary>Positions the floating control bar over the video area. The main window hosts the video in
        /// both modes now, so there is a single code path (no fullscreen branch).</summary>
        public void PositionOverlay()
        {
            if (_overlayWpf == null) return;
            var rect = _window.VideoPanel.RectangleToScreen(_window.VideoPanel.ClientRectangle);

            var source = PresentationSource.FromVisual(_window);
            if (source != null)
            {
                var m = source.CompositionTarget.TransformFromDevice;
                var pt1 = m.Transform(new System.Windows.Point(rect.Left, rect.Top));
                var pt2 = m.Transform(new System.Windows.Point(rect.Right, rect.Bottom));
                _overlayWpf.Left = pt1.X;
                _overlayWpf.Top = pt2.Y - _overlayWpf.Height;
                _overlayWpf.Width = Math.Max(320, pt2.X - pt1.X);
                PositionChannelNumberOverlay();
                return;
            }
            // Fallback
            _overlayWpf.Left = rect.Left;
            _overlayWpf.Top = rect.Bottom - _overlayWpf.Height;
            _overlayWpf.Width = Math.Max(320, rect.Width);
            PositionChannelNumberOverlay();
        }

        /// <summary>Positions the minimal mode top bar over the video area. Like the bottom bar this is based
        /// on the main window only.</summary>
        public void PositionTopOverlay()
        {
            if (_shell.WindowStateActions.TopOverlay == null) return;
            var isFs = _shell.WindowStateActions.IsFullscreen;

            var rect = GetVideoScreenRect();

            var source = PresentationSource.FromVisual(_window);
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

        /// <summary>Screen rectangle of the video area. VideoPanel reports zero while nothing is playing
        /// or the video host is collapsed, so this falls back to the WPF container around it.</summary>
        private System.Drawing.Rectangle GetVideoScreenRect()
        {
            var panelRect = _window.VideoPanel.RectangleToScreen(_window.VideoPanel.ClientRectangle);
            if (panelRect.Width > 0 && panelRect.Height > 0 && _window.VideoHost.IsVisible) return panelRect;

            try
            {
                var container = System.Windows.Media.VisualTreeHelper.GetParent(_window.VideoHost) as System.Windows.FrameworkElement;
                if (container != null)
                {
                    var tl = container.PointToScreen(new System.Windows.Point(0, 0));
                    var br = container.PointToScreen(new System.Windows.Point(container.ActualWidth, container.ActualHeight));
                    return new System.Drawing.Rectangle((int)tl.X, (int)tl.Y, Math.Max(0, (int)(br.X - tl.X)), Math.Max(0, (int)(br.Y - tl.Y)));
                }
            }
            catch { }

            return panelRect;
        }

        /// <summary>Flashes the channel number in the top-right corner of the video area. Only channel
        /// switches made from the web remote call this; the badge hides itself after a few seconds.</summary>
        public void ShowChannelNumber(int number)
        {
            if (number <= 0) return;
            try
            {
                if (_channelNumberWpf == null)
                {
                    _channelNumberWpf = new ChannelNumberOverlay();
                    try { _channelNumberWpf.Owner = _window; } catch { }

                    _channelNumberHideTimer = new DispatcherTimer { Interval = ChannelNumberVisibleTime };
                    _channelNumberHideTimer.Tick += (s, e) =>
                    {
                        _channelNumberHideTimer?.Stop();
                        _channelNumberWpf?.Hide();
                    };
                }

                _channelNumberWpf.SetNumber(number);
                _channelNumberWpf.Show();
                // SizeToContent needs a layout pass before the badge has a size to align against.
                _channelNumberWpf.UpdateLayout();
                PositionChannelNumberOverlay();

                _channelNumberHideTimer?.Stop();
                _channelNumberHideTimer?.Start();
            }
            catch { }
        }

        /// <summary>Anchors the channel number badge to the top-right corner of the video area.</summary>
        public void PositionChannelNumberOverlay()
        {
            if (_channelNumberWpf == null || !_channelNumberWpf.IsVisible) return;

            var rect = GetVideoScreenRect();
            var width = _channelNumberWpf.ActualWidth > 0 ? _channelNumberWpf.ActualWidth : 0;

            var source = PresentationSource.FromVisual(_window);
            if (source != null)
            {
                var m = source.CompositionTarget.TransformFromDevice;
                var pt1 = m.Transform(new System.Windows.Point(rect.Left, rect.Top));
                var pt2 = m.Transform(new System.Windows.Point(rect.Right, rect.Bottom));
                _channelNumberWpf.Left = Math.Max(pt1.X, pt2.X - width - ChannelNumberMarginX + ChannelNumberShadowRoom);
                _channelNumberWpf.Top = pt1.Y + ChannelNumberMarginY - ChannelNumberShadowRoom;
                return;
            }

            _channelNumberWpf.Left = Math.Max(rect.Left, rect.Right - width - ChannelNumberMarginX + ChannelNumberShadowRoom);
            _channelNumberWpf.Top = rect.Top + ChannelNumberMarginY - ChannelNumberShadowRoom;
        }

        /// <summary>Shows or hides the floating overlays depending on where the pointer is. Windowed mode keeps
        /// the bottom bar as a real row of the main window; fullscreen drives both the bottom bar and the
        /// top bar from the cursor hot zones.</summary>
        public void ShowOverlayWithDelay()
        {
            if (!_shell.WindowStateActions.IsFullscreen)
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
            
            // While fullscreen the main window (and with it the video host) must stay above everything else.
            try
            {
                if (!_window.Topmost) _window.Topmost = true;
            }
            catch { }

            // Fullscreen Logic
            PositionOverlay();
            PositionTopOverlay();

            if (GetCursorPos(out POINT p))
            {
                System.Windows.Point relPoint;
                try
                {
                    relPoint = _window.PointFromScreen(new System.Windows.Point(p.X, p.Y));
                }
                catch
                {
                    relPoint = new System.Windows.Point(p.X - _window.Left, p.Y - _window.Top);
                }

                var relX = relPoint.X;
                var relY = relPoint.Y;
                var screenW = _window.ActualWidth;
                var screenH = _window.ActualHeight;

                if (screenW <= 0 || screenH <= 0) return;

                // This poll runs every 120 ms regardless of where the pointer actually is, so a
                // cursor sitting on another monitor (or over another application) must never
                // drive the fullscreen hot zones. Mixed-DPI setups additionally skew
                // PointFromScreen, hence both the monitor check and the window rect check.
                bool insideWindow = IsCursorInsideWindow(_window, p, relX, relY, screenW, screenH);

                // 1. Bottom Zone (Overlay Controls)
                double bottomZone = Math.Max(160, screenH * 0.26);
                bool inBottomZone = insideWindow && relY > screenH - bottomZone;
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
                                _window.Topmost = true;
                                var h = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
                                SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
                            }
                            catch { }

                            _lastBottomVisible = false;
                            _lastOverlayEval = now;
                        }
                    }
                }

                // 2. Top Zone (Top Overlay)
                bool inTopZone = insideWindow && relY < 120;
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
                
                HandleFullscreenSidebars(relX, screenW, preferTopOverlay, insideWindow);
            }
        }

        /// <summary>
        /// True when the cursor is on the same monitor as <paramref name="window"/> and inside its
        /// client rectangle. Guards the hot zones against pointer positions on other screens.
        /// </summary>
        private static bool IsCursorInsideWindow(System.Windows.Window window, POINT p, double relX, double relY, double screenW, double screenH)
        {
            try
            {
                var fsHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var cursorMonitor = MonitorFromPoint(new POINT { X = p.X, Y = p.Y }, MONITOR_DEFAULTTONEAREST);
                var windowMonitor = MonitorFromWindow(fsHandle, MONITOR_DEFAULTTONEAREST);
                if (cursorMonitor != IntPtr.Zero && windowMonitor != IntPtr.Zero && cursorMonitor != windowMonitor)
                    return false;
            }
            catch { }

            return relX >= 0 && relY >= 0 && relX <= screenW && relY <= screenH;
        }

        private void HandleFullscreenSidebars(double relX, double screenW, bool preferTopOverlay, bool insideWindow)
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
                        bool inZone = insideWindow && relX <= 320; 
                        bool onEdge = insideWindow && relX <= 20;

                        // If user toggled it ON, we treat it as "Enabled for Auto-Hide interaction"
                        // If user toggled it OFF, we close it completely (handled by else block).
                        
                        if (inZone || onEdge)
                        {
                            if (!isVisible)
                            {
                                _shell.WindowStateActions.FullscreenEpg.Show();
                                _shell.WindowStateActions.FullscreenEpg.Visibility = Visibility.Visible;
                                _window.Topmost = true;
                                _shell.WindowStateActions.FullscreenEpg.Topmost = !preferTopOverlay;
                                if (!preferTopOverlay) _shell.WindowStateActions.FullscreenEpg.Activate();
                                
                                // Geometry (top overlay offset + bottom navigation reserve) lives in one place.
                                _shell.WindowStateActions.PositionFullscreenPanels();
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
                            }
                        }
                    }
                }
                else if (_shell.WindowStateActions.FullscreenEpg != null)
                {
                     _window.CloseFullscreenEpg();
                }

                // 4. Drawer (Right)
                if (!_shell.IsDrawerCollapsed) 
                {
                    if (_shell.WindowStateActions.FullscreenDrawer == null) _window.ShowFullscreenDrawer(); 
                    
                    if (_shell.WindowStateActions.FullscreenDrawer != null)
                    {
                        bool isVisible = _shell.WindowStateActions.FullscreenDrawer.Visibility == Visibility.Visible;
                        double w = _shell.DrawerWidth > 0 ? _shell.DrawerWidth : 380;
                        bool inZone = insideWindow && relX >= screenW - w;
                        bool onEdge = insideWindow && relX >= screenW - 20;

                        if (inZone || onEdge)
                        {
                            if (!isVisible)
                            {
                                _shell.WindowStateActions.FullscreenDrawer.Show();
                                _shell.WindowStateActions.FullscreenDrawer.Visibility = Visibility.Visible;
                                _window.Topmost = true;
                                _shell.WindowStateActions.FullscreenDrawer.Topmost = !preferTopOverlay;
                                if (!preferTopOverlay) _shell.WindowStateActions.FullscreenDrawer.Activate();
                                
                                // Geometry (top overlay offset + bottom navigation reserve) lives in one place.
                                _shell.WindowStateActions.PositionFullscreenPanels();
                            }
                        }
                        else
                        {
                            if (isVisible)
                            {
                                _shell.WindowStateActions.FullscreenDrawer.Hide();
                                _shell.WindowStateActions.FullscreenDrawer.Visibility = Visibility.Collapsed;
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
            if (!_shell.WindowStateActions.IsFullscreen || _overlayWpf == null) return;
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

        const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

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
