using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace LibmpvIptvClient
{
    /// <summary>
    /// Fullscreen floating control bar. The EPG timeline itself lives in <see cref="TimeshiftTimeline"/> so the
    /// windowed bottom bar can host the identical control; this window only owns the button row and forwards the
    /// timeline's public surface to the shell so existing subscribers keep working unchanged.
    /// </summary>
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
        public bool IsSourcesMenuOpen { get; private set; } = false;
        public bool IsRatioMenuOpen { get; private set; } = false;
        public bool IsAnyMenuOpen => IsSourcesMenuOpen || IsRatioMenuOpen;
        public string CurrentAspect { get; set; } = "default";
        public event Action<bool>? TimeshiftToggled;
        /// <summary>Raised when a programme segment is clicked; the host resolves the intent at click time.</summary>
        public event Action<TimelineProgramme>? ProgrammeSelected;
        /// <summary>Raised when a time on the ruler is clicked; the host turns it into a timeshift jump.</summary>
        public event Action<DateTime>? TimeSeekRequested;
        private bool _timeshiftOn = false;
        private bool _muted = false;

        // Height of the overlay window is derived from the hosted timeline and the measured button row, with an
        // explicit 8 px rhythm: timeline -> 8 px -> button row -> 8 px. Nothing here is a hard-coded bar height
        // any more, so the floating bar can never be taller than its content and the buttons never hug the edge.
        private const double ButtonRowHeight = 28;
        private const double BarGap = 8;
        private const double FallbackMeterHeight = 20;

        public OverlayControls()
        {
            InitializeComponent();
            InitializeSourceRatioIcons();
            PreviewKeyDown += OnPreviewKeyDown;
            try { CbTimeshift.Checked += (s, e) => TimeshiftToggled?.Invoke(true); } catch { }
            try { CbTimeshift.Unchecked += (s, e) => TimeshiftToggled?.Invoke(false); } catch { }

            // The timeline is the only place that knows the meter's real height, so the host window follows it.
            try
            {
                Timeline.SeekStart += () => SeekStart?.Invoke();
                Timeline.SeekEnd += v => SeekEnd?.Invoke(v);
                Timeline.ProgrammeSelected += programme => ProgrammeSelected?.Invoke(programme);
                Timeline.TimeSeekRequested += time => TimeSeekRequested?.Invoke(time);
                Timeline.VolumeChanged += v => VolumeChanged?.Invoke(v);
                Timeline.MuteChanged += m => { _muted = m; MuteChanged?.Invoke(m); };
                Timeline.LayoutChanged += ApplyBarHeight;

                // The standard bar is a separate control with its own 20 px row; it reports the same events.
                SeekBar.SeekStart += () => SeekStart?.Invoke();
                SeekBar.SeekEnd += v => SeekEnd?.Invoke(v);
                SeekBar.VolumeChanged += v => VolumeChanged?.Invoke(v);
                SeekBar.MuteChanged += m => { _muted = m; MuteChanged?.Invoke(m); };
                SeekBar.LayoutChanged += ApplyBarHeight;
                SeekBar.Visibility = Visibility.Collapsed;

                // The button row is measured once it has been laid out; re-fit when its natural height changes.
                ButtonRowHost.SizeChanged += (s, e) => ApplyBarHeight();
                Loaded += (s, e) => ApplyBarHeight();
            }
            catch { }

            ApplyBarHeight();
        }

        /// <summary>Resizes this floating window to the height the meter currently on screen wants.</summary>
        void ApplyBarHeight()
        {
            try
            {
                double required = ActiveMeter?.RequiredHeight ?? 0;
                if (required <= 0) required = FallbackMeterHeight;
                double row = ButtonRowHost?.ActualHeight ?? 0;
                if (row <= 0) row = ButtonRowHeight;
                // 8 px between the meter and the button row, 8 px between the button row and the bar's edge,
                // so the buttons sit centred in the gap on both sides.
                var wanted = required + BarGap + row + BarGap;
                if (Math.Abs(Height - wanted) > 0.5) Height = wanted;
            }
            catch { }
        }

        void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (ShortcutKeyPressed != null)
            {
                bool handled = ShortcutKeyPressed.Invoke(LibmpvIptvClient.Helpers.WpfKeyInput.NormalizeKey(e), e.KeyboardDevice.Modifiers);
                if (handled)
                {
                    e.Handled = true;
                }
            }
        }

        private void InitializeSourceRatioIcons()
        {
            try
            {
                double thickness = 1.0;

                // 切换源图标 - 圆环+两个对称箭头
                var sourcePath1 = new System.Windows.Shapes.Path
                {
                    StrokeThickness = thickness,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    Data = System.Windows.Media.Geometry.Parse("M 1.5,6 A 4.5,4.5 0 1,1 10.5,6 A 4.5,4.5 0 1,1 1.5,6")
                };
                var sourcePath2 = new System.Windows.Shapes.Path
                {
                    StrokeThickness = thickness,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    Data = System.Windows.Media.Geometry.Parse("M 6.7,4.2 A 0.5,0.5 0 0,1 7.4,4.2 L 9.1,5.9 A 0.5,0.5 0 0,1 8.9,6.8 L 3.9,6.8 A 0.5,0.5 0 0,1 3.9,5.8 L 6.1,5.0 A 0.5,0.5 0 0,1 6.7,4.2 Z")
                };
                var sourcePath3 = new System.Windows.Shapes.Path
                {
                    StrokeThickness = thickness,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    Data = System.Windows.Media.Geometry.Parse("M 5.3,7.8 A 0.5,0.5 0 0,1 4.6,7.8 L 2.9,6.1 A 0.5,0.5 0 0,1 3.1,5.2 L 8.1,5.2 A 0.5,0.5 0 0,1 8.1,6.2 L 5.9,7.0 A 0.5,0.5 0 0,1 5.3,7.8 Z")
                };
                var sourceCanvas = new System.Windows.Controls.Canvas { Width = 12, Height = 12 };
                sourceCanvas.Children.Add(sourcePath1);
                sourceCanvas.Children.Add(sourcePath2);
                sourceCanvas.Children.Add(sourcePath3);
                System.Windows.Controls.Viewbox sourceViewbox = new System.Windows.Controls.Viewbox { Width = 12, Height = 12, Stretch = System.Windows.Media.Stretch.Uniform };
                sourceViewbox.Child = sourceCanvas;
                BtnSourcesIcon.Content = sourceViewbox;

                // 画面比例图标 - 矩形套矩形
                var ratioPath1 = new System.Windows.Shapes.Path
                {
                    StrokeThickness = thickness,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    Data = System.Windows.Media.Geometry.Parse("M1,2 H11 V10 H1 Z")
                };
                var ratioPath2 = new System.Windows.Shapes.Path
                {
                    StrokeThickness = thickness,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    Data = System.Windows.Media.Geometry.Parse("M3,4 H9 V8 H3 Z")
                };
                var ratioCanvas = new System.Windows.Controls.Canvas { Width = 12, Height = 12 };
                ratioCanvas.Children.Add(ratioPath1);
                ratioCanvas.Children.Add(ratioPath2);
                System.Windows.Controls.Viewbox ratioViewbox = new System.Windows.Controls.Viewbox { Width = 12, Height = 12, Stretch = System.Windows.Media.Stretch.Uniform };
                ratioViewbox.Child = ratioCanvas;
                BtnRatioIcon.Content = ratioViewbox;

                // 延迟设置颜色，等待主题资源加载完成
                this.Loaded += (s, e) => UpdateIconBrushes();
            }
            catch { }
        }

        public void UpdateIconBrushes()
        {
            try
            {
                var brush = (System.Windows.Media.Brush)TryFindResource("TextPrimaryBrush") ?? System.Windows.Media.Brushes.White;
                var sourceCanvas = BtnSourcesIcon.Content as System.Windows.Controls.Viewbox;
                if (sourceCanvas?.Child is System.Windows.Controls.Canvas sourceCanvasChild)
                {
                    foreach (var child in sourceCanvasChild.Children)
                    {
                        if (child is System.Windows.Shapes.Path path)
                            path.Stroke = brush;
                    }
                }
                var ratioCanvas = BtnRatioIcon.Content as System.Windows.Controls.Viewbox;
                if (ratioCanvas?.Child is System.Windows.Controls.Canvas ratioCanvasChild)
                {
                    foreach (var child in ratioCanvasChild.Children)
                    {
                        if (child is System.Windows.Shapes.Path path)
                            path.Stroke = brush;
                    }
                }
            }
            catch { }
        }
        public void SetPlaySymbol(ModernWpf.Controls.Symbol symbol)
        {
            try
            {
                IconPlayPause.Data = symbol == ModernWpf.Controls.Symbol.Pause
                    ? System.Windows.Media.Geometry.Parse("M3,6 H6 V14 H3 Z M9,6 H12 V14 H9 Z")
                    : System.Windows.Media.Geometry.Parse("M4,2 L12,8 L4,14 Z");
            }
            catch { }
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;
            try { ActiveMeter.SetMuted(muted); } catch { }
        }
        public void SetTimeshift(bool on)
        {
            _timeshiftOn = on;
            try { CbTimeshift.IsChecked = on; } catch { }
            try { Timeline.SetTimeshift(on); } catch { }
        }
        public void SetTimeshiftRange(DateTime min, DateTime max)
        {
            try { Timeline.SetTimeshiftRange(min, max); } catch { }
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
            try { ActiveMeter.SetTime(current, duration); } catch { }
            if (!_timeshiftOn)
            {
                try
                {
                    LblElapsed.Text = FormatTime(current);
                    LblDuration.Text = FormatTime(duration);
                }
                catch { }
            }
        }

        // ---------------------------------------------------------------------
        // The two progress bars. The overlay hosts both and shows exactly one of them, so a change to either
        // control's own layout cannot move the other one.
        // ---------------------------------------------------------------------

        /// <summary>True while the EPG timeline owns the overlay; false while the standard bar does.</summary>
        private bool _showTimeline = true;

        /// <summary>True once a pushed snapshot carried a schedule the EPG band could draw.</summary>
        private bool _hasSchedule;

        /// <summary>Whichever bar is on screen right now.</summary>
        private ISeekMeter ActiveMeter => _showTimeline ? (ISeekMeter) Timeline : SeekBar;

        /// <summary>
        /// Chooses which of the two bars is visible. Both stay in the tree, so switching styles is instant and
        /// never rebuilds the hidden one's rows.
        /// </summary>
        void SetBarMode(bool showTimeline)
        {
            _showTimeline = showTimeline;
            try
            {
                Timeline.Visibility = showTimeline ? Visibility.Visible : Visibility.Collapsed;
                SeekBar.Visibility = showTimeline ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { }
            ApplyBarHeight();
        }

        /// <summary>
        /// Applies the configured seek-bar style. A channel without an EPG schedule keeps the standard bar even
        /// when the EPG timeline style is selected.
        /// </summary>
        public void ApplySeekBarStyle()
        {
            SetBarMode(WantsTimeline);
        }

        /// <summary>
        /// True when both inputs the bar mode depends on agree: the configured style asks for the EPG timeline and
        /// the pushed snapshot carried a schedule for it to draw.
        /// </summary>
        bool WantsTimeline => _hasSchedule && AppSettings.Current.SeekBar != SeekBarStyle.Standard;

        /// <summary>The hosted EPG timeline; the windowed bar's peer of this window talks to the same type.</summary>
        public TimeshiftTimeline TimelineControl => Timeline;

        /// <summary>Volume UI of whichever bar is visible (the wheel and the mute tooltip drive it).</summary>
        public LibmpvIptvClient.Controls.VolumeSlider VolumeSlider => ActiveMeter.VolumeSlider;

        /// <summary>Mute button of whichever bar is visible.</summary>
        public System.Windows.Controls.Button MuteButton => ActiveMeter.MuteButton;

        /// <summary>
        /// Applies a full EPG timeline snapshot. Callers push this every playback tick; the strip itself is only
        /// rebuilt when <see cref="OverlayTimelineState.Signature"/> changes so it does not flicker.
        /// </summary>
        public void SetTimelineState(OverlayTimelineState? state)
        {
            _hasSchedule = state != null && state.Programmes != null && state.Programmes.Count > 0;

            // The band has to know the schedule before the host measures the bar it is about to show.
            try { Timeline.SetTimelineState(state); } catch { }

            // Re-evaluate the mode on EVERY snapshot, in both directions. This window is created before the first
            // schedule exists (no channel has been played yet), so it starts on the standard bar; promoting here is
            // what brings the EPG timeline back the moment the schedule lands. Without it the overlay could only
            // ever be demoted to the standard bar, and the only remaining promoter was applying the settings, which
            // made the style look theme dependent (saving a theme change was the one action that fixed it).
            if (WantsTimeline != _showTimeline) SetBarMode(WantsTimeline);

            ApplyBarHeight();
        }

        public void SetTimeshiftSchedule(IReadOnlyList<TimelineProgramme> programmes, DateTime cursor)
        {
            try { Timeline.SetTimeshiftSchedule(programmes, cursor); } catch { }
            ApplyBarHeight();
        }

        public void SetVolume(double val)
        {
            try { ActiveMeter.SetVolume(val); } catch { }
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

        public void SetFcc(bool v) { /* Removed */ }
        public void SetUdp(bool v) { LibmpvIptvClient.Diagnostics.Logger.Debug($"[Overlay] UDP optimization toggled: {v}"); }
        
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
        private List<System.Windows.Controls.MenuItem>? _bottomBarRatioItems;
        public void RefreshRatioMenu()
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
            var items = new List<System.Windows.Controls.MenuItem>();
            foreach (var (label, val) in options)
            {
                var mi = new MenuItem();
                mi.Header = label;
                mi.Tag = val;
                mi.IsCheckable = true;
                mi.IsChecked = string.Equals(CurrentAspect, val, StringComparison.OrdinalIgnoreCase);
                mi.Click += (s, ev) => AspectRatioChanged?.Invoke(val);
                menu.Items.Add(mi);
                items.Add(mi);
            }
            menu.Opened += (s, ev) => IsRatioMenuOpen = true;
            menu.Closed += (s, ev) => IsRatioMenuOpen = false;
            BtnRatio.ContextMenu = menu;
            BtnRatio.ContextMenu.PlacementTarget = BtnRatio;
            BtnRatio.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            BtnRatio.ContextMenu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new System.Windows.Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 30), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
            try
            {
                if (_bottomBarRatioItems != null)
                    LibmpvIptvClient.Helpers.MenuBuilder.RemoveRatioMenuItems(_bottomBarRatioItems);
                LibmpvIptvClient.Helpers.MenuBuilder.AddRatioMenuItems(items);
                _bottomBarRatioItems = items;
            }
            catch { }
        }

        public void UpdateRatioMenuItems()
        {
            try
            {
                if (BtnRatio.ContextMenu == null) return;
                var options = new[] { "default", "16:9", "4:3", "stretch", "fill", "crop" };
                int i = 0;
                foreach (var item in BtnRatio.ContextMenu.Items)
                {
                    if (item is System.Windows.Controls.MenuItem mi && i < options.Length)
                    {
                        mi.IsChecked = string.Equals(options[i], CurrentAspect, StringComparison.OrdinalIgnoreCase);
                    }
                    i++;
                }
            }
            catch { }
        }
        void BtnRatio_Click(object sender, RoutedEventArgs e)
        {
            RefreshRatioMenu();
            BtnRatio.ContextMenu.IsOpen = true;
        }
        public void SetInfo(string text) { LblInfo.Text = text; }
        void BtnSourceMenu_Click(object sender, RoutedEventArgs e) => SourceMenuRequested?.Invoke();
        public event Action<bool>? MuteChanged;
        public event Action? PreviewRequested;
        public event Action<double>? SpeedSelected;
        public event Func<System.Windows.Input.Key, System.Windows.Input.ModifierKeys, bool>? ShortcutKeyPressed;
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
        private List<System.Windows.Controls.MenuItem>? _bottomBarSpeedItems;
        void BtnSpeed_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            double[] speeds = new double[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 3.0, 5.0 };
            var items = new List<MenuItem>();
            foreach (var sp in speeds)
            {
                var mi = new MenuItem();
                mi.Header = $"{sp:0.##}x";
                mi.Tag = sp;
                mi.IsCheckable = true;
                try { mi.IsChecked = LblSpeed.Text == $"{sp:0.##}x"; } catch { }
                mi.Click += (s, ev) =>
                {
                    SetSpeed(sp);
                    SpeedSelected?.Invoke(sp);
                };
                menu.Items.Add(mi);
                items.Add(mi);
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
            try
            {
                if (_bottomBarSpeedItems != null)
                    LibmpvIptvClient.Helpers.MenuBuilder.RemoveSpeedMenuItems(_bottomBarSpeedItems);
                LibmpvIptvClient.Helpers.MenuBuilder.AddSpeedMenuItems(items);
                _bottomBarSpeedItems = items;
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
                    var tb = new TextBlock { Text = t, Foreground = brush, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
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
            // Wheel over the EPG programme strip scrolls it horizontally; anywhere else it keeps controlling volume.
            if (_showTimeline && Timeline.HandleWheel(e)) return;

            int step = 5;
            int dir = e.Delta > 0 ? 1 : -1;
            // 静音时滚动，取消静音
            if (_muted)
            {
                _muted = false;
                ActiveMeter.SetMuted(false);
                MuteChanged?.Invoke(_muted);
            }
            var v = Math.Max(0, Math.Min(100, VolumeSlider.Volume + dir * step));
            VolumeSlider.Volume = v;
            // 显示百分比提示
            ShowVolumeTooltip(v);
            e.Handled = true;
        }

        System.Windows.Threading.DispatcherTimer? _volumeTooltipTimer;
        public void ShowVolumeTooltip(double value)
        {
            // 通过 ToolTip 显示百分比
            MuteButton.ToolTip = $"音量: {(int)value}%";
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
                MuteButton.ToolTip = "静音";
            };
            _volumeTooltipTimer.Start();
        }
    }
}
