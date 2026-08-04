using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Windows.Media;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

namespace LibmpvIptvClient
{
    public partial class TopOverlay : Window
    {
        public event Action? Minimize;
        public event Action? MaximizeRestore; // Toggles FS or Maximize
        public event Action? CloseWindow;
        public event Action? FullscreenToggle;
        public event Action<bool>? TopmostChanged;
        public event Action? OpenFile;
        public event Action? OpenUrl;
        public event Action? OpenSettings;
        public event Action? AddM3u;
        public event Action? AddM3uFile;
        public event Action? ExitApp;
        public event Action<M3uSource>? LoadM3u;
        public event Action<M3uSource>? EditM3u;
        public event Action<bool>? FccChanged;
        public event Action<bool>? UdpChanged;
        public event Action<bool>? EpgToggled;
        public event Action<bool>? DrawerToggled;
        public event Action<bool>? MinimalModeChanged;
        public event Action<bool>? DeinterlaceChanged;
        public Func<bool>? IsUdpEnabled;
        public Func<bool>? IsTopmost;
        public Func<bool>? IsEpgVisible;
        public Func<bool>? IsDrawerVisible;
        public Func<bool>? IsMinimalMode;
        public event Func<System.Windows.Input.Key, bool>? ShortcutKeyPressed;
        private readonly MainWindowTopOverlayMenuViewModel _menuViewModel = new();

        public bool IsMenuOpen => BtnTitle.ContextMenu?.IsOpen == true;

        public TopOverlay()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (s, e) =>
            {
                if (IsTopmost != null && BtnPin != null) BtnPin.IsChecked = IsTopmost();
                var minimalOn = IsMinimalMode?.Invoke() ?? false;
                SyncWindowVisual(false, WindowState.Normal, minimalOn);
            };
        }

        void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (ShortcutKeyPressed != null)
            {
                bool handled = ShortcutKeyPressed.Invoke(e.Key);
                if (handled)
                {
                    e.Handled = true;
                }
            }
        }

        public void SetCloseVisible(bool visible)
        {
            try { if (BtnClose != null) BtnClose.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; } catch { }
        }

        public void SyncWindowVisual(bool isFullscreenOwner, WindowState ownerState, bool minimalOn)
        {
            try
            {
                if (BtnMinimal != null) BtnMinimal.IsChecked = minimalOn;
            }
            catch { }
            try
            {
                if (PathMaxIcon != null)
                {
                    var icon = isFullscreenOwner || ownerState == WindowState.Maximized
                        ? "M4.5,6.5 H9.5 V11.5 H4.5 Z M6.5,6.5 V4.5 H11.5 V9.5 H9.5"
                        : "M4.5,4.5 H11.5 V11.5 H4.5 Z";
                    PathMaxIcon.Data = Geometry.Parse(icon);
                }
            }
            catch { }
            try
            {
                if (PathFullscreenIcon != null)
                {
                    var icon = isFullscreenOwner
                        ? "M4.5,4.5 H7.5 V3.5 H3.5 V7.5 H4.5 M11.5,4.5 H8.5 V3.5 H12.5 V7.5 H11.5 M11.5,11.5 H8.5 V12.5 H12.5 V8.5 H11.5 M4.5,11.5 H7.5 V12.5 H3.5 V8.5 H4.5"
                        : "M3.5,6.5 V3.5 H6.5 M9.5,3.5 H12.5 V6.5 M12.5,9.5 V12.5 H9.5 M6.5,12.5 H3.5 V9.5";
                    PathFullscreenIcon.Data = Geometry.Parse(icon);
                }
            }
            catch { }
            try
            {
                if (PathMinimalIcon != null)
                {
                    PathMinimalIcon.Data = Geometry.Parse("M4,6 H12 M4,9 H12 M4,12 H12");
                }
            }
            catch { }
        }

        private void BtnMin_Click(object sender, RoutedEventArgs e) => Minimize?.Invoke();
        private void BtnMax_Click(object sender, RoutedEventArgs e) => MaximizeRestore?.Invoke();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => CloseWindow?.Invoke();
        private void BtnFullscreen_Click(object sender, RoutedEventArgs e) => FullscreenToggle?.Invoke();
        private void BtnPin_Click(object sender, RoutedEventArgs e) => TopmostChanged?.Invoke(BtnPin.IsChecked == true);
        private void BtnMinimal_Click(object sender, RoutedEventArgs e) => MinimalModeChanged?.Invoke(BtnMinimal.IsChecked == true);

        private void BtnTitle_Click(object sender, RoutedEventArgs e)
        {
            var cm = _menuViewModel.BuildMenu(
                openFile: OpenFile,
                openUrl: OpenUrl,
                addM3uFile: AddM3uFile,
                addM3uUrl: AddM3u,
                editM3u: EditM3u,
                loadM3u: LoadM3u,
                openSettings: OpenSettings,
                showAbout: null,
                exitApp: ExitApp,
                toggleFcc: (on) =>
                {
                    AppSettings.Current.FccPrefetchCount = on ? 2 : 0;
                    AppSettings.Current.Save();
                    FccChanged?.Invoke(on);
                },
                toggleUdp: (on) =>
                {
                    AppSettings.Current.EnableUdpOptimization = on;
                    AppSettings.Current.Save();
                    UdpChanged?.Invoke(on);
                },
                toggleEpg: (on) => EpgToggled?.Invoke(on),
                toggleDrawer: (on) => DrawerToggled?.Invoke(on),
                toggleMinimal: (on) => MinimalModeChanged?.Invoke(on),
                toggleDeinterlace: (on) => DeinterlaceChanged?.Invoke(on),
                isEpgChecked: IsEpgVisible?.Invoke() ?? false,
                isDrawerChecked: IsDrawerVisible?.Invoke() ?? false,
                isMinimalChecked: IsMinimalMode?.Invoke() ?? false,
                refreshChannels: null,
                togglePlayPause: null,
                stopPlayback: null,
                seekForward: null,
                seekBackward: null,
                prevChannel: null,
                nextChannel: null,
                toggleMute: null,
                volumeUp: null,
                volumeDown: null,
                toggleTopmost: null,
                openDebug: null,
                showShortcuts: null,
                isTopmostChecked: false);

            BtnTitle.ContextMenu = cm;
            BtnTitle.ContextMenu.PlacementTarget = BtnTitle;
            BtnTitle.ContextMenu.IsOpen = true;
        }

        public void SetTitle(string title)
        {
            // BtnTitle.Content = title; // Removed to keep the icon+text layout
        }
    }
}
