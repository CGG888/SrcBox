using System;
using System.Reflection;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Text.Json;
using LibmpvIptvClient.Architecture.Application.Settings;
using LibmpvIptvClient.Architecture.Application.Shared;
using LibmpvIptvClient.Architecture.Core;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;
namespace LibmpvIptvClient
{
    public partial class SettingsWindow : Window
    {
        public event Action<PlaybackSettings>? ApplySettingsRequested;
        public PlaybackSettings Result { get; private set; } = new PlaybackSettings();
        private readonly SettingsLegalDocumentsViewModel _legalDocumentsViewModel;
        private readonly SettingsAppearanceViewModel _appearanceViewModel = new();
        private readonly SettingsWebDavViewModel _webDavViewModel;
        private readonly SettingsPlaybackViewModel _playbackViewModel = new();
        private readonly SettingsTimeOverrideViewModel _timeOverrideViewModel = new();
        private readonly SettingsCdnViewModel _cdnViewModel = new();
        private readonly SettingsUpdateViewModel _updateViewModel;
        private readonly SettingsEpgDrawerViewModel _epgDrawerViewModel = new();
        private readonly SettingsTimeshiftDrawerViewModel _timeshiftDrawerViewModel = new();
        private readonly SettingsLogoDrawerViewModel _logoDrawerViewModel = new();
        private readonly SettingsRecordingDrawerViewModel _recordingDrawerViewModel = new();
        private readonly SettingsReplayDrawerViewModel _replayDrawerViewModel = new();
        private readonly SettingsSaveCoordinatorViewModel _saveCoordinator;
        private readonly SettingsWebDavActionsViewModel _webDavActionsViewModel;
        private readonly SettingsShellActionsViewModel _shellActionsViewModel = new();
        private readonly SettingsLegalActionsViewModel _legalActionsViewModel;
        private readonly SettingsUpdateActionsViewModel _updateActionsViewModel;
        private readonly SettingsCdnActionsViewModel _cdnActionsViewModel;
        private readonly SettingsTimePreviewActionsViewModel _timePreviewActionsViewModel;
        private readonly SettingsWindowUiActionsViewModel _windowUiActionsViewModel;
        
        private Controls.PlaybackDrawer? _playbackDrawer;
        private Controls.TimeshiftDrawer? _timeshiftDrawer;
        private Controls.EpgDrawer? _epgDrawer;
        private Controls.LogoDrawer? _logoDrawer;
        private Controls.RecordingDrawer? _recordingDrawer;
        
        private ReplayConfig _tempReplay;
        private TimeshiftConfig _tempTimeshift;
        private EpgConfig _tempEpg;
        private LogoConfig _tempLogo;
        private RecordingConfig _tempRecording;
        private HttpHeaderConfig _tempHttpHeader;
        
        // private FrameworkElement? _currentDrawer;

        public SettingsWindow(PlaybackSettings current)
        {
            InitializeComponent();
            PreviewKeyDown += Settings_PreviewKeyDown;
            try
            {
                var legalService = SrcBoxArchitectureHost.Kernel.Resolve<ISettingsLegalDocumentService>();
                var localizationService = SrcBoxArchitectureHost.Kernel.Resolve<ILocalizationService>();
                var webDavService = SrcBoxArchitectureHost.Kernel.Resolve<IWebDavSettingsService>();
                var updateService = SrcBoxArchitectureHost.Kernel.Resolve<IUpdateService>();
                var themeService = SrcBoxArchitectureHost.Kernel.Resolve<IThemeTitleBarService>();
                _legalDocumentsViewModel = new SettingsLegalDocumentsViewModel(legalService, localizationService);
                _webDavViewModel = new SettingsWebDavViewModel(localizationService, webDavService);
                _webDavActionsViewModel = new SettingsWebDavActionsViewModel(_webDavViewModel, localizationService);
                _legalActionsViewModel = new SettingsLegalActionsViewModel(_legalDocumentsViewModel, localizationService);
                _updateViewModel = new SettingsUpdateViewModel(updateService);
                _updateActionsViewModel = new SettingsUpdateActionsViewModel(_updateViewModel, localizationService);
                _windowUiActionsViewModel = new SettingsWindowUiActionsViewModel(_appearanceViewModel, _timeOverrideViewModel, themeService, localizationService);
            }
            catch
            {
                var localizationService = new ResourceLocalizationService();
                var webDavService = new WebDavSettingsService(localizationService);
                var themeService = new WindowsTitleBarThemeService();
                _legalDocumentsViewModel = new SettingsLegalDocumentsViewModel(
                    new SettingsLegalDocumentService(LibmpvIptvClient.Services.HttpClientService.Instance.Client),
                    localizationService);
                _webDavViewModel = new SettingsWebDavViewModel(localizationService, webDavService);
                _webDavActionsViewModel = new SettingsWebDavActionsViewModel(_webDavViewModel, localizationService);
                _legalActionsViewModel = new SettingsLegalActionsViewModel(_legalDocumentsViewModel, localizationService);
                _updateViewModel = new SettingsUpdateViewModel(new GithubUpdateService(LibmpvIptvClient.Services.HttpClientService.Instance.Client));
                _updateActionsViewModel = new SettingsUpdateActionsViewModel(_updateViewModel, localizationService);
                _windowUiActionsViewModel = new SettingsWindowUiActionsViewModel(_appearanceViewModel, _timeOverrideViewModel, themeService, localizationService);
            }
            _cdnActionsViewModel = new SettingsCdnActionsViewModel(_cdnViewModel);
            _timePreviewActionsViewModel = new SettingsTimePreviewActionsViewModel(_timeOverrideViewModel);
            _saveCoordinator = new SettingsSaveCoordinatorViewModel(
                _playbackViewModel,
                _timeOverrideViewModel,
                _webDavViewModel,
                _cdnViewModel,
                _appearanceViewModel);
            
            // Deep copy for temp configs
            _tempReplay = _replayDrawerViewModel.BuildTempConfig(current.Replay);
            _tempTimeshift = _timeshiftDrawerViewModel.BuildTempConfig(current.Timeshift);
            _tempEpg = _epgDrawerViewModel.BuildTempConfig(current.Epg);
            _tempLogo = _logoDrawerViewModel.BuildTempConfig(current.Logo);

            SetComboByTag(CbDecoder, current.Decoder);
            TbCacheSecs.Text = current.CacheSecs.ToString(CultureInfo.InvariantCulture);
            TbMaxBytes.Text = current.DemuxerMaxBytesMiB.ToString(CultureInfo.InvariantCulture);
            TbMaxBackBytes.Text = current.DemuxerMaxBackBytesMiB.ToString(CultureInfo.InvariantCulture);
            TbFccPrefetch.Text = current.FccPrefetchCount.ToString(CultureInfo.InvariantCulture);
            TbSourceTimeout.Text = current.SourceTimeoutSec.ToString(CultureInfo.InvariantCulture);

            // Deinterlace (反交错) - 三选一下拉框 + 算法下拉
            try
            {
                SetComboByTag(CbDeinterlaceMode, string.IsNullOrWhiteSpace(current.Deinterlace) ? "auto" : current.Deinterlace);
                SetComboByTag(CbDeinterlaceAlgo, string.IsNullOrWhiteSpace(current.DeinterlaceAlgorithm) ? "yadif" : current.DeinterlaceAlgorithm);
            }
            catch { }
            // 音频设置
            try
            {
                SliderVolumeGain.Value = current.VolumeGain;
                SliderVolumeMax.Value = current.VolumeMax;
                SliderAudioDelay.Value = current.AudioDelay;
                TxtVolumeGain.Text = $"{current.VolumeGain:0} dB";
                TxtVolumeMax.Text = $"{current.VolumeMax:0}%";
                TxtAudioDelay.Text = $"{current.AudioDelay:0.0} s";
            }
            catch { }
            // New playback options
            try
            {
                CbAdaptive.IsChecked = current.EnableProtocolAdaptive;
                CbHlsStart.IsChecked = current.HlsStartAtLiveEdge;
                TbHlsReadahead.Text = current.HlsReadaheadSecs.ToString(CultureInfo.InvariantCulture);
                TbAlang.Text = current.Alang ?? "";
                TbSlang.Text = current.Slang ?? "";
                TbMpvTimeout.Text = current.MpvNetworkTimeoutSec.ToString(CultureInfo.InvariantCulture);
            }
            catch { }

            // Load HTTP/RTSP Header settings
            _tempHttpHeader = new HttpHeaderConfig();
            try
            {
                var hh = current.HttpHeaders ?? new HttpHeaderConfig();
                _tempHttpHeader = new HttpHeaderConfig
                {
                    Headers = hh.Headers,
                    RtspUserAgent = hh.RtspUserAgent,
                    RtspUser = hh.RtspUser,
                    EncryptedRtspPassword = hh.EncryptedRtspPassword,
                    RtspTransport = hh.RtspTransport
                };
                TbHttpHeaders.Text = hh.Headers ?? "";
                // Set RTSP Transport combobox
                if (CbRtspTransport != null)
                {
                    var transport = string.IsNullOrWhiteSpace(hh.RtspTransport) ? "tcp" : hh.RtspTransport;
                    for (int i = 0; i < CbRtspTransport.Items.Count; i++)
                    {
                        var item = CbRtspTransport.Items[i] as ComboBoxItem;
                        if (item != null && string.Equals(item.Tag?.ToString(), transport, StringComparison.OrdinalIgnoreCase))
                        {
                            CbRtspTransport.SelectedIndex = i;
                            break;
                        }
                    }
                }
                TbRtspUserAgent.Text = hh.RtspUserAgent ?? "";
                TbRtspUser.Text = hh.RtspUser ?? "";
                // Password is not shown for security, it stays encrypted
            }
            catch { }

            try
            {
                // Init Controls (Lazy load not strictly needed for local controls, but we populate them now)
                // We reference the controls by x:Name defined in XAML
                _epgDrawerViewModel.LoadDrawer(EpgSettingsControl, _tempEpg);
                _logoDrawerViewModel.LoadDrawer(LogoSettingsControl, _tempLogo);
                _replayDrawerViewModel.LoadDrawer(ReplaySettingsControl, _tempReplay);
                _timeshiftDrawerViewModel.LoadDrawer(TimeshiftSettingsControl, _tempTimeshift);
                _tempRecording = _recordingDrawerViewModel.BuildTempConfig(current.Recording);
                _recordingDrawerViewModel.LoadDrawer(RecordingSettingsControl, _tempRecording);

                // Wire up pointers for HasChanges check if needed, or just check controls directly
                _epgDrawer = EpgSettingsControl;
                _logoDrawer = LogoSettingsControl;
                _playbackDrawer = ReplaySettingsControl;
                _timeshiftDrawer = TimeshiftSettingsControl;
                _recordingDrawer = RecordingSettingsControl;
                
                // Time Override UI init
                try
                {
                    var to = current.TimeOverride ?? new TimeOverrideConfig();
                    if (CbTimeOverrideEnabled != null) CbTimeOverrideEnabled.IsChecked = to.Enabled;
                    if (CbTimeOverrideMode != null)
                    {
                        CbTimeOverrideMode.SelectedIndex = string.Equals(to.Mode, "replace_all", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        _prevModeIndex = CbTimeOverrideMode.SelectedIndex;
                        CbTimeOverrideMode.SelectionChanged += (_, __) => OnTimeOverrideModeChanged();
                    }
                    if (CbTimeLayout != null)
                    {
                        if (string.Equals(to.Layout, "playseek", StringComparison.OrdinalIgnoreCase)) CbTimeLayout.SelectedIndex = 1;
                        else if (string.Equals(to.Layout, "start_duration", StringComparison.OrdinalIgnoreCase)) CbTimeLayout.SelectedIndex = 2;
                        else CbTimeLayout.SelectedIndex = 0;
                        CbTimeLayout.SelectionChanged += (_, __) => UpdateTimeOverrideKeyFieldsVisibility();
                    }
                    if (CbTimeEncoding != null)
                    {
                        if (string.Equals(to.Encoding, "utc", StringComparison.OrdinalIgnoreCase)) CbTimeEncoding.SelectedIndex = 1;
                        else if (string.Equals(to.Encoding, "unix", StringComparison.OrdinalIgnoreCase)) CbTimeEncoding.SelectedIndex = 2;
                        else if (string.Equals(to.Encoding, "unix_ms", StringComparison.OrdinalIgnoreCase)) CbTimeEncoding.SelectedIndex = 3;
                        else CbTimeEncoding.SelectedIndex = 0;
                    }
                    if (CbStartKey != null) CbStartKey.Text = to.StartKey ?? "start";
                    if (CbEndKey != null) CbEndKey.Text = to.EndKey ?? "end";
                    if (CbDurationKey != null) CbDurationKey.Text = to.DurationKey ?? "duration";
                    if (CbPlayseekKey != null) CbPlayseekKey.Text = to.PlayseekKey ?? "playseek";
                    if (CbUrlEncode != null) CbUrlEncode.IsChecked = to.UrlEncode;
                    UpdateTimeOverrideKeyFieldsVisibility();
                }
                catch { }

                // Web Remote settings
                try
                {
                    var wr = current.WebRemote ?? new WebRemoteConfig();
                    if (CbWebRemoteEnabled != null) CbWebRemoteEnabled.IsChecked = wr.Enabled;
                    if (TbWebRemotePort != null) TbWebRemotePort.Text = wr.HttpPort.ToString();
                    if (CbWebRemotePassword != null) CbWebRemotePassword.IsChecked = wr.RequirePassword;
                    if (TbWebRemotePassword != null) TbWebRemotePassword.Text = wr.Password ?? "";
                    if (CbWebRemoteShowChannels != null) CbWebRemoteShowChannels.IsChecked = wr.ShowChannelList;
                    if (CbWebRemoteShowEpg != null) CbWebRemoteShowEpg.IsChecked = wr.ShowEpgList;
                }
                catch { }
            }
            catch { }

            try
            {
                if (CbLanguage != null)
                {
                    CbLanguage.SelectedIndex = _appearanceViewModel.ResolveLanguageIndex(current.Language);
                }
                if (CbTheme != null)
                {
                    CbTheme.SelectedIndex = _appearanceViewModel.ResolveThemeIndex(current.ThemeMode);
                }
            }
            catch { }
            // CDN
            _cdnViewModel.Load(current.UpdateCdnMirrors);
            ListCdn.ItemsSource = _cdnViewModel.CdnList;
            
            if (TxtVersionInline != null) TxtVersionInline.Text = $"v{_updateViewModel.CurrentVersion}";
            _ = CheckUpdateInlineAsync(false);
            
            try
            {
                SetWebDavFields(current.WebDav);
            }
            catch { }
        }
        void SetWebDavFields(WebDavConfig? wd)
        {
            try
            {
                var state = _webDavViewModel.BuildFormState(wd);
                CbWebDavEnabled.IsChecked = state.Enabled;
                TbWebDavBase.Text = state.BaseUrl;
                TbWebDavUser.Text = state.Username;
                PbWebDavToken.Password = state.Token;
                CbWebDavSelfSigned.IsChecked = state.AllowSelfSignedCert;
                TbWebDavRoot.Text = state.RootPath;
                TbWebDavRec.Text = state.RecordingsPath;
                TbWebDavUserData.Text = state.UserDataPath;
            }
            catch { }
        }
        SettingsWebDavFormState ReadWebDavFormState()
        {
            return new SettingsWebDavFormState(
                CbWebDavEnabled?.IsChecked == true,
                TbWebDavBase?.Text ?? "",
                TbWebDavUser?.Text ?? "",
                PbWebDavToken?.Password ?? "",
                CbWebDavSelfSigned?.IsChecked == true,
                TbWebDavRoot?.Text ?? "/srcbox/",
                TbWebDavRec?.Text ?? "/srcbox/recordings/",
                TbWebDavUserData?.Text ?? "/srcbox/user-data/");
        }
        SettingsPlaybackFormState ReadPlaybackFormState()
        {
            return new SettingsPlaybackFormState(
                GetComboTag(CbDecoder, "auto"),
                TbCacheSecs?.Text ?? "0",
                TbMaxBytes?.Text ?? "1",
                TbMaxBackBytes?.Text ?? "0",
                TbFccPrefetch?.Text ?? "0",
                TbSourceTimeout?.Text ?? "30",
                CbAdaptive?.IsChecked == true,
                CbHlsStart?.IsChecked == true,
                TbHlsReadahead?.Text ?? "0",
                TbAlang?.Text ?? "",
                TbSlang?.Text ?? "",
                TbMpvTimeout?.Text ?? "0");
        }
        HttpHeaderConfig ReadHttpHeaderFormState()
        {
            var config = new HttpHeaderConfig
            {
                Headers = TbHttpHeaders?.Text ?? ""
            };
            // RTSP Transport
            if (CbRtspTransport?.SelectedItem is ComboBoxItem tItem)
                config.RtspTransport = tItem.Tag?.ToString() ?? "tcp";
            else
                config.RtspTransport = "tcp";
            config.RtspUserAgent = TbRtspUserAgent?.Text ?? "";
            config.RtspUser = TbRtspUser?.Text ?? "";
            // Password: only update if user entered a new one (not empty and different from placeholder)
            var pwd = PbRtspPassword?.Password ?? "";
            if (!string.IsNullOrWhiteSpace(pwd))
            {
                // New password entered, encrypt and store
                config.EncryptedRtspPassword = LibmpvIptvClient.Services.CryptoUtil.ProtectString(pwd);
            }
            else
            {
                // No new password, keep the original encrypted one
                config.EncryptedRtspPassword = _tempHttpHeader?.EncryptedRtspPassword ?? "";
            }
            return config;
        }
        SettingsTimeOverrideFormState ReadTimeOverrideFormState()
        {
            return new SettingsTimeOverrideFormState(
                CbTimeOverrideEnabled?.IsChecked == true,
                CbTimeOverrideMode?.SelectedIndex ?? 0,
                CbTimeLayout?.SelectedIndex ?? 0,
                CbTimeEncoding?.SelectedIndex ?? 0,
                (CbStartKey?.Text ?? "start").Trim(),
                (CbEndKey?.Text ?? "end").Trim(),
                (CbDurationKey?.Text ?? "duration").Trim(),
                (CbPlayseekKey?.Text ?? "playseek").Trim(),
                CbUrlEncode?.IsChecked == true);
        }
        void ReloadFromSettings(PlaybackSettings s)
        {
            try
            {
                // 基础播放设置
                var pState = _playbackViewModel.BuildFormState(s);
                SetComboByTag(CbDecoder, pState.Decoder);
                TbCacheSecs.Text = pState.CacheSecs;
                TbMaxBytes.Text = pState.MaxBytes;
                TbMaxBackBytes.Text = pState.MaxBackBytes;
                TbFccPrefetch.Text = pState.FccPrefetch;
                TbSourceTimeout.Text = pState.SourceTimeout;
                // 新增播放选项
                CbAdaptive.IsChecked = pState.EnableProtocolAdaptive;
                CbHlsStart.IsChecked = pState.HlsStartAtLiveEdge;
                TbHlsReadahead.Text = pState.HlsReadahead;
                TbAlang.Text = pState.Alang;
                TbSlang.Text = pState.Slang;
                TbMpvTimeout.Text = pState.MpvNetworkTimeout;
                // EPG/Logo/Replay/Timeshift 控件刷新
                _tempReplay = _replayDrawerViewModel.BuildTempConfig(s.Replay);
                _tempTimeshift = _timeshiftDrawerViewModel.BuildTempConfig(s.Timeshift);
                _tempEpg = _epgDrawerViewModel.BuildTempConfig(s.Epg);
                _tempLogo = _logoDrawerViewModel.BuildTempConfig(s.Logo);
                _epgDrawerViewModel.LoadDrawer(EpgSettingsControl, _tempEpg);
                _logoDrawerViewModel.LoadDrawer(LogoSettingsControl, _tempLogo);
                _replayDrawerViewModel.LoadDrawer(ReplaySettingsControl, _tempReplay);
                _timeshiftDrawerViewModel.LoadDrawer(TimeshiftSettingsControl, _tempTimeshift);
                _tempRecording = _recordingDrawerViewModel.BuildTempConfig(s.Recording);
                _recordingDrawerViewModel.LoadDrawer(RecordingSettingsControl, _tempRecording);
                // 时间覆盖 UI
                var toState = _timeOverrideViewModel.BuildFormState(s.TimeOverride);
                if (CbTimeOverrideEnabled != null) CbTimeOverrideEnabled.IsChecked = toState.Enabled;
                if (CbTimeOverrideMode != null)
                {
                    CbTimeOverrideMode.SelectedIndex = toState.ModeIndex;
                    _prevModeIndex = toState.ModeIndex;
                }
                if (CbTimeLayout != null)
                {
                    CbTimeLayout.SelectedIndex = toState.LayoutIndex;
                    UpdateTimeOverrideKeyFieldsVisibility();
                }
                if (CbTimeEncoding != null)
                {
                    CbTimeEncoding.SelectedIndex = toState.EncodingIndex;
                }
                if (CbStartKey != null) CbStartKey.Text = toState.StartKey;
                if (CbEndKey != null) CbEndKey.Text = toState.EndKey;
                if (CbDurationKey != null) CbDurationKey.Text = toState.DurationKey;
                if (CbPlayseekKey != null) CbPlayseekKey.Text = toState.PlayseekKey;
                if (CbUrlEncode != null) CbUrlEncode.IsChecked = toState.UrlEncode;
                // 语言与主题
                if (CbLanguage != null)
                {
                    CbLanguage.SelectedIndex = _appearanceViewModel.ResolveLanguageIndex(s.Language);
                }
                if (CbTheme != null)
                {
                    CbTheme.SelectedIndex = _appearanceViewModel.ResolveThemeIndex(s.ThemeMode);
                }
                // CDN 列表
                _cdnViewModel.Load(s.UpdateCdnMirrors);
                ListCdn.ItemsSource = _cdnViewModel.CdnList;
                // WebDAV
                SetWebDavFields(s.WebDav);
            }
            catch { }
        }
        void Settings_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (_windowUiActionsViewModel.ShouldTriggerDebug(e.Key))
                {
                    _shellActionsViewModel.RaiseDebug(DebugRequested);
                    e.Handled = true;
                }
            }
            catch { }
        }
        
        int _prevModeIndex = 0;
        void OnTimeOverrideModeChanged()
        {
            try
            {
                if (CbTimeOverrideMode == null) return;
                var idx = CbTimeOverrideMode.SelectedIndex;
                var prompt = _windowUiActionsViewModel.BuildModeChangePrompt(idx);
                if (prompt.ShouldConfirm)
                {
                    var ok = ModernMessageBox.Show(this, prompt.Message, prompt.Title, MessageBoxButton.YesNo) == true;
                    if (!ok)
                    {
                        CbTimeOverrideMode.SelectedIndex = _prevModeIndex;
                        return;
                    }
                }
                _prevModeIndex = idx;
            }
            catch { }
        }
        void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Save from Controls to Temp Configs
            _replayDrawerViewModel.SaveDrawer(_playbackDrawer, _tempReplay);
            _timeshiftDrawerViewModel.SaveDrawer(_timeshiftDrawer, _tempTimeshift);
            _epgDrawerViewModel.SaveDrawer(_epgDrawer, _tempEpg);
            _logoDrawerViewModel.SaveDrawer(_logoDrawer, _tempLogo);
            _recordingDrawerViewModel.SaveDrawer(_recordingDrawer, _tempRecording);

            PlaybackSettings s;
            try
            {
                s = _saveCoordinator.BuildSettings(
                    ReadPlaybackFormState(),
                    ReadTimeOverrideFormState(),
                    ReadWebDavFormState(),
                    CbLanguage?.SelectedIndex ?? 0,
                    CbTheme?.SelectedIndex ?? 0,
                    _tempReplay,
                    _tempTimeshift,
                    _tempEpg,
                    _tempLogo,
                    _tempRecording);
            }
            catch
            {
                s = new PlaybackSettings();
            }
            // Save HTTP/RTSP Header settings
            s.HttpHeaders = ReadHttpHeaderFormState();

            // Deinterlace (反交错) - 三选一 + 算法
            try
            {
                s.Deinterlace = GetComboTag(CbDeinterlaceMode, "auto");
                s.DeinterlaceAlgorithm = GetComboTag(CbDeinterlaceAlgo, "yadif");
                // 场序固定为 auto（与 mpv 行为一致；如需更细粒度可后续扩展 UI）
                s.DeinterlaceFieldParity = "auto";
            }
            catch { }

            // 音频设置
            try
            {
                s.VolumeGain = SliderVolumeGain.Value;
                s.VolumeMax = (int)SliderVolumeMax.Value;
                s.AudioDelay = SliderAudioDelay.Value;
            }
            catch { }

            // Web Remote settings
            try
            {
                s.WebRemote = new WebRemoteConfig
                {
                    Enabled = CbWebRemoteEnabled?.IsChecked == true,
                    HttpPort = int.TryParse(TbWebRemotePort?.Text, out var port) ? port : 8899,
                    RequirePassword = CbWebRemotePassword?.IsChecked == true,
                    Password = TbWebRemotePassword?.Text ?? "",
                    ShowChannelList = CbWebRemoteShowChannels?.IsChecked == true,
                    ShowEpgList = CbWebRemoteShowEpg?.IsChecked == true
                };
            }
            catch { }

            Result = s;
            ApplySettingsRequested?.Invoke(s);

            ModernMessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_SettingsSaved", "设置已保存"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK);
            // DialogResult = true;
            // Close();
        }
        async void BtnWebDavTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await _webDavActionsViewModel.TestConnectionAsync(ReadWebDavFormState());
                ModernMessageBox.Show(this, result.Message, result.Title, MessageBoxButton.OK);
            }
            catch { }
        }
        async void BtnTestHttp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = TbTestUrl?.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(url))
                {
                    ModernMessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_TestUrlRequired", "请输入测试URL"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK);
                    return;
                }
                if (BtnTestHttp != null)
                {
                    BtnTestHttp.IsEnabled = false;
                    BtnTestRtsp.IsEnabled = false;
                }
                try
                {
                    // Apply current HTTP headers to temp settings for testing
                    var testSettings = AppSettings.Current;
                    testSettings.HttpHeaders = ReadHttpHeaderFormState();

                    using var player = new LibmpvIptvClient.MpvInterop();
                    player.Create();
                    player.SetSettings(testSettings);
                    player.Initialize();

                    // Show mpv console for testing
                    player.SetString("terminal", "yes");
                    player.SetString("msg-level", "all=debug");
                    player.LoadFile(url);
                    await System.Threading.Tasks.Task.Delay(3000);
                    ModernMessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_TestHttpStarted", "HTTP测试已启动，请查看播放器的调试日志"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK);
                }
                finally
                {
                    if (BtnTestHttp != null) BtnTestHttp.IsEnabled = true;
                    if (BtnTestRtsp != null) BtnTestRtsp.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show(this, $"{LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Error", "错误")}: {ex.Message}", LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK);
            }
        }
        async void BtnTestRtsp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = TbTestUrl?.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(url))
                {
                    ModernMessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_TestUrlRequired", "请输入测试URL"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK);
                    return;
                }
                if (BtnTestRtsp != null)
                {
                    BtnTestRtsp.IsEnabled = false;
                    BtnTestHttp.IsEnabled = false;
                }
                try
                {
                    // Apply current HTTP headers to temp settings for testing
                    var testSettings = AppSettings.Current;
                    testSettings.HttpHeaders = ReadHttpHeaderFormState();

                    using var player = new LibmpvIptvClient.MpvInterop();
                    player.Create();
                    player.SetSettings(testSettings);
                    player.Initialize();

                    // Show mpv console for testing
                    player.SetString("terminal", "yes");
                    player.SetString("msg-level", "all=debug");
                    player.LoadFile(url);
                    await System.Threading.Tasks.Task.Delay(3000);
                    ModernMessageBox.Show(this, LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_TestRtspStarted", "RTSP测试已启动，请查看播放器的调试日志"), LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK);
                }
                finally
                {
                    if (BtnTestRtsp != null) BtnTestRtsp.IsEnabled = true;
                    if (BtnTestHttp != null) BtnTestHttp.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                ModernMessageBox.Show(this, $"{LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Error", "错误")}: {ex.Message}", LibmpvIptvClient.Helpers.ResxLocalizer.Get("Common_Tips", "提示"), MessageBoxButton.OK);
            }
        }
        async void BtnWebDavBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await _webDavActionsViewModel.BackupUserDataAsync(ReadWebDavFormState(), AppDomain.CurrentDomain.BaseDirectory);
                ModernMessageBox.Show(this, result.Message, result.Title, MessageBoxButton.OK);
            }
            catch { }
        }
        async void BtnWebDavRestore_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                var confirmInfo = _webDavActionsViewModel.BuildRestoreConfirmation(dir);
                if (confirmInfo.ShouldConfirm)
                {
                    bool? confirm = ModernMessageBox.Show(this, confirmInfo.Message, confirmInfo.Title, MessageBoxButton.YesNo);
                    if (confirm != true) return;
                }
                var result = await _webDavActionsViewModel.RestoreUserDataAsync(ReadWebDavFormState(), dir);
                ModernMessageBox.Show(this, result.Message, result.Title, MessageBoxButton.OK);
                try
                {
                    if (result.ShouldReload)
                    {
                        var reloaded = PlaybackSettings.Load();
                        AppSettings.Current = reloaded;
                        ReloadFromSettings(reloaded);
                        ApplySettingsRequested?.Invoke(reloaded);
                    }
                }
                catch { }
            }
            catch { }
        }
        async void BtnWebDavTestRecordingsWrite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await _webDavActionsViewModel.TestRecordingsWriteAsync(ReadWebDavFormState());
                ModernMessageBox.Show(this, result.Message, result.Title, MessageBoxButton.OK);
            }
            catch { }
        }
        
        void BtnPreviewReplay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var baseUrl = TbBaseUrl?.Text?.Trim() ?? "";
                var result = _timePreviewActionsViewModel.BuildPreview(baseUrl, ReadTimeOverrideFormState(), false);
                if (TbPreviewResult != null) TbPreviewResult.Text = result.PreviewUrl;
                if (TbPreviewTemplate != null) TbPreviewTemplate.Text = result.PreviewTemplate;
            }
            catch { }
        }
        void BtnPreviewTimeshift_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var baseUrl = TbBaseUrl?.Text?.Trim() ?? "";
                var result = _timePreviewActionsViewModel.BuildPreview(baseUrl, ReadTimeOverrideFormState(), true);
                if (TbPreviewResult != null) TbPreviewResult.Text = result.PreviewUrl;
                if (TbPreviewTemplate != null) TbPreviewTemplate.Text = result.PreviewTemplate;
            }
            catch { }
        }

        void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }
        }
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                if (Owner != null)
                {
                    Owner.Activate();
                }
            }
            catch { }
        }
        void BtnOpenDocs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _shellActionsViewModel.OpenDocs();
            }
            catch { }
        }
        void BtnOpenIssues_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _shellActionsViewModel.OpenIssues();
            }
            catch { }
        }
        
        void UpdateTimeOverrideKeyFieldsVisibility()
        {
            try
            {
                var layoutIndex = CbTimeLayout?.SelectedIndex ?? 0;
                var visibility = _windowUiActionsViewModel.BuildTimeOverrideVisibility(layoutIndex);
                
                if (CbStartKey != null) CbStartKey.Visibility = visibility.ShowStart ? Visibility.Visible : Visibility.Collapsed;
                if (CbEndKey != null) CbEndKey.Visibility = visibility.ShowEnd ? Visibility.Visible : Visibility.Collapsed;
                if (CbDurationKey != null) CbDurationKey.Visibility = visibility.ShowDuration ? Visibility.Visible : Visibility.Collapsed;
                if (SpPlayseekRow != null) SpPlayseekRow.Visibility = visibility.ShowPlayseek ? Visibility.Visible : Visibility.Collapsed;
                if (CbPlayseekKey != null) CbPlayseekKey.Visibility = visibility.ShowPlayseek ? Visibility.Visible : Visibility.Collapsed;
                
                // 对应标签同步隐藏/显示
                if (LblStartKey != null) LblStartKey.Visibility = visibility.ShowStart ? Visibility.Visible : Visibility.Collapsed;
                if (LblEndKey != null) LblEndKey.Visibility = visibility.ShowEnd ? Visibility.Visible : Visibility.Collapsed;
                if (LblDurationKey != null) LblDurationKey.Visibility = visibility.ShowDuration ? Visibility.Visible : Visibility.Collapsed;
                if (LblPlayseekKey != null) LblPlayseekKey.Visibility = visibility.ShowPlayseek ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }
        


        void BtnDebug_Click(object sender, RoutedEventArgs e)
        {
            _shellActionsViewModel.RaiseDebug(DebugRequested);
        }
        void BtnOpenAbout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = this.Owner ?? this;
                var dlg = _shellActionsViewModel.CreateAboutDialog(owner, this.Topmost);
                dlg.ShowDialog();
            }
            catch { }
        }
        async void BtnCheckUpdateInline_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BtnCheckUpdateInline != null)
                {
                    BtnCheckUpdateInline.IsEnabled = false;
                    var old = BtnCheckUpdateInline.Content;
                    BtnCheckUpdateInline.Content = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Checking", "检查中…");
                    try { await CheckUpdateInlineAsync(true); }
                    finally
                    {
                        BtnCheckUpdateInline.Content = old;
                        BtnCheckUpdateInline.IsEnabled = true;
                    }
                }
                else
                {
                    await CheckUpdateInlineAsync(true);
                }
            }
            catch { }
        }

        private void OpenDrawer()
        {
            // Drawer logic removed
        }

        private void CloseDrawer()
        {
            // Drawer logic removed
        }

        private void DrawerOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            // Drawer logic removed
        }
/*
        async void BtnViewUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BtnViewUpdate != null)
                {
                    BtnViewUpdate.IsEnabled = false;
                    var old = BtnViewUpdate.Content;
                    BtnViewUpdate.Content = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Opening", "打开中…");
                    try
                    {
                        var latest = _updateViewModel.GetLatestRelease();
                        if (latest == null)
                        {
                            await CheckUpdateInlineAsync(true);
                            return;
                        }
                        var dlg = new UpdateDialog(latest);
                        dlg.Owner = this;
                        dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        dlg.Topmost = this.Topmost;
                        dlg.ShowDialog();
                    }
                    finally
                    {
                        BtnViewUpdate.Content = old;
                        BtnViewUpdate.IsEnabled = true;
                    }
                    return;
                }
                
                var latest2 = _updateViewModel.GetLatestRelease();
                if (latest2 == null)
                {
                    await CheckUpdateInlineAsync(true);
                    return;
                }
                var dlg2 = new UpdateDialog(latest2);
                dlg2.Owner = this;
                dlg2.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dlg2.Topmost = this.Topmost;
                dlg2.ShowDialog();
            }
            catch { }
        }
*/
        void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.ClickCount == 1)
            {
                try { DragMove(); } catch { }
            }
        }
        void BtnCloseTitle_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        public event Action? DebugRequested;

        // CDN 管理交互
        void BtnCdnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (TbNewCdn != null)
            {
                TbNewCdn.Text = _cdnActionsViewModel.AddFromInput(TbNewCdn.Text);
            }
        }
        void BtnCdnRemove_Click(object sender, RoutedEventArgs e)
        {
            _cdnActionsViewModel.RemoveSelected(ListCdn.SelectedItems);
        }
        void BtnCdnUp_Click(object sender, RoutedEventArgs e)
        {
            _cdnActionsViewModel.MoveUp(ListCdn.SelectedItem);
        }
        void BtnCdnDown_Click(object sender, RoutedEventArgs e)
        {
            _cdnActionsViewModel.MoveDown(ListCdn.SelectedItem);
        }
        async void BtnCdnSpeedTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _cdnActionsViewModel.RunSpeedTestAsync();
            }
            catch { }
        }
        void Inline_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try { _shellActionsViewModel.OpenUrl(e.Uri.AbsoluteUri); } catch { }
        }
        async void BtnLicense_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                object? oldContent = null;
                if (BtnLicense != null)
                {
                    oldContent = BtnLicense.Content;
                    var loading = _legalActionsViewModel.CreateLoadingState(oldContent);
                    BtnLicense.Content = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Loading", "加载中…");
                    BtnLicense.IsEnabled = loading.IsButtonEnabled;
                }
                var result = await _legalActionsViewModel.LoadLicenseAsync();
                if (BtnLicense != null)
                {
                    BtnLicense.Content = oldContent;
                    BtnLicense.IsEnabled = true;
                }
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    ModernMessageBox.Show(this, result.ErrorMessage, result.TipsTitle, MessageBoxButton.OK);
                    return;
                }
                if (result.ShouldShowDialog)
                {
                    var dlg = new TextViewerDialog(result.DialogTitle, result.DialogContent)
                    {
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Topmost = this.Topmost
                    };
                    dlg.ShowDialog();
                }
            }
            catch { }
        }
        async void BtnThirdParty_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                object? oldContent = null;
                if (BtnThirdParty != null)
                {
                    oldContent = BtnThirdParty.Content;
                    var loading = _legalActionsViewModel.CreateLoadingState(oldContent);
                    BtnThirdParty.Content = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Msg_Loading", "加载中…");
                    BtnThirdParty.IsEnabled = loading.IsButtonEnabled;
                }
                var result = await _legalActionsViewModel.LoadThirdPartyAsync();
                if (BtnThirdParty != null)
                {
                    BtnThirdParty.Content = oldContent;
                    BtnThirdParty.IsEnabled = true;
                }
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    ModernMessageBox.Show(this, result.ErrorMessage, result.TipsTitle, MessageBoxButton.OK);
                    return;
                }
                if (result.ShouldShowDialog)
                {
                    var dlg = new TextViewerDialog(result.DialogTitle, result.DialogContent)
                    {
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Topmost = this.Topmost
                    };
                    dlg.ShowDialog();
                }
            }
            catch { }
        }
        async System.Threading.Tasks.Task CheckUpdateInlineAsync(bool interactive)
        {
            try
            {
                var result = await _updateActionsViewModel.CheckUpdateAsync(interactive);
                if (result.ShowBadge)
                {
                    if (BadgeNewInline != null) BadgeNewInline.Visibility = Visibility.Visible;
                }
                if (result.ShouldPromptUpdate)
                {
                    var r = ModernMessageBox.Show(this, result.PromptMessage, result.Title, MessageBoxButton.YesNo);
                    if (r == true && result.ReleaseInfo != null)
                    {
                        try
                        {
                            var dlg = new UpdateDialog(result.ReleaseInfo);
                            dlg.Owner = this;
                            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            dlg.Topmost = this.Topmost;
                            dlg.ShowDialog();
                        }
                        catch { }
                    }
                    return;
                }
                if (result.ShouldShowInfo)
                {
                    ModernMessageBox.Show(this, result.InfoMessage, result.Title, MessageBoxButton.OK);
                }
            }
            catch { }
        }

        // ---- Deinterlace (反交错) 设置 ----

        /// <summary>
        /// 根据 ComboBox 的 SelectedItem.Tag 返回字符串值，缺省返回 fallback
        /// </summary>
        private static string GetComboTag(System.Windows.Controls.ComboBox? cb, string fallback)
        {
            try
            {
                if (cb == null) return fallback;
                if (cb.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string s && !string.IsNullOrEmpty(s))
                    return s;
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// 根据 tag 值选中 ComboBox 对应项，若无匹配保留当前
        /// </summary>
        private static void SetComboByTag(System.Windows.Controls.ComboBox? cb, string tag)
        {
            try
            {
                if (cb == null || string.IsNullOrEmpty(tag)) return;
                foreach (var obj in cb.Items)
                {
                    if (obj is System.Windows.Controls.ComboBoxItem item &&
                        string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        cb.SelectedItem = item;
                        return;
                    }
                }
            }
            catch { }
        }

        private void CbDeinterlaceMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                var mode = GetComboTag(CbDeinterlaceMode, "auto");
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[Deinterlace] 设置页变更模式: {mode}");
                // 立即生效：仅预览，不保存（用户在 BtnSave 时才落盘）
                _previewPlayerEngine?.SetDeinterlace(
                    mode,
                    "auto",
                    GetComboTag(CbDeinterlaceAlgo, "yadif"));
            }
            catch { }
        }

        private void CbDeinterlaceAlgo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                var algo = GetComboTag(CbDeinterlaceAlgo, "yadif");
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[Deinterlace] 设置页变更算法: {algo}");
                _previewPlayerEngine?.SetDeinterlace(
                    GetComboTag(CbDeinterlaceMode, "auto"),
                    "auto",
                    algo);
            }
            catch { }
        }

        private void SliderVolumeGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtVolumeGain == null) return;
            TxtVolumeGain.Text = $"{e.NewValue:0} dB";
        }

        private void SliderVolumeMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtVolumeMax == null) return;
            TxtVolumeMax.Text = $"{e.NewValue:0}%";
        }

        private void SliderAudioDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtAudioDelay == null) return;
            TxtAudioDelay.Text = $"{e.NewValue:0.0} s";
        }

        // 预览引擎引用（由 MainWindow 在打开设置时注入，避免循环依赖）
        private Architecture.Application.Player.IPlayerEngine? _previewPlayerEngine;
        public void SetPreviewPlayerEngine(Architecture.Application.Player.IPlayerEngine? engine)
        {
            _previewPlayerEngine = engine;
        }

        private void ListCdn_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox lb && NetworkScrollViewer != null)
            {
                var sv = GetScrollViewer(lb);
                bool shouldHandle = false;
                if (sv == null || sv.ScrollableHeight <= 0)
                {
                    shouldHandle = true;
                }
                else
                {
                    double offset = sv.VerticalOffset;
                    bool atTop = offset <= 0;
                    bool atBottom = offset >= sv.ScrollableHeight - 1;
                    if ((e.Delta > 0 && atTop) || (e.Delta < 0 && atBottom))
                        shouldHandle = true;
                }

                if (shouldHandle)
                {
                    NetworkScrollViewer.ScrollToVerticalOffset(NetworkScrollViewer.VerticalOffset - e.Delta);
                    e.Handled = true;
                }
            }
        }

        private static System.Windows.Controls.ScrollViewer? GetScrollViewer(System.Windows.DependencyObject parent)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is System.Windows.Controls.ScrollViewer sv)
                    return sv;
                if (GetScrollViewer(child) is System.Windows.Controls.ScrollViewer found)
                    return found;
            }
            return null;
        }

    }
}
