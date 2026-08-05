using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Helpers;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowMenuManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;
        private readonly MainWindowOverlayManager _overlayManager;

        public MainWindowMenuManager(MainWindow window, MainShellViewModel shell, MainWindowOverlayManager overlayManager)
        {
            _window = window;
            _shell = shell;
            _overlayManager = overlayManager;
        }

        public void BtnAppTitle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cm = CreateAppMenu();
                _window.BtnAppTitle.ContextMenu = cm;
                _window.BtnAppTitle.ContextMenu.PlacementTarget = _window.BtnAppTitle;
                _window.BtnAppTitle.ContextMenu.IsOpen = true;
            }
            catch { }
        }

        public ContextMenu CreateAppMenu()
        {
            MenuBuilder.SetSpeedCallback((speed) =>
            {
                System.Diagnostics.Debug.WriteLine($"[MenuManager.SetSpeedCallback] ENTER speed={speed}");
                System.Diagnostics.Debug.WriteLine($"[MenuManager.SetSpeedCallback] OverlayWpf is null: {_overlayManager.OverlayWpf == null}");
                _shell.PlaybackSpeed = speed;
                _shell.IsSpeedEnabled = true;
                MenuBuilder.SetCurrentSpeed(speed);
                MenuBuilder.RefreshAllSpeedChecks(speed);
                if (_overlayManager.OverlayWpf != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MenuManager.SetSpeedCallback] calling SetSpeed {speed}");
                    _overlayManager.OverlayWpf.SetSpeed(speed);
                    System.Diagnostics.Debug.WriteLine($"[MenuManager.SetSpeedCallback] SetSpeed called successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[MenuManager.SetSpeedCallback] OverlayWpf is NULL, skipping SetSpeed");
                }
            });

            MenuBuilder.SetRatioCallback((val) =>
            {
                _shell.CurrentAspect = val;
                MenuBuilder.SetCurrentAspectRatio(val);
                MenuBuilder.RefreshAllRatioChecks(val);
                _shell.PlayerEngine?.SetAspectRatio(val);
            });

            MenuBuilder.SetRecToggleCallback((start) =>
            {
                _window.Dispatcher.Invoke(() =>
                {
                    var btn = _window.BtnRecordNow;
                    if (btn != null)
                    {
                        btn.IsChecked = start;
                        btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    }
                });
            });

            MenuBuilder.SetRecListCallback(() =>
            {
                _window.Dispatcher.Invoke(() =>
                {
                    _window.SetDrawerCollapsed(false);
                    _window.DrawerTabs.SelectedIndex = 3;
                });
            });

            MenuBuilder.SetRecSettingsCallback((tabIndex) =>
            {
                _window.Dispatcher.Invoke(() => OpenSettings(tabIndex));
            });

            MenuBuilder.SetSwitchSourceCallback(() =>
            {
                _shell.MenuActions.SwitchSourceCycle(true);
            });

            MenuBuilder.SetCurrentAspectRatio(_shell.CurrentAspect);
            MenuBuilder.SetCurrentSpeed(_shell.PlaybackSpeed);
            MenuBuilder.SetCurrentDecoder(AppSettings.Current.Decoder);
            MenuBuilder.SetDecoderCallback((decoder) =>
            {
                _window.Dispatcher.Invoke(() =>
                {
                    AppSettings.Current.Decoder = decoder;
                    AppSettings.Current.Save();
                    try { _window.ApplyDecoder(decoder); } catch { }
                });
            });
            return MenuBuilder.BuildMainMenu(
                openFile: () => _shell.MenuActions.OpenFile(),
                openUrl: () => _shell.MenuActions.OpenUrl(),
                addM3uFile: () => _shell.MenuActions.AddM3uFile(),
                addM3uUrl: () => _shell.MenuActions.AddM3uUrl(),
                editM3u: (s) => _shell.MenuActions.EditM3u(s),
                loadM3u: (s) => _shell.MenuActions.LoadM3u(s),
                openSettings: () => _window.Dispatcher.Invoke(() => OpenSettings()),
                showAbout: () => _shell.MenuActions.ShowAbout(),
                exitApp: () => _shell.MenuActions.ExitApp(),
                toggleFcc: (on) => _shell.MenuActions.ToggleFcc(on),
                toggleUdp: (on) => _shell.MenuActions.ToggleUdp(on),
                toggleEpg: (on) => _shell.MenuActions.ToggleEpg(on),
                toggleDrawer: (on) => _shell.MenuActions.ToggleDrawer(on),
                toggleMinimal: (on) => _shell.MenuActions.ToggleMinimalMode(on),
                toggleDeinterlace: (on) => _shell.MenuActions.ToggleDeinterlace(on),
                isEpgChecked: _window.CbEpg.IsChecked == true,
                isDrawerChecked: !_shell.IsDrawerCollapsed,
                isMinimalChecked: _shell.IsMinimalMode,
                refreshChannels: null,
                togglePlayPause: () => _shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.TogglePlayPause),
                stopPlayback: () => _shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.Stop),
                seekForward: () => _shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.SeekForward),
                seekBackward: () => _shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.SeekBackward),
                prevChannel: () => _shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.PreviousChannel),
                nextChannel: () => _shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.NextChannel),
                toggleMute: () => _shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.ToggleMute),
                volumeUp: null,
                volumeDown: null,
                toggleTopmost: (on) => { _window.Topmost = on; },
                openDebug: null,
                showShortcuts: () => _window.Dispatcher.Invoke(() => _window.ShowShortcuts()),
                isTopmostChecked: _window.Topmost,
                clearCloseMode: null
            );
        }

        private void OpenSettings(int tabIndex = 0)
        {
            // Delegate back to MainWindow or implement here?
            // OpenSettings logic is complex (dialog creation, callback handling).
            // Better to expose OpenSettings as internal/public in MainWindow or move it to a SettingsManager.
            // For now, let's assume MainWindow has OpenSettings() method that we can call.
            // Since OpenSettings is private in MainWindow, we might need to make it internal.
            // Or use reflection? No.
            // Let's assume we'll make MainWindow.OpenSettings internal.
            _window.OpenSettings(tabIndex);
        }

        public void VideoPanel_MouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                _window.Dispatcher.Invoke(() => 
                {
                    try
                    {
                        var cm = CreateAppMenu();
                        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                        cm.IsOpen = true;
                    }
                    catch { }
                });
            }
        }

        public void MainWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (IsInsideRecordingsArea(e.OriginalSource as DependencyObject))
                {
                    e.Handled = true;
                    return;
                }
                var cm = CreateAppMenu();
                cm.IsOpen = true;
                e.Handled = true;
            }
            catch
            {
                e.Handled = true;
            }
        }

        private bool IsInsideRecordingsArea(DependencyObject? d)
        {
            try
            {
                while (d != null)
                {
                    if (d is FrameworkElement fe)
                    {
                        var name = fe.Name ?? "";
                        if (string.Equals(name, "ListRecordings", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, "ListRecordingsGrouped", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, "TxtRecordingsSearch", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, "BtnRecordingsRefresh", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    d = VisualTreeHelper.GetParent(d);
                }
            }
            catch { }
            return false;
        }

        public void UpdateSpeedLabelWindow(double speed)
        {
            try
            {
                if (_window.LblSpeedWindow != null)
                {
                    var noXxList = new double[] { 0.75, 1.25, 1.75 };
                    var suffix = noXxList.Contains(speed) ? "" : "x";
                    _window.LblSpeedWindow.Text = $"{speed:0.##}{suffix}";
                }
            }
            catch { }
        }

        public void BtnSpeed_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menu = new ContextMenu();
                var speeds = _shell.PlaybackActions.GetSpeedOptions();
                foreach (var sp in speeds)
                {
                    var mi = new MenuItem();
                    mi.Header = sp.Label;
                    mi.IsCheckable = true;
                    mi.IsChecked = Math.Abs(_shell.PlaybackSpeed - sp.Speed) < 0.001;
                    mi.Click += (s, ev) =>
                    {
                        _shell.PlaybackSpeed = sp.Speed;
                        _shell.IsSpeedEnabled = true;
                        UpdateSpeedLabelWindow(sp.Speed);
                    };
                    menu.Items.Add(mi);
                }
                try
                {
                    var cmStyle = (Style)_window.FindResource(typeof(ContextMenu));
                    if (cmStyle != null) menu.Style = cmStyle;
                    var miStyle = (Style)_window.FindResource(typeof(MenuItem));
                    foreach (var obj in menu.Items)
                    {
                        if (obj is MenuItem mi && miStyle != null) mi.Style = miStyle;
                    }
                }
                catch { }
                menu.Closed += (s, ev) =>
                {
                    if (_window.BtnSpeed is System.Windows.Controls.Primitives.ToggleButton tb)
                        tb.IsChecked = false;
                };
                _window.BtnSpeed.ContextMenu = menu;
                _window.BtnSpeed.ContextMenu.PlacementTarget = _window.BtnSpeed;
                _window.BtnSpeed.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
                _window.BtnSpeed.ContextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                    new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new System.Windows.Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 30), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
                _window.BtnSpeed.ContextMenu.IsOpen = true;
            }
            catch { }
        }

        public void BtnSources_Click(object sender, RoutedEventArgs e)
        {
            OpenSourceMenuAtButton(_window.BtnSources);
            if (_window.BtnSources is System.Windows.Controls.Primitives.ToggleButton tb)
                tb.IsChecked = false;
        }

        public void BtnRatio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menu = new ContextMenu();
                foreach (var item in _shell.MenuActions.BuildRatioMenuItems())
                {
                    var mi = new MenuItem();
                    mi.Header = item.Header;
                    mi.IsCheckable = item.IsCheckable;
                    mi.IsChecked = item.IsChecked;
                    mi.Command = item.Command;
                    menu.Items.Add(mi);
                }
                menu.Closed += (s, ev) =>
                {
                    if (_window.BtnRatio is System.Windows.Controls.Primitives.ToggleButton tb)
                        tb.IsChecked = false;
                };
                _window.BtnRatio.ContextMenu = menu;
                _window.BtnRatio.ContextMenu.PlacementTarget = _window.BtnRatio;
                _window.BtnRatio.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
                _window.BtnRatio.ContextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                    new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new System.Windows.Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 30), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
                _window.BtnRatio.ContextMenu.IsOpen = true;
            }
            catch { }
        }

        public void OpenSourceMenuAtButton(System.Windows.Controls.Primitives.ToggleButton target)
        {
            try
            {
                var items = _shell.MenuActions.BuildSourceMenuItems();
                if (items.Count == 0) return;

                var menu = new ContextMenu();
                foreach (var item in items)
                {
                    var mi = new MenuItem();
                    mi.Header = item.Header;
                    mi.IsCheckable = item.IsCheckable;
                    mi.IsChecked = item.IsChecked;
                    mi.Command = item.Command;
                    menu.Items.Add(mi);
                }
                target.ContextMenu = menu;
                target.ContextMenu.PlacementTarget = target;
                target.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
                target.ContextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                    new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new System.Windows.Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 30), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
                target.ContextMenu.IsOpen = true;
            }
            catch { }
        }

        public void OpenSourceMenuAtOverlay()
        {
            try
            {
                if (_overlayManager.OverlayWpf == null) return;
                var items = _shell.MenuActions.BuildSourceMenuItems();
                if (items.Count == 0) return;

                var menu = new ContextMenu();
                foreach (var item in items)
                {
                    var mi = new MenuItem();
                    mi.Header = item.Header;
                    mi.IsCheckable = item.IsCheckable;
                    mi.IsChecked = item.IsChecked;
                    mi.Command = item.Command;
                    menu.Items.Add(mi);
                }
                _overlayManager.OverlayWpf.OpenSourceContextMenu(menu);
            }
            catch { }
        }
    }
}
