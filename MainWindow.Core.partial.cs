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

        public MainWindow()
        {
            InitializeComponent();
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
    }
}
