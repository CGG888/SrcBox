using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ModernWpf.Controls;
using LibmpvIptvClient.Architecture.Application.Player;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public class MainShellViewModel : ViewModelBase
    {
        public MainShellViewModel()
        {
            MenuActions = new MainWindowMenuActionsViewModel(this);
            ChannelPlaybackActions = new MainWindowChannelPlaybackActionsViewModel(this);
            RecordingPlaybackActions = new MainWindowRecordingPlaybackActionsViewModel(this);
            ShortcutActions = new MainWindowShortcutActionsViewModel(this);
            DragDropActions = new MainWindowDragDropActionsViewModel(this);
            MediaInfo = new MainWindowMediaInfoViewModel(this);
        }

        public event Action? RequestClose;
        public void CloseWindow() => RequestClose?.Invoke();

        // Sub-ViewModels
        public MainWindowMenuActionsViewModel MenuActions { get; }
        public MainWindowChannelPlaybackActionsViewModel ChannelPlaybackActions { get; }
        public MainWindowRecordingPlaybackActionsViewModel RecordingPlaybackActions { get; }
        public MainWindowDialogActionsViewModel DialogActions { get; } = new();
        public MainWindowShortcutActionsViewModel ShortcutActions { get; }
        public MainWindowTitleBarActionsViewModel TitleBarActions { get; } = new();
        public MainWindowPlaybackActionsViewModel PlaybackActions { get; } = new();
        public MainWindowViewToggleActionsViewModel ViewToggleActions { get; } = new();
        public MainWindowOverlayBindingActionsViewModel OverlayBindingActions { get; } = new();
        public MainWindowOverlayPreviewActionsViewModel OverlayPreviewActions { get; } = new();
        public MainWindowEpgActionsViewModel EpgActions { get; } = new();
        public MainWindowEpgReminderActionsViewModel EpgReminderActions { get; } = new();
        public MainWindowEpgReminderSyncActionsViewModel EpgReminderSyncActions { get; } = new();
        public MainWindowEpgSelectionSyncActionsViewModel EpgSelectionSyncActions { get; } = new();
        public MainWindowChannelListActionsViewModel ChannelListActions { get; } = new();
        public MainWindowChannelInteractionActionsViewModel ChannelInteractionActions { get; } = new();
        public MainWindowChannelPlaybackSyncActionsViewModel ChannelPlaybackSyncActions { get; } = new();
        public MainWindowPlaybackStatusOverlaySyncActionsViewModel PlaybackStatusOverlaySyncActions { get; } = new();
        public MainWindowPlaybackSpeedOverlaySyncActionsViewModel PlaybackSpeedOverlaySyncActions { get; } = new();
        public MainWindowVolumeMuteOverlaySyncActionsViewModel VolumeMuteOverlaySyncActions { get; } = new();
        public MainWindowPlaybackPauseOverlaySyncActionsViewModel PlaybackPauseOverlaySyncActions { get; } = new();
        public MainWindowRecordingActionsViewModel RecordingActions { get; } = new();
        public MainWindowHistoryActionsViewModel HistoryActions { get; } = new();
        public MainWindowTimeshiftOverlaySyncActionsViewModel TimeshiftOverlaySyncActions { get; } = new();
        public MainWindowSourceLoaderViewModel SourceLoader { get; } = new();
        public MainWindowDragDropActionsViewModel DragDropActions { get; }
        public MainWindowWindowStateActionsViewModel WindowStateActions { get; } = new();
        public MainWindowMediaInfoViewModel MediaInfo { get; }

        private double _volume = 60;
        public double Volume
        {
            get => _volume;
            set
            {
                if (SetProperty(ref _volume, value))
                {
                    _playerEngine?.SetVolume(value);
                }
            }
        }

        private bool _isMuted;
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (SetProperty(ref _isMuted, value))
                {
                    _playerEngine?.SetMute(value);
                }
            }
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (SetProperty(ref _isPaused, value))
                {
                    _playerEngine?.Pause(value);
                    UpdatePlayPauseSymbol();
                }
            }
        }

        private Symbol _playPauseSymbol = Symbol.Play;
        public Symbol PlayPauseSymbol
        {
            get => _playPauseSymbol;
            set => SetProperty(ref _playPauseSymbol, value);
        }

        private double _playbackSpeed = 1.0;
        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            set
            {
                if (SetProperty(ref _playbackSpeed, value))
                {
                    _playerEngine?.SetSpeed(value);
                }
            }
        }

        private EpgService? _epgService;
        private IPlayerEngine? _playerEngine;
        private UserDataStore? _userDataStore;
        private Action<double, double>? _overlayTimeSync;
        private Action<DateTime, DateTime>? _overlayRangeSync;
        private Action<DateTime, DateTime, bool>? _overlayTimeshiftLabelSync;
        private System.Net.Http.HttpClient? _httpClient;

        public void SetHttpClient(System.Net.Http.HttpClient client) => _httpClient = client;

        public void InjectServices(
            EpgService epgService, 
            IPlayerEngine playerEngine,
            UserDataStore userDataStore,
            Action<double, double>? overlayTimeSync = null, 
            Action<DateTime, DateTime>? overlayRangeSync = null,
            Action<DateTime, DateTime, bool>? overlayTimeshiftLabelSync = null)
        {
            _epgService = epgService;
            _playerEngine = playerEngine;
            _userDataStore = userDataStore;
            _overlayTimeSync = overlayTimeSync;
            _overlayRangeSync = overlayRangeSync;
            _overlayTimeshiftLabelSync = overlayTimeshiftLabelSync;
        }

        public IPlayerEngine? PlayerEngine => _playerEngine;
        public EpgService? EpgService => _epgService;
        public UserDataStore? UserDataStore => _userDataStore;

        private EpgProgram? _currentPlayingProgram;
        public EpgProgram? CurrentPlayingProgram
        {
            get => _currentPlayingProgram;
            set => SetProperty(ref _currentPlayingProgram, value);
        }

        private double _currentTimePos;
        public double CurrentTimePos
        {
            get => _currentTimePos;
            set => SetProperty(ref _currentTimePos, value);
        }

        private DateTime _lastHistoryUpdate = DateTime.MinValue;

        public void UpdateHistoryProgress()
        {
            if (_userDataStore == null) return;
            var now = DateTime.UtcNow;
            if ((now - _lastHistoryUpdate).TotalSeconds < 5) return;
            _lastHistoryUpdate = now;
            try
            {
                var untitledLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("App_Untitled", "未命名");
                double dur = SeekMaximum;
                if (IsTimeshiftActive) dur = (TimeshiftMax - TimeshiftMin).TotalSeconds;

                HistoryActions.AddOrUpdateHistory(
                    _userDataStore,
                    CurrentTimePos,
                    dur,
                    CurrentChannel,
                    CurrentUrl,
                    IsTimeshiftActive,
                    CurrentPlayingProgram,
                    untitledLabel);
            }
            catch { }
        }

        public DateTime? GetPlaybackLocalTime()
        {
            try
            {
                if (IsTimeshiftActive)
                {
                    var total = Math.Max(1, (TimeshiftMax - TimeshiftMin).TotalSeconds);
                    var sec = Math.Max(0, Math.Min(total, TimeshiftCursorSec));
                    return TimeshiftMin.AddSeconds(sec);
                }
                if (CurrentPlayingProgram != null)
                {
                    // Use cached CurrentTimePos updated by HandlePlaybackTick
                    return CurrentPlayingProgram.Start.AddSeconds(Math.Max(0, CurrentTimePos));
                }
            }
            catch { }
            return null;
        }

        private string FormatTime(double sec)
        {
            if (sec < 0) sec = 0;
            var ts = TimeSpan.FromSeconds(sec);
            return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }

        public void HandlePlaybackTick(double? timePos, double? duration, bool isFirstFrameLogged)
        {
            CurrentTimePos = timePos ?? 0;
            if (CurrentTimePos > 0) ChannelPlaybackActions.CheckPlaybackStarted(CurrentTimePos);

            if (CurrentRecordingPlaying != null)
            {
                if (PlayerEngine?.IsEofReached() == true)
                {
                    ClearRecordingPlayingIndicator();
                }
            }

            if (IsTimeshiftActive)
            {
                TimeshiftMax = DateTime.Now;
                var posMpv = timePos ?? 0;
                var currentPlaybackTime = TimeshiftStart.AddSeconds(posMpv);
                var current = (currentPlaybackTime - TimeshiftMin).TotalSeconds;
                var total = (TimeshiftMax - TimeshiftMin).TotalSeconds;
                
                if (!IsSeeking)
                {
                    TimeshiftCursorSec = Math.Max(0, Math.Min(total, current));
                    UpdateTimeshiftUi();
                }
                else
                {
                    try 
                    { 
                        DurationText = TimeshiftMax.ToString("yyyy-MM-dd HH:mm:ss");
                        _overlayRangeSync?.Invoke(TimeshiftMin, TimeshiftMax);
                    } catch { }
                }

                if (CurrentChannel != null && EpgService != null)
                {
                    var programs = EpgService.GetPrograms(CurrentChannel.TvgId, CurrentChannel.TvgName, CurrentChannel.Name);
                    var programAtTime = programs?.FirstOrDefault(p => currentPlaybackTime >= p.Start && currentPlaybackTime < p.End);
                    if (programAtTime != null && programAtTime != CurrentPlayingProgram)
                    {
                        CurrentPlayingProgram = programAtTime;
                    }
                }
            }
            else
            {
                if (!IsSeeking && duration.HasValue && duration.Value > 0)
                {
                    SeekMaximum = duration.Value;
                    SeekValue = timePos ?? 0;
                }
                ElapsedTimeText = FormatTime(timePos ?? 0);
                if (duration.HasValue) DurationText = FormatTime(duration.Value);
                
                if (timePos.HasValue && duration.HasValue)
                {
                    _overlayTimeSync?.Invoke(timePos.Value, duration.Value);
                }
            }
            UpdateHistoryProgress();
        }

        private bool _isTimeshiftActive;
        private bool _timeshiftSeeked;
        private bool _timeshiftSeekedForUi;
        private bool _timeshiftReturnHasStatus;
        private PlaybackStatusKind _timeshiftReturnKind = PlaybackStatusKind.Live;
        private bool _suppressTimeshiftAutoReturn;
        private EpgProgram? _timeshiftReturnProgram;
        private PlaybackState _playbackState = PlaybackState.Default;
        public bool IsTimeshiftActive
        {
            get => _isTimeshiftActive;
            set
            {
                if (_isTimeshiftActive != value)
                {
                    if (value)
                    {
                        // Attempting to enable
                        if (!CanEnableTimeshift())
                        {
                            // Revert UI if bound
                            OnPropertyChanged(nameof(IsTimeshiftActive));
                            return;
                        }
                    }

                    SetProperty(ref _isTimeshiftActive, value);
                    HandleTimeshiftChanged(value);
                }
            }
        }

        private bool CanEnableTimeshift()
        {
            if (_playerEngine == null) return false;
            if (CurrentRecordingPlaying != null) return false;
            if (string.IsNullOrWhiteSpace(CurrentUrl)) return false;
            bool hasSource = CurrentChannel != null && !string.IsNullOrEmpty(CurrentChannel.CatchupSource);
            bool hasFallback = AppSettings.Current.Timeshift.Enabled && !string.IsNullOrEmpty(AppSettings.Current.Timeshift.UrlFormat);
            return hasSource || hasFallback;
        }

        private void HandleTimeshiftChanged(bool on)
        {
            try 
            {
                Diagnostics.Logger.Info(on ? "时移已开启" : "时移已关闭");
            } 
            catch { }

            // ClearRecordingPlayingIndicator logic (implied by not being recording state)

            if (on)
            {
                _timeshiftSeeked = false;
                _timeshiftSeekedForUi = false;
                _timeshiftReturnHasStatus = false;
                _timeshiftReturnProgram = CurrentPlayingProgram;
                if (CurrentRecordingPlaying != null)
                {
                    _timeshiftReturnHasStatus = true;
                    _timeshiftReturnKind = PlaybackStatusKind.Record;
                }
                else if (CurrentPlayingProgram != null)
                {
                    _timeshiftReturnHasStatus = true;
                    _timeshiftReturnKind = PlaybackStatusKind.Playback;
                }
                else if (!string.IsNullOrWhiteSpace(CurrentUrl))
                {
                    _timeshiftReturnHasStatus = true;
                    _timeshiftReturnKind = PlaybackStatusKind.Live;
                }
                if (CurrentPlayingProgram != null)
                {
                    CurrentPlayingProgram = null;
                }
                var range = TimeshiftOverlaySyncActions.CalculateTimeshiftRange(
                    CurrentChannel,
                    _epgService,
                    AppSettings.Current.Timeshift.DurationHours);

                TimeshiftMin = range.Min;
                TimeshiftMax = range.Max;
                TimeshiftStart = range.Start;
                TimeshiftCursorSec = range.CursorSec;
                DispatchPlaybackEvent(new StartTimeshiftPlayback(
                    CurrentChannel?.TvgId ?? CurrentChannel?.Id ?? CurrentChannel?.Name ?? "",
                    TimeshiftMin.AddSeconds(TimeshiftCursorSec),
                    null,
                    CurrentUrl));
                
                // Initial UI Sync
                UpdateTimeshiftUi();
                SyncPlaybackSpeed(true, true, false);
            }
            else
            {
                var shouldReturnLive = _timeshiftSeeked && !_suppressTimeshiftAutoReturn;
                _timeshiftSeeked = false;
                _suppressTimeshiftAutoReturn = false;

                if (shouldReturnLive)
                {
                    if (CurrentChannel != null)
                    {
                        try { ChannelPlaybackActions.PlayChannel(CurrentChannel); } catch { }
                    }
                    SyncPlaybackSpeed(false, true, true);
                    return;
                }

                if (_timeshiftReturnHasStatus)
                {
                    if (_timeshiftReturnKind == PlaybackStatusKind.Playback && _timeshiftReturnProgram != null)
                    {
                        CurrentPlayingProgram = _timeshiftReturnProgram;
                    }
                    _timeshiftReturnProgram = null;
                    if (_timeshiftReturnKind == PlaybackStatusKind.Record)
                    {
                        DispatchPlaybackEvent(new StartRecordingPlayback(CurrentUrl));
                    }
                    else if (_timeshiftReturnKind == PlaybackStatusKind.Playback)
                    {
                        DispatchPlaybackEvent(new StartReplayPlayback(
                            CurrentChannel?.TvgId ?? CurrentChannel?.Id ?? CurrentChannel?.Name ?? "",
                            _timeshiftReturnProgram,
                            CurrentUrl));
                    }
                    else
                    {
                        DispatchPlaybackEvent(new StartLivePlayback(
                            CurrentChannel?.TvgId ?? CurrentChannel?.Id ?? CurrentChannel?.Name ?? "",
                            _timeshiftReturnProgram,
                            CurrentUrl));
                    }
                    var enableSpeed = _timeshiftReturnKind == PlaybackStatusKind.Playback || _timeshiftReturnKind == PlaybackStatusKind.Record;
                    SyncPlaybackSpeed(enableSpeed, true, !enableSpeed);
                }
                else
                {
                    DispatchPlaybackEvent(new StopPlayback(false));
                    SyncPlaybackSpeed(false, true, true);
                }
            }
        }

        internal void DispatchPlaybackEvent(PlaybackEvent ev)
        {
            var next = PlaybackStateReducer.Reduce(_playbackState, ev);
            _playbackState = next;
            PlaybackMode = next.Mode;
            PlaybackWindowForm = next.WindowForm;
            PlaybackFocusTime = next.FocusTime;
            if (ev is StartLivePlayback or StartReplayPlayback or StartTimeshiftPlayback or StartRecordingPlayback or StartLocalFilePlayback)
            {
                IsPaused = false;
                UpdatePlayPauseSymbol();
            }
            ApplyPlaybackProjection(next);
        }

        internal void RefreshPlaybackProjection()
        {
            ApplyPlaybackProjection(_playbackState);
        }

        private void ApplyPlaybackProjection(PlaybackState state)
        {
            if (state.Mode == PlaybackMode.Stopped)
            {
                PlaybackStatusText = "";
                PlaybackStatusBrush = ResolvePlaybackStatusBrush(PlaybackStatusBrushKind.Live);
                IsSpeedEnabled = false;
                IsPaused = false;
                UpdatePlayPauseSymbol();
                return;
            }

            var text = ResolvePlaybackStatusText(state.Mode);
            var brushKind = ResolvePlaybackStatusBrushKind(state.Mode);
            PlaybackStatusOverlaySyncActions.Sync(
                new PlaybackStatusState(text, brushKind),
                ResolvePlaybackStatusBrush,
                (t, b) =>
                {
                    PlaybackStatusText = t;
                    PlaybackStatusBrush = b;
                },
                null,
                null
            );

            IsSpeedEnabled = state.Mode == PlaybackMode.Replay ||
                             state.Mode == PlaybackMode.Timeshift ||
                             state.Mode == PlaybackMode.RecordingPlayback;
            UpdatePlayPauseSymbol();
        }

        private void UpdatePlayPauseSymbol()
        {
            PlayPauseSymbol = PlaybackMode == PlaybackMode.Stopped || IsPaused
                ? Symbol.Play
                : Symbol.Pause;
        }

        private string ResolvePlaybackStatusText(PlaybackMode mode)
        {
            return mode switch
            {
                PlaybackMode.Timeshift => LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Timeshift", "时移"),
                PlaybackMode.Replay => LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Playback", "回放"),
                PlaybackMode.RecordingPlayback => LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Record", "录播"),
                PlaybackMode.LocalFile => LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_LocalFile", "本地"),
                _ => LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Live", "直播")
            };
        }

        private PlaybackStatusBrushKind ResolvePlaybackStatusBrushKind(PlaybackMode mode)
        {
            return mode switch
            {
                PlaybackMode.Timeshift => PlaybackStatusBrushKind.Timeshift,
                PlaybackMode.Replay => PlaybackStatusBrushKind.Replay,
                PlaybackMode.RecordingPlayback => PlaybackStatusBrushKind.Replay,
                PlaybackMode.LocalFile => PlaybackStatusBrushKind.Replay,
                _ => PlaybackStatusBrushKind.Live
            };
        }

        // Helper to resolve brush (need to replicate logic or inject service)
        // For now, we can use a simplified version or assume View handles brush conversion if we expose Kind.
        // But existing code uses Brush. 
        // We will need a way to get resources. 
        // Hack: Application.Current.FindResource or pass a provider.
        // Better: Use a dedicated Service for Resource/Brush resolution.
        // For this step, I'll use Application.Current.FindResource carefully.
        
        internal System.Windows.Media.Brush ResolvePlaybackStatusBrush(PlaybackStatusBrushKind kind)
        {
            try
            {
                return kind switch
                {
                    PlaybackStatusBrushKind.Orange => System.Windows.Media.Brushes.Orange,
                    PlaybackStatusBrushKind.Timeshift => (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("StatusTimeshiftBrush"),
                    PlaybackStatusBrushKind.Replay => (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("StatusReplayBrush"),
                    _ => (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("StatusLiveBrush")
                };
            }
            catch
            {
                return System.Windows.Media.Brushes.White;
            }
        }

        public void SeekTo(double value)
        {
            if (IsTimeshiftActive)
            {
                TimeshiftCursorSec = Math.Max(0, value);
                var t = TimeshiftMin.AddSeconds(value);
                try { Diagnostics.Logger.Info($"时移定位到 {t:yyyy-MM-dd HH:mm:ss}"); } catch { }
            }
            else
            {
                _playerEngine?.SeekAbsolute(value);
            }
        }

        public void MarkTimeshiftSeeked()
        {
            _timeshiftSeeked = true;
            _timeshiftSeekedForUi = true;
        }

        public bool ConsumeTimeshiftSeekedForUi()
        {
            if (!_timeshiftSeekedForUi) return false;
            _timeshiftSeekedForUi = false;
            return true;
        }

        public void DisableTimeshiftForStop()
        {
            if (!IsTimeshiftActive)
            {
                _suppressTimeshiftAutoReturn = false;
                return;
            }
            _suppressTimeshiftAutoReturn = true;
            IsTimeshiftActive = false;
        }

        public void UpdateTimeshiftUi()
        {
             try
            {
                if (!IsTimeshiftActive) return;
                
                // We don't have _mpv here directly for GetTimePos used in BuildState.
                // However, BuildState uses _mpv for normal playback (IsActive=false).
                // For Timeshift (IsActive=true), BuildState uses internal calculations mostly, 
                // BUT it might use _mpv if we passed it?
                // Let's check MainWindowTimeshiftOverlaySyncActionsViewModel.BuildState
                // It takes MpvInterop mpv.
                // In Timeshift mode, it sets state.Max = DateTime.Now and calculates offsets.
                // It does NOT use mpv in IsActive=true block.
                
                var state = TimeshiftOverlaySyncActions.BuildState(
                    true,
                    TimeshiftMin,
                    TimeshiftMax,
                    TimeshiftCursorSec,
                    null, // MPV not needed for Timeshift Active calculation in BuildState
                    IsSeeking,
                    SeekValue);

                TimeshiftOverlaySyncActions.Sync(
                    state,
                    (val, max) => 
                    { 
                        SeekMaximum = max; 
                        if (!IsSeeking) SeekValue = val; 
                    },
                    (elapsed, duration) => 
                    { 
                        ElapsedTimeText = elapsed;
                        DurationText = duration;
                    },
                    (cur, total) => { try { _overlayTimeSync?.Invoke(cur, total); } catch { } },
                    (min, max) => { try { _overlayRangeSync?.Invoke(min, max); } catch { } },
                    (cursor, max, seeking) => { try { _overlayTimeshiftLabelSync?.Invoke(cursor, max, seeking); } catch { } });
            }
            catch { }
        }

        private void SyncPlaybackSpeed(bool enabled, bool syncMpv, bool resetLocalSpeedWhenDisabled)
        {
             try
            {
                IsSpeedEnabled = enabled;
                var state = PlaybackSpeedOverlaySyncActions.BuildState(PlaybackSpeed, enabled);
                if (!enabled && resetLocalSpeedWhenDisabled)
                {
                    PlaybackSpeed = state.Speed;
                }
                SpeedText = state.Label;
                
                // Sync MPV via Action if needed, or rely on property change if MPV observes it
                if (syncMpv)
                {
                     // We need a way to set MPV speed. 
                     // Currently MainWindow.xaml.cs handles this via OnShellPropertyChanged?
                     // No, MainWindow calls _shell.PlaybackActions.TrySetSpeed(_mpv, speed).
                     // But here we are in VM.
                     // We can rely on `PlaybackSpeed` property change to trigger MainWindow's listener 
                     // if we set `PlaybackSpeed`.
                     // But if `PlaybackSpeed` doesn't change (e.g. just enabling/disabling), we might need explicit sync.
                     // However, `SyncPlaybackSpeed` in MainWindow also called `TrySetSpeed`.
                     // Let's rely on PropertyChanged for now, or add a request action.
                     // Actually, `IsSpeedEnabled` change might need to trigger UI update.
                }
            }
            catch { }
        }


        private DateTime _timeshiftMin;
        public DateTime TimeshiftMin
        {
            get => _timeshiftMin;
            set => SetProperty(ref _timeshiftMin, value);
        }

        private DateTime _timeshiftMax;
        public DateTime TimeshiftMax
        {
            get => _timeshiftMax;
            set => SetProperty(ref _timeshiftMax, value);
        }

        private DateTime _timeshiftStart;
        public DateTime TimeshiftStart
        {
            get => _timeshiftStart;
            set => SetProperty(ref _timeshiftStart, value);
        }

        private double _timeshiftCursorSec;
        public double TimeshiftCursorSec
        {
            get => _timeshiftCursorSec;
            set => SetProperty(ref _timeshiftCursorSec, value);
        }

        private List<Channel> _channels = new List<Channel>();
        public List<Channel> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        private Channel? _currentChannel;
        public Channel? CurrentChannel
        {
            get => _currentChannel;
            set => SetProperty(ref _currentChannel, value);
        }

        private List<Source> _currentSources = new List<Source>();
        public List<Source> CurrentSources
        {
            get => _currentSources;
            set => SetProperty(ref _currentSources, value);
        }

        private string? _selectedGroup;
        public string? SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value))
                {
                    ApplyChannelFilter();
                }
            }
        }

        private IReadOnlyList<Channel> _filteredChannels = new List<Channel>();
        public IReadOnlyList<Channel> FilteredChannels
        {
            get => _filteredChannels;
            set => SetProperty(ref _filteredChannels, value);
        }

        private IReadOnlyList<ChannelGroupItem> _channelGroups = new List<ChannelGroupItem>();
        public IReadOnlyList<ChannelGroupItem> ChannelGroups
        {
            get => _channelGroups;
            set => SetProperty(ref _channelGroups, value);
        }

        private IReadOnlyList<Channel> _favorites = new List<Channel>();
        public IReadOnlyList<Channel> Favorites
        {
            get => _favorites;
            set => SetProperty(ref _favorites, value);
        }
        
        private IReadOnlyList<HistoryItem> _history = new List<HistoryItem>();
        public IReadOnlyList<HistoryItem> History
        {
            get => _history;
            set => SetProperty(ref _history, value);
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyChannelFilter();
                }
            }
        }
        
        private string _filterCountText = "";
        public string FilterCountText
        {
            get => _filterCountText;
            set => SetProperty(ref _filterCountText, value);
        }

        public void ApplyChannelFilter()
        {
            var result = ChannelListActions.BuildChannelFilterResult(Channels, SearchText, SelectedGroup);
            FilteredChannels = result.Channels;
            FilterCountText = result.CountText;
            UpdateGroups();
        }

        public void UpdateGroups()
        {
            ChannelGroups = ChannelListActions.BuildGroups(Channels);
        }

        public void UpdateFavorites()
        {
            if (UserDataStore == null) return;
            Favorites = ChannelInteractionActions.BuildFavoriteList(Channels, c => UserDataStore.ComputeKey(c));
        }

        /// <summary>
        /// 修复 Bug #1：从 UserDataStore 的持久化收藏列表还原所有 Channel.Favorite 标记。
        /// 必须在 LoadChannels 后、UpdateFavorites 前调用。
        /// 之前漏掉了这一步，导致重启后 Channel.Favorite 全部为 false，收藏目录永远为空。
        /// </summary>
        public void ApplyFavoritesFromStore(List<Channel> channels)
        {
            if (UserDataStore == null || channels == null) return;
            foreach (var c in channels)
            {
                if (c == null) continue;
                try
                {
                    var key = UserDataStore.ComputeKey(c);
                    c.Favorite = UserDataStore.IsFavorite(key);
                }
                catch { }
            }
        }

        public void LoadHistory()
        {
            if (UserDataStore != null)
                History = UserDataStore.GetHistory();
        }


        public void InitializeServices()
        {
            M3UParser = new LibmpvIptvClient.Services.M3UParser();
            var checker = new LibmpvIptvClient.Services.IptvCheckerClient(M3UParser, "", "/api/export/json", "/api/export/m3u", "");
            ChannelService = new LibmpvIptvClient.Services.ChannelService(M3UParser, checker);
        }

        public LibmpvIptvClient.Services.M3UParser? M3UParser { get; private set; }
        public LibmpvIptvClient.Services.ChannelService? ChannelService { get; private set; }

        public async System.Threading.Tasks.Task LoadChannels(string url)
        {
            if (ChannelService == null) return;

            try
            {
                LibmpvIptvClient.Diagnostics.Logger.Info("正在加载频道...");

                var (loadedChannels, loadedTvgUrl) = await SourceLoader.LoadChannelsAsync(ChannelService, url, msg => LibmpvIptvClient.Diagnostics.Logger.Log(msg));

                ChannelListActions.ComputeGlobalIndices(loadedChannels);
                ApplyFavoritesFromStore(loadedChannels);

                if (loadedChannels.Count == 0)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Info("未解析到频道");
                    return;
                }

                var epgUrl = AppSettings.Current.CustomEpgUrl;
                if (string.IsNullOrWhiteSpace(epgUrl))
                {
                    epgUrl = loadedTvgUrl;
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Channels = loadedChannels;
                    FilteredChannels = loadedChannels;
                    FilterCountText = string.Format(LibmpvIptvClient.Helpers.ResxLocalizer.Get("Drawer_CountAll", "共 {0} 个频道"), loadedChannels.Count);
                    LoadHistory();
                });

                // Trigger immediate health scan after channels are loaded
                Diagnostics.Logger.Info($"[Shell] About to call RefreshAll with {loadedChannels?.Count ?? -1} channels");
                LibmpvIptvClient.Services.SourceHealthService.Instance.RefreshAll(loadedChannels);

                try
                {
                    // NEW-14: Run DNS prefetch and connection preheat in parallel instead of sequentially
                    _ = Task.WhenAll(
                        Task.Run(() => LibmpvIptvClient.Services.DnsPrefetcher.PrefetchForChannels(loadedChannels, maxHosts: 60)),
                        Task.Run(() => LibmpvIptvClient.Services.ConnectionPreheater.PreheatForChannels(loadedChannels, maxHosts: 30))
                    );
                }
                catch { }

                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var filterResult = ChannelListActions.BuildChannelFilterResult(loadedChannels, SearchText, SelectedGroup);
                        var groupsResult = ChannelListActions.BuildGroups(loadedChannels);
                        var favoritesResult = ChannelInteractionActions.BuildFavoriteList(loadedChannels, c => UserDataStore.ComputeKey(c));
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            FilteredChannels = filterResult.Channels;
                            FilterCountText = filterResult.CountText;
                            ChannelGroups = groupsResult;
                            Favorites = favoritesResult;
                        });
                    }
                    catch (Exception ex)
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Warn($"[ChannelList] Filter/build failed: {ex.Message}");
                    }
                });

                if (!string.IsNullOrEmpty(epgUrl) && _epgService != null)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Info("正在加载节目单...");
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await _epgService.LoadEpgAsync(epgUrl);
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                EpgActions.SyncChannelCurrentProgramTitles(loadedChannels, ch => _epgService.GetCurrentProgram(ch.TvgId, ch.Name));
                            });
                            System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                            {
                                try { await RecordingActions.LoadRecordingsLocalGroupedAsync(loadedChannels, _epgService); } catch { }
                            });
                        }
                        catch (Exception ex) { LibmpvIptvClient.Diagnostics.Logger.Info("节目单加载失败: " + ex.Message); }
                    });
                }
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Error("加载频道失败 " + ex.Message);
            }
        }

        public async System.Threading.Tasks.Task ForceRefreshChannels(string url)
        {
            LibmpvIptvClient.Diagnostics.Logger.Info("强制刷新频道列表...");
            LibmpvIptvClient.Services.M3UCacheService.Instance.RemoveCache(url);
            await LoadChannels(url);
        }

        public void LoadSingleStream(string url)
        {
            Channels.Clear();
            var streamLabel = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Stream_Network", "网络流");
            Channels = SourceLoader.LoadSingleStream(url, streamLabel);
            
            ChannelListActions.ComputeGlobalIndices(Channels);
            
            ApplyChannelFilter();
            
            if (Channels.Count > 0)
            {
                ChannelPlaybackActions.PlayChannel(Channels[0], null);
            }
        }

        private IReadOnlyList<LibmpvIptvClient.Services.RecordingGroup> _recordingGroups = new List<LibmpvIptvClient.Services.RecordingGroup>();
        public IReadOnlyList<LibmpvIptvClient.Services.RecordingGroup> RecordingGroups
        {
            get => _recordingGroups;
            set => SetProperty(ref _recordingGroups, value);
        }

        private IReadOnlyList<LibmpvIptvClient.Services.RecordingEntry> _filteredRecordings = new List<LibmpvIptvClient.Services.RecordingEntry>();
        public IReadOnlyList<LibmpvIptvClient.Services.RecordingEntry> FilteredRecordings
        {
            get => _filteredRecordings;
            set => SetProperty(ref _filteredRecordings, value);
        }

        private bool _isDrawerCollapsed = true;
        public bool IsDrawerCollapsed
        {
            get => _isDrawerCollapsed;
            set
            {
                if (SetProperty(ref _isDrawerCollapsed, value))
                {
                    OnPropertyChanged(nameof(DrawerGridLength));
                }
            }
        }

        private bool _isMinimalMode;
        public bool IsMinimalMode
        {
            get => _isMinimalMode;
            set => SetProperty(ref _isMinimalMode, value);
        }

        private double _drawerWidth = 380;
        public double DrawerWidth
        {
            get => _drawerWidth;
            set
            {
                if (SetProperty(ref _drawerWidth, value))
                {
                    OnPropertyChanged(nameof(DrawerGridLength));
                }
            }
        }

        public GridLength DrawerGridLength => IsDrawerCollapsed ? new GridLength(0) : new GridLength(DrawerWidth > 0 ? DrawerWidth : 380);

        private string _playbackStatusText = "";
        public string PlaybackStatusText
        {
            get => _playbackStatusText;
            set => SetProperty(ref _playbackStatusText, value);
        }

        private PlaybackMode _playbackMode = PlaybackMode.Stopped;
        public PlaybackMode PlaybackMode
        {
            get => _playbackMode;
            set => SetProperty(ref _playbackMode, value);
        }

        private PlaybackWindowForm _playbackWindowForm = PlaybackWindowForm.Window;
        public PlaybackWindowForm PlaybackWindowForm
        {
            get => _playbackWindowForm;
            set => SetProperty(ref _playbackWindowForm, value);
        }

        private DateTime? _playbackFocusTime;
        public DateTime? PlaybackFocusTime
        {
            get => _playbackFocusTime;
            set => SetProperty(ref _playbackFocusTime, value);
        }

        private System.Windows.Media.Brush _playbackStatusBrush = System.Windows.Media.Brushes.White;
        public System.Windows.Media.Brush PlaybackStatusBrush
        {
            get => _playbackStatusBrush;
            set => SetProperty(ref _playbackStatusBrush, value);
        }

        private string _currentAspect = "default";
        public string CurrentAspect
        {
            get => _currentAspect;
            set => SetProperty(ref _currentAspect, value);
        }

        private string _currentUrl = "";
        public string CurrentUrl
        {
            get => _currentUrl;
            set => SetProperty(ref _currentUrl, value);
        }

        private bool _isSeeking;
        public bool IsSeeking
        {
            get => _isSeeking;
            set => SetProperty(ref _isSeeking, value);
        }

        private List<DateTime> _availableDates = new List<DateTime>();
        public List<DateTime> AvailableDates
        {
            get => _availableDates;
            set => SetProperty(ref _availableDates, value);
        }

        private DateTime _currentEpgDate = DateTime.Today;
        public DateTime CurrentEpgDate
        {
            get => _currentEpgDate;
            set => SetProperty(ref _currentEpgDate, value);
        }

        private string _elapsedTimeText = "00:00";
        public string ElapsedTimeText
        {
            get => _elapsedTimeText;
            set => SetProperty(ref _elapsedTimeText, value);
        }

        private string _durationText = "00:00";
        public string DurationText
        {
            get => _durationText;
            set => SetProperty(ref _durationText, value);
        }

        private string _speedText = "1.0x";
        public string SpeedText
        {
            get => _speedText;
            set => SetProperty(ref _speedText, value);
        }

        private bool _isSpeedEnabled = false;
        public bool IsSpeedEnabled
        {
            get => _isSpeedEnabled;
            set => SetProperty(ref _isSpeedEnabled, value);
        }

        private double _seekValue;
        public double SeekValue
        {
            get => _seekValue;
            set => SetProperty(ref _seekValue, value);
        }

        private double _seekMaximum = 100;
        public double SeekMaximum
        {
            get => _seekMaximum;
            set => SetProperty(ref _seekMaximum, value);
        }

        private LibmpvIptvClient.Services.RecordingEntry? _currentRecordingPlaying;
        public LibmpvIptvClient.Services.RecordingEntry? CurrentRecordingPlaying
        {
            get => _currentRecordingPlaying;
            set
            {
                if (value != _currentRecordingPlaying)
                {
                    if (_currentRecordingPlaying != null) _currentRecordingPlaying.IsPlaying = false;
                    SetProperty(ref _currentRecordingPlaying, value);
                    if (value != null) value.IsPlaying = true;
                }
            }
        }

        public void ClearRecordingPlayingIndicator()
        {
            CurrentRecordingPlaying = null;
        }
    }
}
