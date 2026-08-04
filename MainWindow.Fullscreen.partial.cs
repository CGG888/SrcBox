using System;
using System.Windows;
using System.Windows.Controls;
using LibmpvIptvClient.Architecture.Platform.Player;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

namespace LibmpvIptvClient
{
    public partial class MainWindow : Window
    {
        void ToggleFullscreen(bool on)
        {
            var ctx = new LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow.FullscreenContext
            {
                MainWindow = this,
                Mpv = _mpv,
                WindowedPanel = VideoPanel,
                SyncTimeshiftUi = () => { if (on || _shell.IsTimeshiftActive) _overlayManager.SyncTimeshiftUi(); },
                ResetOverlayForOwner = () => {
                    _overlayManager.ResetOverlayForOwner(
                        _shell.WindowStateActions.FullscreenWindow ?? (Window)this,
                        _shell.WindowStateActions.IsFullscreen,
                        () => BtnPlayPause_Click(this, new RoutedEventArgs()),
                        () => BtnStop_Click(this, new RoutedEventArgs()),
                        () => BtnRew_Click(this, new RoutedEventArgs()),
                        () => BtnFwd_Click(this, new RoutedEventArgs()),
                        () => OpenSourceMenuAtOverlay()
                    );
                    if (!on) UpdateIconBrushes();
                },
                ShowFsOverlayNow = _overlayManager.ShowFsOverlayNow,
                TryArrowSeek = (dir) =>
                {
                    if (_shell.CurrentPlayingProgram != null)
                        _shell.PlaybackActions.TrySeekRelative(_shell.PlayerEngine, dir * 10);
                },
                TogglePlayPause = () => BtnPlayPause_Click(this, new RoutedEventArgs()),
                PositionOverlay = () => { _overlayManager.PositionOverlay(); _overlayManager.PositionTopOverlay(); },
                ShowOverlayWithDelay = _overlayManager.ShowOverlayWithDelay,
                FsHostPreviewMouseWheel = (e) => FsHost_PreviewMouseWheel(null, e),
                FsPanelMouseWheel = (e) => FsPanel_MouseWheel(null, e),
                FsPanelMouseUp = (e) => FsPanel_MouseUp(null, e),
                FsPanelDoubleClick = () => ToggleFullscreen(false),
                OnLoaded = OnFullscreenLoaded,
                CreateTopOverlay = () =>
                {
                    if (_shell.WindowStateActions.FullscreenWindow != null)
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
                        topOverlay.Owner = _shell.WindowStateActions.FullscreenWindow;
                        _shell.WindowStateActions.TopOverlay = topOverlay;

                        _shell.OverlayBindingActions.BindTopOverlay(topOverlay, new TopOverlayBindingContext(
                            Minimize: () => BtnMin_Click(this, new RoutedEventArgs()),
                            MaximizeRestore: () => ToggleFullscreen(false),
                            CloseWindow: () => _shell.MenuActions.ExitApp(),
                            ExitApp: () => _shell.MenuActions.ExitApp(),
                            FullscreenToggle: () => ToggleFullscreen(false),
                            OpenFile: () => { ToggleFullscreen(false); _shell.MenuActions.OpenFile(); },
                            OpenUrl: () => { ToggleFullscreen(false); _shell.MenuActions.OpenUrl(); },
                            OpenSettings: OpenSettings,
                            AddM3u: () => { ToggleFullscreen(false); _shell.MenuActions.AddM3uUrl(); },
                            AddM3uFile: () => { ToggleFullscreen(false); _shell.MenuActions.AddM3uFile(); },
                            LoadM3u: (src) => _shell.MenuActions.LoadM3u(src),
                            EditM3u: (src) => _shell.MenuActions.EditM3u(src),
                            FccChanged: (val) => _shell.MenuActions.ToggleFcc(val),
                            UdpChanged: (val) => _shell.MenuActions.ToggleUdp(val),
                            EpgToggled: (val) => _shell.MenuActions.ToggleEpg(val),
                            DrawerToggled: (val) => _shell.MenuActions.ToggleDrawer(val),
                            MinimalModeChanged: (val) => _shell.MenuActions.ToggleMinimalMode(val),
                            DeinterlaceChanged: (val) => _shell.MenuActions.ToggleDeinterlace(val),
                            TopmostChanged: (val) =>
                            {
                                Topmost = val;
                                if (_shell.WindowStateActions.FullscreenWindow != null) _shell.WindowStateActions.FullscreenWindow.Topmost = val;
                                if (_shell.WindowStateActions.TopOverlay != null) _shell.WindowStateActions.TopOverlay.Topmost = true;
                            },
                            IsUdpEnabled: () => AppSettings.Current.EnableUdpOptimization,
                            IsTopmost: () => Topmost,
                            IsEpgVisible: () => CbEpg.IsChecked == true,
                            IsDrawerVisible: () => !_shell.IsDrawerCollapsed,
                            IsMinimalMode: () => _shell.IsMinimalMode,
                            ShortcutKeyPressed: (key) => {
                                var action = _shell.ShortcutActions.ResolveAction(key);
                                if (action != MainWindowShortcutAction.None)
                                {
                                    _shell.ShortcutActions.ExecuteAction(action);
                                    return true;
                                }
                                return false;
                            }
                        ));
                        try { _shell.WindowStateActions.TopOverlay.SyncWindowVisual(true, WindowState.Maximized, _shell.IsMinimalMode); } catch { }
                    }
                },
                SyncEpgVisibility = () =>
                {
                    bool isEpgOn = CbEpg.IsChecked == true;
                    if (isEpgOn)
                    {
                        EpgPanel.Visibility = Visibility.Visible;
                        EpgColumn.Width = new GridLength(320);
                    }
                    else
                    {
                        EpgPanel.Visibility = Visibility.Collapsed;
                        EpgColumn.Width = new GridLength(0);
                    }

                    if (!_shell.IsDrawerCollapsed)
                    {
                        DrawerPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        DrawerPanel.Visibility = Visibility.Collapsed;
                    }
                },
                HandleShortcutKey = (key) =>
                {
                    var action = _shell.ShortcutActions.ResolveAction(key);
                    if (action != MainWindowShortcutAction.None)
                    {
                        _shell.ShortcutActions.ExecuteAction(action);
                        return true;
                    }
                    return false;
                }
            };

            if (on)
            {
                _timer.Stop();
                _overlayManager.StopTimers();
            }

            _shell.WindowStateActions.ToggleFullscreen(on, ctx);
            DispatchWindowFormChanged();

            if (!on)
            {
                RestoreDrawerEpgLayoutForWindow();
                _timer.Start();
                _overlayManager.StartTimers();
                // 延迟刷新，等 layout 完成后再刷新频道列表
                Dispatcher.InvokeAsync(() =>
                {
                    try { _shell.ApplyChannelFilter(); } catch { }
                    try { if (ListChannels != null) ListChannels.Items.Refresh(); } catch { }
                    try { if (ListGroups != null) ListGroups.Items.Refresh(); } catch { }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        void RestoreDrawerEpgLayoutForWindow()
        {
            if (_shell.WindowStateActions.IsFullscreen) return;
            try
            {
                Grid.SetRow(DrawerPanel, 1);
                Grid.SetColumn(DrawerPanel, 2);
                Grid.SetRowSpan(DrawerPanel, 1);
            }
            catch { }
            try
            {
                Grid.SetRow(EpgPanel, 1);
                Grid.SetColumn(EpgPanel, 0);
                Grid.SetRowSpan(EpgPanel, 1);
            }
            catch { }
        }

        void FsHost_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            _fullscreenInputManager?.FsHost_PreviewMouseWheel(sender, e);
        }
        void FsPanel_MouseWheel(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            _fullscreenInputManager?.FsPanel_MouseWheel(sender, e);
        }
        void FsPanel_MouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            _fullscreenInputManager?.FsPanel_MouseUp(sender, e);
        }
        internal void StartPlaybackTimer()
        {
            _timer.Start();
        }

        void OnFullscreenLoaded(object? sender, RoutedEventArgs e)
        {
            _overlayManager.OnFullscreenLoaded(sender, e);
        }
        internal void ShowFullscreenDrawer()
        {
            _shell.WindowStateActions.ShowFullscreenDrawer(this, DrawerPanel, _shell.DrawerWidth);
        }
        internal void CloseFullscreenDrawer()
        {
            _shell.WindowStateActions.CloseFullscreenDrawer(this, DrawerPanel);
            if (!_shell.WindowStateActions.IsFullscreen)
            {
                DrawerPanel.ClearValue(UIElement.VisibilityProperty);
            }
        }

        internal void ShowFullscreenEpg()
        {
            _shell.WindowStateActions.ShowFullscreenEpg(this, EpgPanel);
        }

        internal void CloseFullscreenEpg()
        {
            _shell.WindowStateActions.CloseFullscreenEpg(this, EpgPanel);

            bool isEpgOn = CbEpg.IsChecked == true;
            if (isEpgOn)
            {
                EpgPanel.Visibility = Visibility.Visible;
                EpgColumn.Width = new GridLength(320);
            }
            else
            {
                EpgPanel.Visibility = Visibility.Collapsed;
                EpgColumn.Width = new GridLength(0);
            }
            if (!_shell.WindowStateActions.IsFullscreen)
            {
                EpgPanel.ClearValue(UIElement.VisibilityProperty);
            }
        }
        void FsPanel_DoubleClick(object? sender, EventArgs e) => _fullscreenInputManager?.FsPanel_DoubleClick(sender, e);
        string SanitizeUrl(string input)
        {
            return _shell.SourceLoader.SanitizeUrl(input);
        }
        void ToggleDebugWindow()
        {
            try
            {
                if (_debug != null && _debug.IsVisible)
                {
                    _debug.Close();
                    return;
                }
                _debug = new DebugWindow();
                _debug.Owner = this;
                _debug.Show();
            }
            catch { }
        }
        void BtnDebug_Click(object sender, RoutedEventArgs e) => ToggleDebugWindow();
        internal void OpenDebugWindowFromManager() => BtnDebug_Click(this, new RoutedEventArgs());
        internal LibmpvIptvClient.Architecture.Presentation.View.MainWindowMenuManager? MenuManagerForManager => _menuManager;
        internal void AdjustVolumeByWheelFromManager(int delta) => AdjustVolumeByWheel(delta);
        internal void ShowOverlayWithDelayFromManager() => _overlayManager.ShowOverlayWithDelay();
        internal void ToggleFullscreenFromManager(bool on) => ToggleFullscreen(on);
        internal MpvInterop? PlayerInterop => _mpv;
        internal System.Windows.Controls.Slider SeekSliderForManager => SliderSeek;
        internal LibmpvIptvClient.Architecture.Presentation.View.MainWindowEpgManager? EpgManagerForManager => _epgManager;
        internal DateTime PlayStartTimeForManager { get => _playStartTime; set => _playStartTime = value; }
        internal bool FirstFrameLoggedForManager { get => _firstFrameLogged; set => _firstFrameLogged = value; }
    }
}
