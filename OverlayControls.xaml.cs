using System;
using System.Windows;
using System.Windows.Controls;

namespace LibmpvIptvClient
{
    public partial class OverlayControls : Window
    {
        public event Action? PlayPause;
        public event Action? Stop;
        public event Action? Rew;
        public event Action? Fwd;
        public event Action<double>? SeekEnd;
        public event Action? SeekStart;
        public event Action<double>? VolumeChanged;
        // public event Action<bool>? FccChanged; // Removed
        // public event Action<bool>? UdpChanged; // Removed
        public event Action? SourceMenuRequested;
        public event Action<bool>? DrawerToggled;
        public event Action<bool>? EpgToggled;
        public event Action<string>? AspectRatioChanged;
        private bool _seeking = false;
        public bool IsSourcesMenuOpen { get; private set; } = false;
        public bool IsRatioMenuOpen { get; private set; } = false;
        public bool IsAnyMenuOpen => IsSourcesMenuOpen || IsRatioMenuOpen;
        public string CurrentAspect { get; set; } = "default";
        public event Action<bool>? TimeshiftToggled;
        private bool _timeshiftOn = false;
        private DateTime _tsMin;
        private DateTime _tsMax;
        public OverlayControls()
        {
            InitializeComponent();
            try { CbTimeshift.Checked += (s, e) => TimeshiftToggled?.Invoke(true); } catch { }
            try { CbTimeshift.Unchecked += (s, e) => TimeshiftToggled?.Invoke(false); } catch { }
        }
        public void SetPlaySymbol(ModernWpf.Controls.Symbol symbol)
        {
            try { IconPlayPause.Symbol = symbol; } catch { }
        }
        public void SetMuted(bool muted)
        {
            _muted = muted;
            try
            {
                IconMute.Symbol = _muted ? ModernWpf.Controls.Symbol.Mute : ModernWpf.Controls.Symbol.Volume;
                BtnMute.Background = _muted
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0x45, 0x3A))
                    : null;
            }
            catch { }
        }
        public void SetTimeshift(bool on)
        {
            _timeshiftOn = on;
            try { CbTimeshift.IsChecked = on; } catch { }
        }
        public void SetTimeshiftRange(DateTime min, DateTime max)
        {
            _tsMin = min;
            _tsMax = max;
        }
        public void SetTimeshiftLabels(DateTime cursor, DateTime now, bool seeking)
        {
            try
            {
                LblDuration.Text = now.ToString("yyyy-MM-dd HH:mm:ss");
                if (!seeking) LblElapsed.Text = cursor.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { }
        }
        public void SetTime(double current, double duration)
        {
            SliderSeek.Maximum = duration <= 0 ? 1 : duration;
            if (!_seeking)
            {
                SliderSeek.Value = Math.Max(0, Math.Min(SliderSeek.Maximum, current));
            }
            if (!_timeshiftOn)
            {
                LblElapsed.Text = FormatTime(current);
                LblDuration.Text = FormatTime(duration);
            }
        }
        public void SetVolume(double val)
        {
            SliderVolume.Volume = val;
        }
        string FormatTime(double sec)
        {
            if (sec < 0) sec = 0;
            var ts = TimeSpan.FromSeconds(sec);
            return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }
        void BtnPlayPause_Click(object sender, RoutedEventArgs e) => PlayPause?.Invoke();
        void BtnStop_Click(object sender, RoutedEventArgs e) => Stop?.Invoke();
        void BtnRew_Click(object sender, RoutedEventArgs e) => Rew?.Invoke();
        void BtnFwd_Click(object sender, RoutedEventArgs e) => Fwd?.Invoke();
        void SliderSeek_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var p = e.GetPosition(SliderSeek);
                var ratio = Math.Max(0, Math.Min(1, SliderSeek.ActualWidth > 0 ? p.X / SliderSeek.ActualWidth : 0));
                var sec = ratio * (SliderSeek.Maximum - SliderSeek.Minimum) + SliderSeek.Minimum;
                _seeking = true;
                SliderSeek.Value = sec;
                SeekStart?.Invoke();
            }
            catch
            {
                _seeking = true;
                SeekStart?.Invoke();
            }
        }
        void SliderSeek_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _seeking = false;
            SeekEnd?.Invoke(SliderSeek.Value);
        }
        void SliderSeek_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                var p = e.GetPosition(SliderSeek);
                var ratio = Math.Max(0, Math.Min(1, SliderSeek.ActualWidth > 0 ? p.X / SliderSeek.ActualWidth : 0));
                var sec = ratio * (SliderSeek.Maximum - SliderSeek.Minimum) + SliderSeek.Minimum;
                if (_timeshiftOn)
                {
                    var t = _tsMin.AddSeconds(Math.Max(0, sec));
                    SliderSeek.ToolTip = t.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else
                {
                    SliderSeek.ToolTip = FormatTime(sec);
                }
            }
            catch { }
        }
        void SliderSeek_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try { SliderSeek.ClearValue(ToolTipProperty); } catch { }
        }
        void SliderSeek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_seeking)
            {
                if (_timeshiftOn)
                {
                    var t = _tsMin.AddSeconds(Math.Max(0, SliderSeek.Value));
                    LblElapsed.Text = t.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else
                {
                    LblElapsed.Text = FormatTime(SliderSeek.Value);
                }
            }
        }
        void SliderVolume_VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            VolumeChanged?.Invoke(SliderVolume.Volume);
        }
        bool _muted = false;
        void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            _muted = !_muted;
            IconMute.Symbol = _muted ? ModernWpf.Controls.Symbol.Mute : ModernWpf.Controls.Symbol.Volume;
            // 静音时按钮背景变为浅红色
            BtnMute.Background = _muted
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0x45, 0x3A))
                : null;
            VolumeChanged?.Invoke(SliderVolume.Volume);
            MuteChanged?.Invoke(_muted);
        }
        public void SetFcc(bool v) { /* Removed */ }
        public void SetUdp(bool v) { /* Removed */ }
        
        // 重构：统一使用 Visible 逻辑
        // visible = true -> IsChecked = true (高亮)
        // visible = false -> IsChecked = false (灰色)
        public void SetDrawerVisible(bool visible) 
        { 
            CbDrawer.IsChecked = visible; 
        }
        
        public void SetEpgVisible(bool visible) { CbEpg.IsChecked = visible; }
        // void CbFcc_Checked(object sender, RoutedEventArgs e) => FccChanged?.Invoke(CbFcc.IsChecked == true); // Removed
        // void CbUdp_Checked(object sender, RoutedEventArgs e) => UdpChanged?.Invoke(CbUdp.IsChecked == true); // Removed
        
        // 点击事件：
        // IsChecked = true -> visible = true
        // IsChecked = false -> visible = false
        // DrawerToggled 参数含义改为 visible
        void CbDrawer_Checked(object sender, RoutedEventArgs e) 
        {
             DrawerToggled?.Invoke(CbDrawer.IsChecked == true);
        }
        void CbEpg_Checked(object sender, RoutedEventArgs e) => EpgToggled?.Invoke(CbEpg.IsChecked == true);
        void BtnRatio_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            var options = new[] { 
                ("默认", "default"),
                ("16:9", "16:9"),
                ("4:3", "4:3"),
                ("拉伸", "stretch"),
                ("填充", "fill"),
                ("裁剪", "crop")
            };
            foreach (var (label, val) in options)
            {
                var mi = new MenuItem();
                mi.Header = label;
                mi.IsCheckable = true;
                mi.IsChecked = string.Equals(CurrentAspect, val, StringComparison.OrdinalIgnoreCase);
                mi.Click += (s, ev) => AspectRatioChanged?.Invoke(val);
                menu.Items.Add(mi);
            }
            menu.Opened += (s, ev) => IsRatioMenuOpen = true;
            menu.Closed += (s, ev) => IsRatioMenuOpen = false;
            BtnRatio.ContextMenu = menu;
            BtnRatio.ContextMenu.PlacementTarget = BtnRatio;
            BtnRatio.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            BtnRatio.ContextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new System.Windows.Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 30), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
            BtnRatio.ContextMenu.IsOpen = true;
        }
        public void SetInfo(string text) { LblInfo.Text = text; }
        void BtnSourceMenu_Click(object sender, RoutedEventArgs e) => SourceMenuRequested?.Invoke();
        public event Action<bool>? MuteChanged;
        public event Action? PreviewRequested;
        public event Action<double>? SpeedSelected;
        public void OpenSourceContextMenu(ContextMenu menu)
        {
            try
            {
                var cmStyle = (Style)FindResource(typeof(ContextMenu));
                if (cmStyle != null) menu.Style = cmStyle;
                var miStyle = (Style)FindResource(typeof(MenuItem));
                foreach (var obj in menu.Items)
                {
                    if (obj is MenuItem mi && miStyle != null) mi.Style = miStyle;
                }
            }
            catch { }
            menu.Opened += (s, ev) => IsSourcesMenuOpen = true;
            menu.Closed += (s, ev) => IsSourcesMenuOpen = false;
            BtnSources.ContextMenu = menu;
            BtnSources.ContextMenu.PlacementTarget = BtnSources;
            BtnSources.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            BtnSources.ContextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new System.Windows.Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 30), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
            BtnSources.ContextMenu.IsOpen = true;
        }
        void BtnPreview_Click(object sender, RoutedEventArgs e) => PreviewRequested?.Invoke();
        public void SetSpeed(double v)
        {
            try { LblSpeed.Text = $"{v:0.##}x"; } catch { }
        }
        public void SetSpeedEnabled(bool enabled)
        {
            try { BtnSpeed.IsEnabled = enabled; } catch { }
        }
        void BtnSpeed_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            double[] speeds = new double[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 3.0, 5.0 };
            foreach (var sp in speeds)
            {
                var mi = new MenuItem();
                mi.Header = $"{sp:0.##}x";
                mi.IsCheckable = true;
                try { mi.IsChecked = LblSpeed.Text == $"{sp:0.##}x"; } catch { }
                mi.Click += (s, ev) =>
                {
                    SetSpeed(sp);
                    SpeedSelected?.Invoke(sp);
                };
                menu.Items.Add(mi);
            }
            try
            {
                var cmStyle = (Style)FindResource(typeof(ContextMenu));
                if (cmStyle != null) menu.Style = cmStyle;
                var miStyle = (Style)FindResource(typeof(MenuItem));
                foreach (var obj in menu.Items)
                {
                    if (obj is MenuItem mi && miStyle != null) mi.Style = miStyle;
                }
            }
            catch { }
            BtnSpeed.ContextMenu = menu;
            BtnSpeed.ContextMenu.PlacementTarget = BtnSpeed;
            BtnSpeed.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            BtnSpeed.ContextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new System.Windows.Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 30), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
            BtnSpeed.ContextMenu.IsOpen = true;
        }
        public void SetTags(System.Collections.Generic.List<string> tags)
        {
            try
            {
                TagPanelOverlay.Children.Clear();
                var style = TryFindResource("OverlayTagChip") as System.Windows.Style;
                var brush = TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
                foreach (var t in tags)
                {
                    var b = new System.Windows.Controls.Border { Style = style };
                    var tb = new TextBlock { Text = t, Foreground = brush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    b.Child = tb;
                    TagPanelOverlay.Children.Add(b);
                }
            }
            catch { }
        }
        
        public void SetPlaybackStatus(string text, System.Windows.Media.Brush foreground)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    StatusIndicatorInner.Visibility = Visibility.Collapsed;
                }
                else
                {
                    StatusIndicatorInner.Visibility = Visibility.Visible;
                    TxtStatusInner.Text = text;
                    TxtStatusInner.Foreground = foreground;
                }
            }
            catch { }
        }
        void OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            int step = 5;
            int dir = e.Delta > 0 ? 1 : -1;
            // 静音时滚动，取消静音
            if (_muted)
            {
                _muted = false;
                IconMute.Symbol = ModernWpf.Controls.Symbol.Volume;
                BtnMute.Background = null;
                MuteChanged?.Invoke(_muted);
            }
            var v = Math.Max(0, Math.Min(100, SliderVolume.Volume + dir * step));
            SliderVolume.Volume = v;
            // 显示百分比提示
            ShowVolumeTooltip(v);
            e.Handled = true;
        }

        System.Windows.Threading.DispatcherTimer? _volumeTooltipTimer;
        public void ShowVolumeTooltip(double value)
        {
            // 通过 ToolTip 显示百分比
            BtnMute.ToolTip = $"音量: {(int)value}%";
            // 自动隐藏
            if (_volumeTooltipTimer != null)
            {
                _volumeTooltipTimer.Stop();
                _volumeTooltipTimer = null;
            }
            _volumeTooltipTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _volumeTooltipTimer.Tick += (s, e2) =>
            {
                _volumeTooltipTimer?.Stop();
                BtnMute.ToolTip = "静音";
            };
            _volumeTooltipTimer.Start();
        }
    }
}
