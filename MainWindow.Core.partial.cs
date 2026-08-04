using System;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using LibmpvIptvClient.Architecture.Platform.Player;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
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

        private bool _firstFrameLogged = false;
        private DateTime _lastHistoryUpdate = DateTime.MinValue;
        private readonly MainShellViewModel _shell = new();
        private readonly Action _epgRemindersChangedHandler;
        private DateTime _playStartTime;
        private double _baseWindowWidth = 1280;

        public MainWindow()
        {
            InitializeComponent();
            InitializeSourceRatioIcons();
            this.Loaded += (s, e) => UpdateIconBrushes();
            CbM3uList.ItemsSource = AppSettings.Current.SavedSources;
            CbM3uListGroups.ItemsSource = AppSettings.Current.SavedSources;
            SyncM3uComboBoxSelection();
            DataContext = _shell;
            _overlayManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowOverlayManager(this, _shell);
            _menuManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowMenuManager(this, _shell, _overlayManager);
            _settingsManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowSettingsManager(this, _shell, _overlayManager);
            _startupManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowStartupManager(this, _shell);
            _historyManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowHistoryManager(this, _shell, _userDataStore);
            _shellSyncManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowShellSyncManager(this, _shell, _overlayManager);
            _playbackTickManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowPlaybackTickManager(this, _shell, _overlayManager);
            _seekInteractionManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowSeekInteractionManager(this, _shell);
            _fullscreenInputManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowFullscreenInputManager(this);
            _windowedInputManager = new LibmpvIptvClient.Architecture.Presentation.View.MainWindowWindowedInputManager(this);
            _shell.PropertyChanged += OnShellPropertyChanged;
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
                    CbM3uListGroups.SelectedIndex = -1;
                    CbM3uListGroups.Text = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_M3uList", "选择M3U列表");
                    return;
                }
                var selected = sources.FirstOrDefault(s => s.IsSelected);
                CbM3uList.SelectedItem = selected;
                CbM3uListGroups.SelectedItem = selected;
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
                if (IconMute != null)
                {
                    IconMute.Fill = brush;
                }
            }
            catch { }
        }

        private const string VolumeData = "M2,5 L2,11 L5,11 L9,15 L9,1 L5,5 Z M11,5 Q13,8 11,11 M14,3 Q17,8 14,13";
        private const string MuteData = "M2,5 L2,11 L5,11 L9,15 L9,1 L5,5 Z M11,3 L15,13 M15,3 L11,13";

        public void UpdateMuteIcon(bool isMuted)
        {
            try
            {
                IconMute.Data = isMuted
                    ? System.Windows.Media.Geometry.Parse(MuteData)
                    : System.Windows.Media.Geometry.Parse(VolumeData);
            }
            catch { }
        }
    }
}
