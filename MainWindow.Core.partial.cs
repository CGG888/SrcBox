using System;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using LibmpvIptvClient.Architecture.Platform.Player;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient
{
    public partial class MainWindow : Window
    {
        private MpvInterop? _mpv;
        private EpgService? _epgService;
        private UserDataStore _userDataStore = new UserDataStore();
        private HttpClient _http => HttpClientService.Instance.Client;

        private DebugWindow? _debug;
        private System.Windows.Threading.DispatcherTimer _timer = new System.Windows.Threading.DispatcherTimer();
        private System.Windows.Threading.DispatcherTimer _epgTimer = new System.Windows.Threading.DispatcherTimer();

        internal readonly LibmpvIptvClient.Architecture.Presentation.View.MainWindowOverlayManager _overlayManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowRecordingManager? _recordingManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowMenuManager? _menuManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowEpgManager? _epgManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowSettingsManager? _settingsManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowStartupManager? _startupManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowHistoryManager? _historyManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowShellSyncManager? _shellSyncManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowPlaybackTickManager? _playbackTickManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowSeekInteractionManager? _seekInteractionManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowFullscreenInputManager? _fullscreenInputManager;
        private LibmpvIptvClient.Architecture.Presentation.View.MainWindowWindowedInputManager? _windowedInputManager;
        private MainWindowChannelContextMenuManager? _channelContextMenuManager;

        private bool _firstFrameLogged = false;
        private DateTime _lastHistoryUpdate = DateTime.MinValue;
        private readonly MainShellViewModel _shell = new();
        private readonly Action _epgRemindersChangedHandler;
        private DateTime _playStartTime;
        private double _baseWindowWidth = 1280;
        // Last window width applied for the current panel layout; the resize handler uses it to tell
        // our own panel-driven resizes apart from a real user resize.
        private double _panelAppliedWidth = double.NaN;
        // True once a remembered window size was applied, so the panel restore knows whether the
        // base width comes from the settings or from the designed window size.
        private bool _geometryRestored;

        public MainWindow()
        {
            InitializeComponent();
            InitializeSourceRatioIcons();
            RestoreWindowGeometry();
            SizeChanged += OnWindowSizeChanged;
            this.Loaded += (s, e) => UpdateIconBrushes();
            CbM3uList.ItemsSource = AppSettings.Current.SavedSources;
            //CbM3uListGroups.ItemsSource = AppSettings.Current.SavedSources;
            SyncM3uComboBoxSelection();
            CbM3uList.Loaded += (s, e) => {
                if (CbM3uList.Template.FindName("PART_EditableTextBox", CbM3uList) is System.Windows.Controls.TextBox tb) {
                    tb.FontSize = 10;
                }
            };
            DataContext = _shell;
            _overlayManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowOverlayManager(this, _shell);
            _menuManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowMenuManager(this, _shell, _overlayManager);
            _settingsManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowSettingsManager(this, _shell, _overlayManager);
            _startupManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowStartupManager(this, _shell);
            _historyManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowHistoryManager(this, _shell, _userDataStore);
            _shellSyncManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowShellSyncManager(this, _shell, _overlayManager);
            _playbackTickManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowPlaybackTickManager(this, _shell, _overlayManager);
            _seekInteractionManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowSeekInteractionManager(this, _shell);
            WireWindowedTimeline();
            _fullscreenInputManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowFullscreenInputManager(this);
            _windowedInputManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowWindowedInputManager(this);
            _channelContextMenuManager = new MainWindowChannelContextMenuManager(_shell);
            _shell.PropertyChanged += OnShellPropertyChanged;
            _shell.RequestClose += () => Close();
            _shell.EpgLoaded += OnEpgLoaded;
            Loaded += OnLoaded;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;
            _epgRemindersChangedHandler = () => { try { _epgManager?.SyncEpgReminderList(); } catch { } };
            try { ReminderListWindow.RemindersChanged += _epgRemindersChangedHandler; } catch { }
            try
            {
                if (ListHistory != null)
                {
                    ListHistory.CommandBindings.Add(new CommandBinding(System.Windows.Input.ApplicationCommands.Delete, HistoryDeleteOne_Executed, HistoryDeleteOne_CanExecute));
                }
            }
            catch { }
            try { VideoPanel.DoubleClick += MainPanel_DoubleClick; } catch { }
            try { VideoPanel.MouseWheel += FsPanel_MouseWheel; } catch { }
            try { VideoPanel.MouseClick += VideoPanel_MouseClick; } catch { }
            try { VideoPanel.MouseDown += VideoPanel_MouseDown; } catch { }
            try { VideoPanel.MouseMove += VideoPanel_MouseMoveForMinimal; } catch { }
            MouseRightButtonUp += MainWindow_MouseRightButtonUp;
            App.LanguageChanged += OnLanguageChanged;
            App.ThemeChanged += OnThemeChanged;
            try
            {
                LibmpvIptvClient.Services.UploadQueueService.OnUploaded += (remoteDir) =>
                {
                    try
                    {
                        var key = "unknown";
                        try
                        {
                            var parts = (remoteDir ?? "").Trim('/').Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                            key = parts.Length > 0 ? parts.Last() : "unknown";
                        }
                        catch { }
                        Dispatcher.BeginInvoke(new Action(() => { try { _recordingManager?.ScheduleRecordingsRefresh(key); } catch { } }));
                    }
                    catch { }
                };
            }
            catch { }
        }

        private void SyncM3uComboBoxSelection()
        {
            try
            {
                var sources = AppSettings.Current.SavedSources;
                if (sources == null || sources.Count == 0)
                {
                    CbM3uList.SelectedIndex = -1;
                    CbM3uList.Text = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_M3uList", "选择M3U列表");
                    //CbM3uListGroups.SelectedIndex = -1;
                    //CbM3uListGroups.Text = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_M3uList", "选择M3U列表");
                    return;
                }
                var selected = sources.FirstOrDefault(s => s.IsSelected);
                CbM3uList.SelectedItem = selected;
                //CbM3uListGroups.SelectedItem = selected;
            }
            catch { }
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
            }
            catch { }
        }

        private void UpdateIconBrushes()
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

        /// <summary>
        /// Paints the mute state on both windowed bars. The icon used to live in the inline windowed bottom bar;
        /// it now belongs to the shared controls, so this forwards to them in turn (the fullscreen overlay's copy
        /// is painted through <c>OverlayControls.SetMuted</c>).
        /// </summary>
        public void UpdateMuteIcon(bool isMuted)
        {
            try { TimelineWindowed?.SetMuted(isMuted); } catch { }
            try { SeekBarWindowed?.SetMuted(isMuted); } catch { }
        }

        /// <summary>
        /// Hooks the two windowed bottom-bar controls up to the shell: the EPG <see cref="TimeshiftTimeline"/> and
        /// the plain <see cref="StandardSeekBar"/>. Only one of them is on screen at a time (see
        /// <see cref="SetWindowedBarMode"/>), so the two styles share no layout and fixing one cannot move the
        /// other.
        /// </summary>
        private void WireWindowedTimeline()
        {
            try
            {
                if (TimelineWindowed == null) return;
                TimelineWindowed.SeekStart += TimelineWindowed_SeekStart;
                TimelineWindowed.SeekEnd += TimelineWindowed_SeekEnd;
                TimelineWindowed.ProgrammeSelected += TimelineWindowed_ProgrammeSelected;
                TimelineWindowed.TimeSeekRequested += TimelineWindowed_TimeSeekRequested;
                TimelineWindowed.VolumeChanged += (val) => { try { _shell.Volume = val; } catch { } };
                TimelineWindowed.MuteChanged += (on) => { try { _shell.IsMuted = on; } catch { } };
                TimelineWindowed.LayoutChanged += UpdateWindowedBarHeight;
                // The windowed EPG timeline is only ever shown when a schedule exists, so it always owns its band;
                // the plain 20 px meter belongs to the separate StandardSeekBar control.
                TimelineWindowed.SetCompactMode(false);
                TimelineWindowed.SetMuted(_shell.IsMuted);
                TimelineWindowed.SetVolume(_shell.Volume);

                if (SeekBarWindowed != null)
                {
                    SeekBarWindowed.SeekStart += TimelineWindowed_SeekStart;
                    SeekBarWindowed.SeekEnd += TimelineWindowed_SeekEnd;
                    SeekBarWindowed.VolumeChanged += (val) => { try { _shell.Volume = val; } catch { } };
                    SeekBarWindowed.MuteChanged += (on) => { try { _shell.IsMuted = on; } catch { } };
                    SeekBarWindowed.LayoutChanged += UpdateWindowedBarHeight;
                    SeekBarWindowed.SetMuted(_shell.IsMuted);
                    SeekBarWindowed.SetVolume(_shell.Volume);
                }

                if (BottomButtonRow != null)
                {
                    BottomButtonRow.SizeChanged -= BottomButtonRow_SizeChanged;
                    BottomButtonRow.SizeChanged += BottomButtonRow_SizeChanged;
                }
                UpdateWindowedBarHeight();
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.Debug($"[Timeline] windowed wiring failed: {ex.Message}");
            }
        }

        /// <summary>Mirrors a shell volume change onto both windowed bars.</summary>
        public void SetWindowedTimelineVolume(double volume)
        {
            try { TimelineWindowed?.SetVolume(volume); } catch { }
            try { SeekBarWindowed?.SetVolume(volume); } catch { }
        }

        /// <summary>Mirrors the shell's timeshift flag onto the windowed EPG timeline (the standard bar
        /// has no timeshift state to paint - its scale is the item that is playing).</summary>
        public void SetWindowedTimelineTimeshift(bool on)
        {
            try { TimelineWindowed?.SetTimeshift(on); } catch { }
        }

        /// <summary>
        /// Pushes the "seek bar style" setting onto both bars - the windowed bottom bar and the fullscreen
        /// overlay - so switching it in Settings takes effect without restarting playback. A channel without an
        /// EPG schedule keeps the standard bar even when the timeline style is selected.
        /// </summary>
        public void ApplySeekBarStyle()
        {
            SetWindowedBarMode(_windowedTimelineHasData && AppSettings.Current.SeekBar != SeekBarStyle.Standard);
            try { _overlayManager?.OverlayWpf?.ApplySeekBarStyle(); } catch { }
            UpdateWindowedBarHeight();
        }

        /// <summary>Space between the timeline and the button row, and above/below that whole stack.</summary>
        private const double BottomBarGap = 8;

        /// <summary>Equal padding above and below the stack, so the content reads as vertically centred.</summary>
        private const double BottomBarPadding = 8;

        /// <summary>Used only until the button row has been measured once.</summary>
        private const double BottomButtonRowFallbackHeight = 34;

        /// <summary>True while the windowed bar shows the EPG timeline; false while it shows the standard bar.</summary>
        private bool _windowedBarIsTimeline;

        /// <summary>True when the last snapshot carried a schedule the EPG band could really draw.</summary>
        private bool _windowedTimelineHasData;

        /// <summary>
        /// Chooses which windowed bar is on screen. They are two separate controls with their own rows, so
        /// switching styles never re-lays-out the other one. Returns true when the choice changed.
        /// </summary>
        private bool SetWindowedBarMode(bool showTimeline)
        {
            var changed = _windowedBarIsTimeline != showTimeline;
            _windowedBarIsTimeline = showTimeline;
            try
            {
                if (TimelineWindowed != null)
                {
                    TimelineWindowed.Visibility = showTimeline
                        ? System.Windows.Visibility.Visible
                        : System.Windows.Visibility.Collapsed;
                }
                if (SeekBarWindowed != null)
                {
                    SeekBarWindowed.Visibility = showTimeline
                        ? System.Windows.Visibility.Collapsed
                        : System.Windows.Visibility.Visible;
                }
            }
            catch { }
            return changed;
        }

        /// <summary>
        /// Feeds the windowed bar. The EPG band only takes the bar over when the style asks for it AND the channel
        /// really has a schedule; a channel without EPG keeps the plain standard progress bar - the same look the
        /// "no EPG" case had before this setting existed.
        /// </summary>
        internal void ApplyWindowedTimelineSnapshot(OverlayTimelineState? state)
        {
            var hasData = state != null && state.Programmes != null && state.Programmes.Count > 0;
            _windowedTimelineHasData = hasData;
            if (SetWindowedBarMode(hasData && AppSettings.Current.SeekBar != SeekBarStyle.Standard))
            {
                UpdateWindowedBarHeight();
            }
            try { TimelineWindowed?.SetTimelineState(state); } catch { }
        }

        /// <summary>
        /// Sizes the windowed bottom bar from what the bar that is actually on screen really renders, plus the
        /// button row, keeping the same padding above and below that stack. A hardcoded height made the standard
        /// progress bar hug the top and pushed the button row into/over the bottom edge; measuring instead keeps
        /// both the standard bar (no EPG data) and the full EPG timeline centred. Fullscreen collapses this bar
        /// entirely, so the two states never fight.
        /// </summary>
        private void UpdateWindowedBarHeight()
        {
            try
            {
                if (BottomBar == null) return;
                double barHeight;
                if (_windowedBarIsTimeline && TimelineWindowed != null)
                {
                    barHeight = TimelineWindowed.ActualHeight > 0.5
                        ? TimelineWindowed.ActualHeight
                        : TimelineWindowed.RequiredHeight;
                }
                else if (SeekBarWindowed != null)
                {
                    barHeight = SeekBarWindowed.ActualHeight > 0.5
                        ? SeekBarWindowed.ActualHeight
                        : SeekBarWindowed.RequiredHeight;
                }
                else
                {
                    return;
                }
                var buttonHeight = BottomButtonRow != null && BottomButtonRow.ActualHeight > 0.5
                    ? BottomButtonRow.ActualHeight
                    : BottomButtonRowFallbackHeight;
                var wanted = Math.Max(64, barHeight + BottomBarGap + buttonHeight + (BottomBarPadding * 2));
                if (Math.Abs(BottomBar.Height - wanted) > 0.5) BottomBar.Height = wanted;
            }
            catch { }
        }

        /// <summary>Re-sizes the bar once the button row has really been measured (fonts, DPI, theme swap).</summary>
        private void BottomButtonRow_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
            => UpdateWindowedBarHeight();

        public void OpenMultiScreen(int screenCount)
        {
            try
            {
                var groups = GetChannelGroupData();
                var win = new LibmpvIptvClient.Controls.MultiScreenWindow(screenCount, () => _shell.FilteredChannels, groups);
                win.Show();
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.Error($"[MultiScreen] Failed to open: {ex.Message}");
            }
        }

        private List<ChannelGroupData> GetChannelGroupData()
        {
            var result = new List<ChannelGroupData>();
            try
            {
                var channels = _shell.FilteredChannels;
                var groupMap = new Dictionary<string, List<Channel>>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var ch in channels)
                {
                    if (string.IsNullOrWhiteSpace(ch.Group)) continue;
                    if (!groupMap.TryGetValue(ch.Group, out var list))
                    {
                        list = new List<Channel>();
                        groupMap[ch.Group] = list;
                    }
                    list.Add(ch);
                }
                foreach (var kv in groupMap)
                {
                    result.Add(new ChannelGroupData { Name = kv.Key, Channels = kv.Value });
                }
            }
            catch { }
            return result;
        }

        private void ListBoxItem_ContextMenuOpening(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                // sender is the ListBoxItem itself — set context menu directly on it
                if (sender is not System.Windows.Controls.ListBoxItem item) return;
                if (item.DataContext is not Channel ch) return;

                _channelContextMenuManager?.ShowContextMenu(item, ch);
            }
            catch { }
        }

        private void GroupChannelItem_ContextMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.FrameworkElement fe) return;
                if (fe.DataContext is not Channel ch) return;
                // Use the Border (sender) as anchor directly — it's the visual item
                _channelContextMenuManager?.ShowContextMenu(fe, ch);
            }
            catch { }
        }

        private void ListChannelItem_ContextMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.FrameworkElement fe) return;
                if (fe.DataContext is not Channel ch) return;
                _channelContextMenuManager?.ShowContextMenu(fe, ch);
                e.Handled = true;
            }
            catch { }
        }
    }
}
