using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LibmpvIptvClient.Architecture.Application.Player;
using LibmpvIptvClient.Architecture.Platform.Player;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient
{
    public partial class MainWindow : Window
    {
        bool _isApplyingMinimalMode;
        bool _minimalPrevEpgChecked;
        bool _minimalPrevDrawerCollapsed;
        bool _minimalStateCaptured;
        System.Windows.Forms.Panel? _minimalToolbarPanel;
        System.Windows.Forms.Button? _minimalBtnFullscreen;
        System.Windows.Forms.Button? _minimalBtnWindow;
        System.Windows.Forms.Button? _minimalBtnClose;
        System.Windows.Forms.Timer? _minimalToolbarHideTimer;
        System.Windows.Threading.DispatcherTimer? _minimalPointerWatchTimer;
        System.Drawing.Point _minimalLastCursorPos;
        bool _minimalCursorInitialized;
        bool _minimalToolbarHovering;
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HTCAPTION = 0x2;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;
        const int HTCLIENT = 1;
        const int WM_NCHITTEST = 0x0084;
        const int MinimalResizeGrip = 8;
        HwndSourceHook? _mainWndHook;

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var source = PresentationSource.FromVisual(this) as HwndSource;
                if (source != null)
                {
                    _mainWndHook = MainWindowWndProc;
                    source.AddHook(_mainWndHook);
                }
            }
            catch { }
        }

        IntPtr MainWindowWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_NCHITTEST) return IntPtr.Zero;
            try
            {
                if (_shell.WindowStateActions.IsFullscreen) return IntPtr.Zero;
                if (!_shell.IsMinimalMode) return IntPtr.Zero;
                if (WindowState != WindowState.Normal) return IntPtr.Zero;

                var xy = lParam.ToInt64();
                int x = (short)(xy & 0xFFFF);
                int y = (short)((xy >> 16) & 0xFFFF);
                var pt = PointFromScreen(new System.Windows.Point(x, y));
                int hit = ResolveResizeHitTest((int)pt.X, (int)pt.Y, (int)ActualWidth, (int)ActualHeight);
                if (hit == 0 || hit == HTCLIENT) return IntPtr.Zero;
                handled = true;
                return (IntPtr)hit;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateMinimalResizeCursor(e.GetPosition(this));
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            DispatchWindowFormChanged();
            PositionMinimalToolbar();
            try
            {
                // 状态改变时同步处理精简顶部：仅在窗口Normal且精简模式下可出现
                if (_shell.WindowStateActions.TopOverlay != null)
                {
                    if (WindowState == WindowState.Minimized || _shell.WindowStateActions.IsFullscreen || !_shell.IsMinimalMode)
                    {
                        try
                        {
                            _shell.WindowStateActions.TopOverlay.Close();
                        }
                        catch { }
                        _shell.WindowStateActions.TopOverlay = null;
                    }
                    else
                    {
                        _overlayManager.PositionTopOverlay();
                    }
                }
            }
            catch { }
        }

        void DispatchWindowFormChanged()
        {
            var form = PlaybackWindowForm.Window;
            if (_shell.WindowStateActions.IsFullscreen)
            {
                form = PlaybackWindowForm.Fullscreen;
            }
            else if (WindowState == WindowState.Maximized)
            {
                form = PlaybackWindowForm.Maximized;
            }
            _shell.DispatchPlaybackEvent(new WindowFormChanged(form));
        }
        void OnLanguageChanged()
        {
            try
            {
                _shell.ApplyChannelFilter();
                try { _recordingManager?.ApplyRecordingsFilter(); } catch { }
                _epgManager?.UpdateEpgDateUI();
                _epgManager?.UpdateEpgDisplay();

                _shell.RefreshPlaybackProjection();
                UpdateMinimalToolbarText();
            }
            catch { }
        }

        void OnThemeChanged()
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!_shell.WindowStateActions.IsFullscreen)
                        {
                            UpdateIconBrushes();
                            _overlayManager?.UpdateOverlayIconBrushes();
                            return;
                        }

                        if (CbEpg.IsChecked == true)
                        {
                            try { CloseFullscreenEpg(); } catch { }
                            try { ShowFullscreenEpg(); } catch { }
                        }

                        if (!_shell.IsDrawerCollapsed)
                        {
                            try { CloseFullscreenDrawer(); } catch { }
                            try { ShowFullscreenDrawer(); } catch { }
                        }

                        try { _epgManager?.UpdateEpgDisplay(); } catch { }
                        try { _shell.ApplyChannelFilter(); } catch { }
                        try { UpdateMinimalToolbarStyle(); } catch { }
                        _overlayManager?.UpdateOverlayIconBrushes();
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (_shell.IsMinimalMode && e.Key == Key.Escape)
                {
                    _shell.IsMinimalMode = false;
                    e.Handled = true;
                    return;
                }
                var action = _shell.ShortcutActions.ResolveAction(e.Key);
                if (action != MainWindowShortcutAction.None)
                {
                    _shell.ShortcutActions.ExecuteAction(action);
                    e.Handled = true;
                }
            }
            catch { }
        }

        void OnDrop(object sender, System.Windows.DragEventArgs e)
        {
            try
            {
                _shell.DragDropActions.OnDrop(e);
            }
            catch { }
        }

        void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            _shellSyncManager?.OnShellPropertyChanged(e);
            if (e.PropertyName == nameof(MainShellViewModel.IsMinimalMode))
            {
                ApplyMinimalMode(_shell.IsMinimalMode);
            }
        }

        void OnOverlayVolumeChanged(double v)
        {
            _shell.Volume = v;
        }
        void AdjustVolumeByWheel(int delta)
        {
            int step = 5;
            int dir = delta > 0 ? 1 : -1;
            // 静音时滚动，取消静音
            if (_shell.IsMuted)
            {
                _shell.IsMuted = false;
                _overlayManager.OverlayWpf?.SetMuted(false);
            }
            var newVol = _shell.Volume + dir * step;
            _shell.Volume = Math.Max(0, Math.Min(100, newVol));
            // 显示百分比提示
            _overlayManager.OverlayWpf?.ShowVolumeTooltip(_shell.Volume);
        }

        void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            var state = _shell.TitleBarActions.TogglePin(Topmost);
            Topmost = state.Topmost;
            if (sender is System.Windows.Controls.Primitives.ToggleButton tb)
            {
                tb.IsChecked = state.IsChecked;
            }
        }
        void BtnMin_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
        void BtnMax_Click(object sender, RoutedEventArgs e)
        {
            var next = _shell.TitleBarActions.ToggleMaximize(WindowState);
            if (next == WindowState.Maximized) SystemCommands.MaximizeWindow(this);
            else SystemCommands.RestoreWindow(this);
        }
        void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        void BtnMinimal_Click(object sender, RoutedEventArgs e)
        {
            _shell.IsMinimalMode = !_shell.IsMinimalMode;
        }

        void BtnAppTitle_Click(object sender, RoutedEventArgs e)
        {
            _menuManager?.BtnAppTitle_Click(sender, e);
        }

        void VideoPanel_MouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            _menuManager?.VideoPanel_MouseClick(sender, e);
        }
        void VideoPanel_MouseDown(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (!_shell.IsMinimalMode) return;
            if (_shell.WindowStateActions.IsFullscreen) return;
            if (e.Button != System.Windows.Forms.MouseButtons.Left) return;
            if (BeginWindowResizeFromVideo(e.X, e.Y)) return;
            BeginWindowDragFromVideo();
        }
        void VideoPanel_MouseMoveForMinimal(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (!_shell.IsMinimalMode) return;
            ShowMinimalToolbar();
        }

        void MainWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _menuManager?.MainWindow_MouseRightButtonUp(sender, e);
        }

        void MainPanel_DoubleClick(object? sender, EventArgs e) => _windowedInputManager?.MainPanel_DoubleClick(sender, e);
        void VideoHost_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            _windowedInputManager?.VideoHost_PreviewMouseWheel(sender, e);
        }
        void VideoHost_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_shell.IsMinimalMode) return;
            UpdateMinimalResizeCursor(e.GetPosition(this));
            ShowMinimalToolbar();
        }
        void VideoArea_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            _windowedInputManager?.VideoArea_PreviewMouseWheel(sender, e);
        }
        void VideoArea_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_shell.IsMinimalMode) return;
            UpdateMinimalResizeCursor(e.GetPosition(this));
            ShowMinimalToolbar();
        }
        void VideoArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_shell.IsMinimalMode) return;
            if (_shell.WindowStateActions.IsFullscreen) return;
            if (e.ClickCount != 1) return;
            try
            {
                var pt = e.GetPosition(this);
                if (BeginWindowResizeFromWindowPoint(pt.X, pt.Y))
                {
                    e.Handled = true;
                    return;
                }
            }
            catch { }
            try { DragMove(); } catch { }
        }
        void BottomBar_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            _windowedInputManager?.BottomBar_PreviewMouseWheel(sender, e);
        }
        void ListChannels_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try { ListChannels.Focus(); } catch { }
        }
        void EpgLiveChip_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_shell.CurrentChannel == null) return;
                if (sender is FrameworkElement fe && fe.DataContext is EpgProgram p)
                {
                    var liveLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Live", "直播");
                    if (string.Equals(p.Status, liveLabel, StringComparison.OrdinalIgnoreCase) || p.Status == "直播")
                    {
                        PlayChannel(_shell.CurrentChannel);
                    }
                }
            }
            catch { }
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var drawerWidth = _shell.DrawerWidth > 0 ? _shell.DrawerWidth : 380;
                _baseWindowWidth = Width - (_shell.IsDrawerCollapsed ? 0 : drawerWidth) - (CbEpg.IsChecked == true ? 320 : 0);

                InitializeAppServices();
                var playerEngine = InitializePlayer();
                InitializeViewModel(playerEngine);

                _overlayManager.Initialize();
                _overlayManager.BuildOverlayWindow(
                    () => BtnPlayPause_Click(this, new RoutedEventArgs()),
                    () => BtnStop_Click(this, new RoutedEventArgs()),
                    () => BtnRew_Click(this, new RoutedEventArgs()),
                    () => BtnFwd_Click(this, new RoutedEventArgs()),
                    () => OpenSourceMenuAtOverlay()
                );

                InitializeBindings();
                SyncM3uComboBoxSelection();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _recordingManager?.Initialize();
                    InitializeAutoLoad();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                try { Logger.Error("初始化失败 " + ex.ToString()); } catch { }
                System.Windows.MessageBox.Show(this, ex.Message, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Err_StartupFailed", "启动失败"), MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }
        }

        void InitializeAppServices()
        {
            _startupManager?.InitializeAppServices();
        }

        IPlayerEngine InitializePlayer()
        {
            _mpv = new MpvInterop();
            _mpv.Create();
            _mpv.SetSettings(AppSettings.Current);
            var hwnd = new WindowInteropHelper(this).Handle;
            var panelHwnd = VideoPanel.Handle;
            _mpv.SetWid(panelHwnd);
            _mpv.Initialize();
            _shell.Volume = 60;
            return new MpvPlayerEngineAdapter(_mpv);
        }

        void InitializeViewModel(IPlayerEngine playerEngine)
        {
            _epgService = new EpgService();
            _recordingManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowRecordingManager(this, _shell, _epgService);
            _epgManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowEpgManager(this, _shell, _overlayManager, _epgService);
            _shell.InitializeServices();
            _shell.SetHttpClient(_http);

            _shell.InjectServices(
                _epgService,
                playerEngine,
                _userDataStore,
                (cur, tot) => { try { _overlayManager.OverlayWpf?.SetTime(cur, tot); } catch { } },
                (min, max) => { try { _overlayManager.OverlayWpf?.SetTimeshiftRange(min, max); } catch { } },
                (cursor, max, seeking) => { try { _overlayManager.OverlayWpf?.SetTimeshiftLabels(cursor, max, seeking); } catch { } }
            );

            // 初始化 Web 遥控器服务
            try { LibmpvIptvClient.Services.WebRemote.WebRemoteManager.Initialize(_shell); } catch { }
            try { LibmpvIptvClient.Services.WebRemote.WebRemoteManager.SetToggleFullscreenCallback(ToggleFullscreenFromManager); } catch { }

            _shell.ChannelPlaybackActions.RequestEpgRefresh += () => { try { ListEpg.Items.Refresh(); } catch { } };
            _shell.ChannelPlaybackActions.RequestHistoryRefresh += () => { try { ListHistory.ItemsSource = _userDataStore.GetHistory(); } catch { } };
            _shell.ChannelPlaybackActions.RequestEpgReload += (ch, focusTime) =>
            {
                try
                {
                    if (EpgPanel.Visibility != Visibility.Visible) return;
                    if (focusTime.HasValue) _epgManager?.RefreshEpgListWithFocus(ch, focusTime.Value, true);
                    else _epgManager?.RefreshEpgList(ch);
                }
                catch { }
            };
            _shell.ChannelPlaybackActions.RequestVideoShow += () => { try { PlaceholderPanel.Visibility = Visibility.Collapsed; VideoHost.Visibility = Visibility.Visible; Diagnostics.Logger.Info($"[WebRemote] 视频画面已显示"); } catch (Exception ex) { Diagnostics.Logger.Error($"[WebRemote] RequestVideoShow error: {ex.Message}"); } };

            _shell.RecordingPlaybackActions.RequestEpgRefresh += () => { try { ListEpg.Items.Refresh(); } catch { } };
            _shell.RecordingPlaybackActions.RequestVideoShow += () => { try { PlaceholderPanel.Visibility = Visibility.Collapsed; VideoHost.Visibility = Visibility.Visible; } catch { } };
            _shell.RecordingPlaybackActions.RequestPlayModeChoice = (canNetwork, canLocal) =>
            {
                try
                {
                    var title = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Play_Mode_Title", "播放方式");
                    string desc;
                    if (canLocal && canNetwork)
                        desc = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Play_Mode_Desc_Both", "此录播同时存在网络与本地，请选择播放方式");
                    else if (canNetwork)
                        desc = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Play_Mode_Desc_RemoteOnly", "此录播仅有网络版本");
                    else
                        desc = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_Recordings_Play_Mode_Desc_LocalOnly", "此录播仅有本地版本");
                    var yesLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Btn_Play_Remote", "网络");
                    var noLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Btn_Play_Local", "本地");
                    var choice = LibmpvIptvClient.ModernMessageBox.ShowCustom(this, desc, title, yesLabel, noLabel, canNetwork, canLocal);
                    return System.Threading.Tasks.Task.FromResult(choice);
                }
                catch { return System.Threading.Tasks.Task.FromResult<bool?>(null); }
            };

            _shell.MenuActions.RequestLoadChannels += (url) => { try { _ = _shell.LoadChannels(url); } catch { } };
            _shell.MenuActions.RequestLoadSingleStream += (url) => { try { _shell.LoadSingleStream(url); } catch { } };
            _shell.MenuActions.RequestFccUpdate += (on) => { try { _overlayManager.OverlayWpf?.SetFcc(on); } catch { } };
            _shell.MenuActions.RequestUdpUpdate += (on) => { try { _overlayManager.OverlayWpf?.SetUdp(on); } catch { } };
            _shell.MenuActions.RequestDeinterlaceUpdate += (on) => { try { /* 立即生效已在 ToggleDeinterlace 完成 */ } catch { } };
            _shell.MenuActions.RequestEpgToggle += (on) => { try { CbEpg.IsChecked = on; CbEpg_Click(CbEpg, new RoutedEventArgs()); } catch { } };
            _shell.MenuActions.RequestMinimalToggle += (on) => { try { _shell.IsMinimalMode = on; } catch { } };
            _shell.ShortcutActions.RequestDebugWindow += () => BtnDebug_Click(this, new RoutedEventArgs());
            _shell.ShortcutActions.RequestToggleFullscreen += (on) => ToggleFullscreen(on);
            _shell.ShortcutActions.RequestToggleDrawer += () =>
            {
                CbDrawer.IsChecked = !(CbDrawer.IsChecked == true);
                CbDrawer_Click(CbDrawer, new RoutedEventArgs());
            };
            _shell.ShortcutActions.RequestToggleEpg += () =>
            {
                CbEpg.IsChecked = !(CbEpg.IsChecked == true);
                CbEpg_Click(CbEpg, new RoutedEventArgs());
            };

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += OnTick;
            _timer.Start();

            if (DrawerColumn != null) _shell.DrawerWidth = DrawerColumn.Width.Value;

            try { VideoHost.Visibility = Visibility.Collapsed; } catch { }
        }

        void InitializeBindings()
        {
            ListChannels.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("FilteredChannels"));
            ListGroups.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("ChannelGroups"));
            ListFavorites.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("Favorites"));
            ListHistory.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("History"));
            TxtCount.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("FilterCountText"));
            TxtSearch.SetBinding(System.Windows.Controls.TextBox.TextProperty, new System.Windows.Data.Binding("SearchText") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            _epgManager?.AttachEpgListInteractions();

            _epgTimer.Interval = TimeSpan.FromMinutes(1);
            _epgTimer.Tick += (s, e) => _epgManager?.UpdateEpgDisplay();
            _epgTimer.Start();
            Logger.Info("应用启动完成");

            try { _userDataStore.Load(); } catch { }
            LocationChanged += (_, __) => PositionMinimalToolbar();
            SizeChanged += (_, __) => PositionMinimalToolbar();
            try { VideoPanel.Resize += (_, __) => PositionMinimalToolbar(); } catch { }
        }

        void ApplyMinimalMode(bool on)
        {
            if (_isApplyingMinimalMode) return;
            _isApplyingMinimalMode = true;
            try
            {
                if (on)
                {
                    _minimalPrevEpgChecked = CbEpg.IsChecked == true;
                    _minimalPrevDrawerCollapsed = _shell.IsDrawerCollapsed;
                    _minimalStateCaptured = true;

                    try
                    {
                        CbEpg.IsChecked = false;
                        CbEpg_Click(CbEpg, new RoutedEventArgs());
                    }
                    catch { }

                    try
                    {
                        _shell.IsDrawerCollapsed = true;
                        if (CbDrawer != null) CbDrawer.IsChecked = false;
                    }
                    catch { }

                    try { DrawerPanel.Visibility = Visibility.Collapsed; } catch { }
                    
                    try
                    {
                        if (!_shell.WindowStateActions.IsFullscreen && WindowState != WindowState.Minimized)
                        {
                            try
                            {
                                if (_shell.WindowStateActions.TopOverlay != null)
                                {
                                    _shell.WindowStateActions.TopOverlay.Close();
                                    _shell.WindowStateActions.TopOverlay = null;
                                }
                            }
                            catch { }
                            var topOverlay = new TopOverlay();
                            topOverlay.Owner = this;
                            _shell.WindowStateActions.TopOverlay = topOverlay;
                            _shell.OverlayBindingActions.BindTopOverlay(topOverlay, new TopOverlayBindingContext(
                                Minimize: () => BtnMin_Click(this, new RoutedEventArgs()),
                                MaximizeRestore: () => BtnMax_Click(this, new RoutedEventArgs()),
                                CloseWindow: () => Close(),
                                ExitApp: () => _shell.MenuActions.ExitApp(),
                                FullscreenToggle: () => ToggleFullscreen(true),
                                OpenFile: () => _shell.MenuActions.OpenFile(),
                                OpenUrl: () => _shell.MenuActions.OpenUrl(),
                                OpenSettings: OpenSettings,
                                AddM3u: () => _shell.MenuActions.AddM3uUrl(),
                                AddM3uFile: () => _shell.MenuActions.AddM3uFile(),
                                LoadM3u: (src) => _shell.MenuActions.LoadM3u(src),
                                EditM3u: (src) => _shell.MenuActions.EditM3u(src),
                                FccChanged: (val) => _shell.MenuActions.ToggleFcc(val),
                                UdpChanged: (val) => _shell.MenuActions.ToggleUdp(val),
                                EpgToggled: (val) => { CbEpg.IsChecked = val; CbEpg_Click(CbEpg, new RoutedEventArgs()); },
                                DrawerToggled: (visible) => SetDrawerCollapsed(!visible),
                                MinimalModeChanged: (val) => _shell.MenuActions.ToggleMinimalMode(val),
                                DeinterlaceChanged: (val) => _shell.MenuActions.ToggleDeinterlace(val),
                                TopmostChanged: (val) => { Topmost = val; if (_shell.WindowStateActions.TopOverlay != null) _shell.WindowStateActions.TopOverlay.Topmost = true; },
                                IsUdpEnabled: () => AppSettings.Current.EnableUdpOptimization,
                                IsTopmost: () => Topmost,
                                IsEpgVisible: () => CbEpg.IsChecked == true,
                                IsDrawerVisible: () => !_shell.IsDrawerCollapsed,
                                IsMinimalMode: () => _shell.IsMinimalMode
                            ));
                            try { _shell.WindowStateActions.TopOverlay.Height = 28; } catch { }
                            try { _shell.WindowStateActions.TopOverlay.Show(); } catch { }
                            try { _overlayManager.PositionTopOverlay(); } catch { }
                            try { _shell.WindowStateActions.TopOverlay.SyncWindowVisual(false, WindowState, _shell.IsMinimalMode); } catch { }
                        }
                    }
                    catch { }
                }
                else
                {
                    if (_minimalStateCaptured)
                    {
                        try
                        {
                            CbEpg.IsChecked = _minimalPrevEpgChecked;
                            CbEpg_Click(CbEpg, new RoutedEventArgs());
                        }
                        catch { }

                        try
                        {
                            _shell.IsDrawerCollapsed = _minimalPrevDrawerCollapsed;
                            if (CbDrawer != null) CbDrawer.IsChecked = !_shell.IsDrawerCollapsed;
                        }
                        catch { }
                    }

                    try { DrawerPanel.Visibility = _shell.IsDrawerCollapsed ? Visibility.Collapsed : Visibility.Visible; } catch { }
                    try
                    {
                        if (!_shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.TopOverlay != null)
                        {
                            _shell.WindowStateActions.TopOverlay.Close();
                            _shell.WindowStateActions.TopOverlay = null;
                        }
                    }
                    catch { }

                    // 修复 Bug #2：精简模式退出时强制刷新频道列表
                    // 解决 ListBox 虚拟化（Recycling）在 DrawerPanel 隐藏→显示后容器未重新生成的问题
                    try
                    {
                        _shell.ApplyChannelFilter();
                        if (ListChannels != null) ListChannels.Items.Refresh();
                        if (ListGroups != null) ListGroups.Items.Refresh();
                    }
                    catch { }
                }
            }
            finally
            {
                _isApplyingMinimalMode = false;
            }
        }

        void ShowMinimalToolbar()
        {
            if (_shell.WindowStateActions.TopOverlay != null && !_shell.WindowStateActions.IsFullscreen) return;
            if (_shell.WindowStateActions.IsFullscreen) return;
            EnsureMinimalToolbarCreated();
            if (_minimalToolbarPanel == null) return;
            UpdateMinimalToolbarText();
            UpdateMinimalToolbarStyle();
            PositionMinimalToolbar();
            _minimalToolbarPanel.Visible = true;
            try { _minimalToolbarPanel.BringToFront(); } catch { }
            RestartMinimalToolbarHideCountdown();
        }

        void EnsureMinimalToolbarCreated()
        {
            if (_shell.WindowStateActions.TopOverlay != null && !_shell.WindowStateActions.IsFullscreen) return;
            if (_minimalToolbarPanel != null) return;
            try
            {
                var panel = new System.Windows.Forms.Panel
                {
                    Width = 132,
                    Height = 26,
                    Visible = false,
                    BackColor = System.Drawing.Color.FromArgb(230, 16, 16, 16)
                };
                panel.MouseEnter += (_, __) => { _minimalToolbarHovering = true; SetMinimalToolbarHover(true); };
                panel.MouseLeave += (_, __) => { _minimalToolbarHovering = false; SetMinimalToolbarHover(false); };
                panel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;

                var btnFull = new System.Windows.Forms.Button
                {
                    Width = 40,
                    Height = 24,
                    FlatStyle = System.Windows.Forms.FlatStyle.Flat,
                    TabStop = false,
                    UseVisualStyleBackColor = false,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };
                btnFull.FlatAppearance.BorderSize = 1;
                btnFull.Left = 0;
                btnFull.Top = 1;
                btnFull.Click += (_, __) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _shell.IsMinimalMode = false;
                    ToggleFullscreen(true);
                }));
                btnFull.MouseEnter += (_, __) => { _minimalToolbarHovering = true; SetMinimalToolbarHover(true); };
                btnFull.MouseLeave += (_, __) => { _minimalToolbarHovering = false; SetMinimalToolbarHover(false); };

                var btnWindow = new System.Windows.Forms.Button
                {
                    Width = 40,
                    Height = 24,
                    FlatStyle = System.Windows.Forms.FlatStyle.Flat,
                    TabStop = false,
                    UseVisualStyleBackColor = false,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Left = 42,
                    Top = 1
                };
                btnWindow.FlatAppearance.BorderSize = 1;
                btnWindow.Click += (_, __) => Dispatcher.BeginInvoke(new Action(() => _shell.IsMinimalMode = false));
                btnWindow.MouseEnter += (_, __) => { _minimalToolbarHovering = true; SetMinimalToolbarHover(true); };
                btnWindow.MouseLeave += (_, __) => { _minimalToolbarHovering = false; SetMinimalToolbarHover(false); };

                var btnClose = new System.Windows.Forms.Button
                {
                    Width = 40,
                    Height = 24,
                    FlatStyle = System.Windows.Forms.FlatStyle.Flat,
                    TabStop = false,
                    UseVisualStyleBackColor = false,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Left = 84,
                    Top = 1
                };
                btnClose.FlatAppearance.BorderSize = 1;
                btnClose.Click += (_, __) => Dispatcher.BeginInvoke(new Action(() => BtnClose_Click(this, new RoutedEventArgs())));
                btnClose.MouseEnter += (_, __) => { _minimalToolbarHovering = true; SetMinimalToolbarHover(true); };
                btnClose.MouseLeave += (_, __) => { _minimalToolbarHovering = false; SetMinimalToolbarHover(false); };

                panel.Controls.Add(btnFull);
                panel.Controls.Add(btnWindow);
                panel.Controls.Add(btnClose);

                _minimalToolbarPanel = panel;
                _minimalBtnFullscreen = btnFull;
                _minimalBtnWindow = btnWindow;
                _minimalBtnClose = btnClose;
                try { VideoPanel.Controls.Add(panel); } catch { }
            }
            catch
            {
                _minimalToolbarPanel = null;
                _minimalBtnFullscreen = null;
                _minimalBtnWindow = null;
                _minimalBtnClose = null;
            }
        }

        void HideMinimalToolbar()
        {
            try { _minimalToolbarHideTimer?.Stop(); } catch { }
            try { if (_minimalToolbarPanel != null) _minimalToolbarPanel.Visible = false; } catch { }
        }

        void PositionMinimalToolbar()
        {
            if (_minimalToolbarPanel == null || !_shell.IsMinimalMode) return;
            if (_shell.WindowStateActions.IsFullscreen || VideoPanel == null)
            {
                try { _minimalToolbarPanel.Visible = false; } catch { }
                return;
            }
            try
            {
                var x = Math.Max(8, VideoPanel.Width - _minimalToolbarPanel.Width - 8);
                _minimalToolbarPanel.Left = x;
                _minimalToolbarPanel.Top = 8;
                _minimalToolbarPanel.BringToFront();
            }
            catch { }
        }

        void StartMinimalPointerWatch()
        {
            if (_minimalPointerWatchTimer == null)
            {
                _minimalPointerWatchTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(120)
                };
                _minimalPointerWatchTimer.Tick += (_, __) => PollMinimalPointerMovement();
            }
            _minimalCursorInitialized = false;
            _minimalPointerWatchTimer.Stop();
            _minimalPointerWatchTimer.Start();
        }

        void StopMinimalPointerWatch()
        {
            try { _minimalPointerWatchTimer?.Stop(); } catch { }
            _minimalCursorInitialized = false;
        }

        void PollMinimalPointerMovement()
        {
            if (!_shell.IsMinimalMode) return;
            if (_shell.WindowStateActions.IsFullscreen) return;
            if (!IsVisible) return;
            try
            {
                var pos = System.Windows.Forms.Cursor.Position;
                if (!_minimalCursorInitialized)
                {
                    _minimalLastCursorPos = pos;
                    _minimalCursorInitialized = true;
                    return;
                }
                if (pos == _minimalLastCursorPos) return;
                _minimalLastCursorPos = pos;
                var pt = PointFromScreen(new System.Windows.Point(pos.X, pos.Y));
                if (pt.X < 0 || pt.Y < 0 || pt.X > ActualWidth || pt.Y > ActualHeight) return;
                ShowMinimalToolbar();
            }
            catch { }
        }

        void RestartMinimalToolbarHideCountdown()
        {
            if (_minimalToolbarPanel == null) return;
            if (_minimalToolbarHideTimer == null)
            {
                _minimalToolbarHideTimer = new System.Windows.Forms.Timer
                {
                    Interval = 3000
                };
                _minimalToolbarHideTimer.Tick += (_, __) =>
                {
                    _minimalToolbarHideTimer.Stop();
                    if (_minimalToolbarPanel != null && _shell.IsMinimalMode && !_minimalToolbarHovering)
                    {
                        _minimalToolbarPanel.Visible = false;
                    }
                };
            }
            _minimalToolbarPanel.Visible = true;
            _minimalToolbarHideTimer.Stop();
            _minimalToolbarHideTimer.Start();
        }

        void SetMinimalToolbarHover(bool hover)
        {
            if (_minimalToolbarPanel == null) return;
            if (hover)
            {
                _minimalToolbarPanel.Visible = true;
                try { _minimalToolbarHideTimer?.Stop(); } catch { }
                return;
            }
            RestartMinimalToolbarHideCountdown();
        }

        void UpdateMinimalToolbarText()
        {
            if (_minimalBtnFullscreen != null) _minimalBtnFullscreen.Text = "全";
            if (_minimalBtnWindow != null) _minimalBtnWindow.Text = "窗";
            if (_minimalBtnClose != null) _minimalBtnClose.Text = "关";
        }

        void UpdateMinimalToolbarStyle()
        {
            if (_minimalToolbarPanel == null) return;
            try
            {
                _minimalToolbarPanel.BackColor = System.Drawing.Color.FromArgb(230, 16, 16, 16);
                ApplyMinimalToolbarButtonStyle(_minimalBtnFullscreen);
                ApplyMinimalToolbarButtonStyle(_minimalBtnWindow);
                ApplyMinimalToolbarButtonStyle(_minimalBtnClose);
            }
            catch { }
        }

        void ApplyMinimalToolbarButtonStyle(System.Windows.Forms.Button? btn)
        {
            if (btn == null) return;
            btn.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            btn.BackColor = System.Drawing.Color.FromArgb(255, 36, 36, 36);
            btn.ForeColor = System.Drawing.Color.White;
            btn.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(255, 255, 255);
            btn.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(255, 72, 72, 72);
            btn.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(255, 96, 96, 96);
        }

        bool BeginWindowResizeFromVideo(int x, int y)
        {
            if (WindowState != WindowState.Normal) return false;
            if (VideoPanel == null) return false;
            var hit = ResolveResizeHitTest(x, y, VideoPanel.Width, VideoPanel.Height);
            if (hit == 0) return false;
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return false;
                ReleaseCapture();
                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)hit, IntPtr.Zero);
                return true;
            }
            catch
            {
                return false;
            }
        }

        bool BeginWindowResizeFromWindowPoint(double x, double y)
        {
            if (WindowState != WindowState.Normal) return false;
            var hit = ResolveResizeHitTest((int)x, (int)y, (int)ActualWidth, (int)ActualHeight);
            if (hit == 0) return false;
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return false;
                ReleaseCapture();
                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)hit, IntPtr.Zero);
                return true;
            }
            catch
            {
                return false;
            }
        }

        int ResolveResizeHitTest(int x, int y, int width, int height)
        {
            bool left = x <= MinimalResizeGrip;
            bool right = x >= Math.Max(0, width - MinimalResizeGrip);
            bool top = y <= MinimalResizeGrip;
            bool bottom = y >= Math.Max(0, height - MinimalResizeGrip);

            if (left && top) return HTTOPLEFT;
            if (right && top) return HTTOPRIGHT;
            if (left && bottom) return HTBOTTOMLEFT;
            if (right && bottom) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return 0;
        }

        void UpdateMinimalResizeCursor(System.Windows.Point pt)
        {
            if (!_shell.IsMinimalMode || _shell.WindowStateActions.IsFullscreen || WindowState != WindowState.Normal)
            {
                if (Cursor == System.Windows.Input.Cursors.SizeWE || Cursor == System.Windows.Input.Cursors.SizeNS || Cursor == System.Windows.Input.Cursors.SizeNWSE || Cursor == System.Windows.Input.Cursors.SizeNESW) Cursor = null;
                try { if (VideoPanel != null) VideoPanel.Cursor = System.Windows.Forms.Cursors.Default; } catch { }
                return;
            }
            int hit = ResolveResizeHitTest((int)pt.X, (int)pt.Y, (int)ActualWidth, (int)ActualHeight);
            if (hit == HTLEFT || hit == HTRIGHT)
            {
                Cursor = System.Windows.Input.Cursors.SizeWE;
                try { if (VideoPanel != null) VideoPanel.Cursor = System.Windows.Forms.Cursors.SizeWE; } catch { }
                return;
            }
            if (hit == HTTOP || hit == HTBOTTOM)
            {
                Cursor = System.Windows.Input.Cursors.SizeNS;
                try { if (VideoPanel != null) VideoPanel.Cursor = System.Windows.Forms.Cursors.SizeNS; } catch { }
                return;
            }
            if (hit == HTTOPLEFT || hit == HTBOTTOMRIGHT)
            {
                Cursor = System.Windows.Input.Cursors.SizeNWSE;
                try { if (VideoPanel != null) VideoPanel.Cursor = System.Windows.Forms.Cursors.SizeNWSE; } catch { }
                return;
            }
            if (hit == HTTOPRIGHT || hit == HTBOTTOMLEFT)
            {
                Cursor = System.Windows.Input.Cursors.SizeNESW;
                try { if (VideoPanel != null) VideoPanel.Cursor = System.Windows.Forms.Cursors.SizeNESW; } catch { }
                return;
            }
            if (Cursor == System.Windows.Input.Cursors.SizeWE || Cursor == System.Windows.Input.Cursors.SizeNS || Cursor == System.Windows.Input.Cursors.SizeNWSE || Cursor == System.Windows.Input.Cursors.SizeNESW) Cursor = null;
            try { if (VideoPanel != null) VideoPanel.Cursor = System.Windows.Forms.Cursors.Default; } catch { }
        }

        void BeginWindowDragFromVideo()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                ReleaseCapture();
                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
            catch { }
        }

        void InitializeAutoLoad()
        {
            _startupManager?.InitializeAutoLoad();
        }
    }
}
