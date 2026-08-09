using System;
using System.Windows;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowSettingsManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;
        private readonly MainWindowOverlayManager _overlayManager;

        public MainWindowSettingsManager(MainWindow window, MainShellViewModel shell, MainWindowOverlayManager overlayManager)
        {
            _window = window;
            _shell = shell;
            _overlayManager = overlayManager;
        }

        public void OpenSettings(int tabIndex = 0)
        {
            var owner = (_shell.WindowStateActions.IsFullscreen && _shell.WindowStateActions.FullscreenWindow != null) ? (Window)_shell.WindowStateActions.FullscreenWindow : _window;
            try
            {
                foreach (Window w in System.Windows.Application.Current.Windows)
                {
                    if (w is SettingsWindow existing)
                    {
                        try { existing.Owner = owner; } catch { }
                        try { existing.SelectTab(tabIndex); } catch { }
                        try { existing.Activate(); existing.Topmost = existing.Topmost; } catch { }
                        return;
                    }
                }
            }
            catch { }
            var dlg = new SettingsWindow(AppSettings.Current) { Owner = owner };
            dlg.DebugRequested += () => _window.OpenDebugWindowFromManager();
            try { dlg.Topmost = _shell.WindowStateActions.IsFullscreen; } catch { }

            // 注入播放器引擎引用，用于反交错"预览即生效"
            try { dlg.SetPreviewPlayerEngine(_shell.PlayerEngine); } catch { }

            dlg.ApplySettingsRequested += ApplySettings;
            try { dlg.SelectTab(tabIndex); } catch { }
            dlg.Show();
        }

        private void ApplySettings(PlaybackSettings settings)
        {
            double? resumePos = null;
            string? resumeUrl = null;
            bool wasTimeshift = _shell.IsTimeshiftActive;
            bool wasReplay = _shell.CurrentPlayingProgram != null;
            var old = AppSettings.Current;
            bool mpvWillChange =
                !string.Equals(old.Decoder, settings.Decoder, StringComparison.OrdinalIgnoreCase)
                || old.CacheSecs != settings.CacheSecs
                || old.DemuxerMaxBytesMiB != settings.DemuxerMaxBytesMiB
                || old.DemuxerMaxBackBytesMiB != settings.DemuxerMaxBackBytesMiB
                || old.EnableProtocolAdaptive != settings.EnableProtocolAdaptive
                || old.HlsStartAtLiveEdge != settings.HlsStartAtLiveEdge
                || old.HlsReadaheadSecs != settings.HlsReadaheadSecs
                || (old.Alang ?? "") != (settings.Alang ?? "")
                || (old.Slang ?? "") != (settings.Slang ?? "")
                || old.MpvNetworkTimeoutSec != settings.MpvNetworkTimeoutSec
                || !string.Equals(old.Deinterlace, settings.Deinterlace, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(old.DeinterlaceFieldParity, settings.DeinterlaceFieldParity, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(old.DeinterlaceAlgorithm, settings.DeinterlaceAlgorithm, StringComparison.OrdinalIgnoreCase);
            try
            {
                if (_window.PlayerInterop != null)
                {
                    resumePos = _window.PlayerInterop.GetTimePos();
                    resumeUrl = _shell.CurrentUrl;
                }
            }
            catch { }
            AppSettings.Current.Decoder = settings.Decoder;
            AppSettings.Current.CacheSecs = settings.CacheSecs;
            AppSettings.Current.DemuxerMaxBytesMiB = settings.DemuxerMaxBytesMiB;
            AppSettings.Current.DemuxerMaxBackBytesMiB = settings.DemuxerMaxBackBytesMiB;
            AppSettings.Current.FccPrefetchCount = settings.FccPrefetchCount;
            AppSettings.Current.SourceTimeoutSec = settings.SourceTimeoutSec;
            AppSettings.Current.CustomEpgUrl = settings.CustomEpgUrl;
            AppSettings.Current.CustomLogoUrl = settings.CustomLogoUrl;
            AppSettings.Current.TimeshiftHours = settings.TimeshiftHours;
            AppSettings.Current.UpdateCdnMirrors = settings.UpdateCdnMirrors;
            AppSettings.Current.EnableProtocolAdaptive = settings.EnableProtocolAdaptive;
            AppSettings.Current.HlsStartAtLiveEdge = settings.HlsStartAtLiveEdge;
            AppSettings.Current.HlsReadaheadSecs = settings.HlsReadaheadSecs;
            AppSettings.Current.Alang = settings.Alang ?? "";
            AppSettings.Current.Slang = settings.Slang ?? "";
            AppSettings.Current.MpvNetworkTimeoutSec = settings.MpvNetworkTimeoutSec;
            AppSettings.Current.VolumeGain = settings.VolumeGain;
            AppSettings.Current.VolumeMax = settings.VolumeMax;
            AppSettings.Current.Volume = settings.Volume;
            AppSettings.Current.AudioDelay = settings.AudioDelay;
            AppSettings.Current.EnableChannelPreview = settings.EnableChannelPreview;
            AppSettings.Current.Deinterlace = settings.Deinterlace;
            AppSettings.Current.DeinterlaceFieldParity = settings.DeinterlaceFieldParity;
            AppSettings.Current.DeinterlaceAlgorithm = settings.DeinterlaceAlgorithm;
            try
            {
                if (settings.Replay != null)
                {
                    AppSettings.Current.Replay.Enabled = settings.Replay.Enabled;
                    AppSettings.Current.Replay.UrlFormat = settings.Replay.UrlFormat ?? "";
                    AppSettings.Current.Replay.DurationHours = settings.Replay.DurationHours;
                }
                if (settings.Timeshift != null)
                {
                    AppSettings.Current.Timeshift.Enabled = settings.Timeshift.Enabled;
                    AppSettings.Current.Timeshift.UrlFormat = settings.Timeshift.UrlFormat ?? "";
                    AppSettings.Current.Timeshift.DurationHours = settings.Timeshift.DurationHours;
                }
                if (settings.Logo != null)
                {
                    AppSettings.Current.Logo.Enabled = settings.Logo.Enabled;
                    AppSettings.Current.Logo.Url = settings.Logo.Url ?? "";
                    AppSettings.Current.Logo.EnableCache = settings.Logo.EnableCache;
                    AppSettings.Current.Logo.CacheDir = settings.Logo.CacheDir ?? "";
                    AppSettings.Current.Logo.CacheTtlHours = settings.Logo.CacheTtlHours;
                    AppSettings.Current.Logo.CacheMaxMiB = settings.Logo.CacheMaxMiB;
                }
                if (settings.Epg != null)
                {
                    AppSettings.Current.Epg.Enabled = settings.Epg.Enabled;
                    AppSettings.Current.Epg.Url = settings.Epg.Url ?? "";
                    AppSettings.Current.Epg.RefreshIntervalHours = settings.Epg.RefreshIntervalHours;
                    AppSettings.Current.Epg.EnableSmartMatch = settings.Epg.EnableSmartMatch;
                    AppSettings.Current.Epg.StrictMatchByPlaybackTime = settings.Epg.StrictMatchByPlaybackTime;
                }
            }
            catch { }
            AppSettings.Current.Language = settings.Language;
            AppSettings.Current.ThemeMode = settings.ThemeMode;
            try
            {
                if (settings.WebDav != null)
                {
                    AppSettings.Current.WebDav.Enabled = settings.WebDav.Enabled;
                    AppSettings.Current.WebDav.BaseUrl = settings.WebDav.BaseUrl ?? "";
                    AppSettings.Current.WebDav.Username = settings.WebDav.Username ?? "";
                    AppSettings.Current.WebDav.TokenOrPassword = settings.WebDav.TokenOrPassword ?? "";
                    AppSettings.Current.WebDav.EncryptedToken = settings.WebDav.EncryptedToken ?? "";
                    AppSettings.Current.WebDav.AllowSelfSignedCert = settings.WebDav.AllowSelfSignedCert;
                    AppSettings.Current.WebDav.RootPath = settings.WebDav.RootPath ?? "/srcbox/";
                    AppSettings.Current.WebDav.RecordingsPath = settings.WebDav.RecordingsPath ?? "/srcbox/recordings/";
                    AppSettings.Current.WebDav.UserDataPath = settings.WebDav.UserDataPath ?? "/srcbox/user-data/";
                }
            }
            catch { }
            // Web Remote settings
            try
            {
                if (settings.WebRemote != null)
                {
                    AppSettings.Current.WebRemote.Enabled = settings.WebRemote.Enabled;
                    AppSettings.Current.WebRemote.HttpPort = settings.WebRemote.HttpPort;
                    AppSettings.Current.WebRemote.RequirePassword = settings.WebRemote.RequirePassword;
                    AppSettings.Current.WebRemote.Password = settings.WebRemote.Password ?? "";
                    AppSettings.Current.WebRemote.ShowChannelList = settings.WebRemote.ShowChannelList;
                    AppSettings.Current.WebRemote.ShowEpgList = settings.WebRemote.ShowEpgList;
                    AppSettings.Current.WebRemote.MaxEpgItems = settings.WebRemote.MaxEpgItems;
                }
                // Restart Web Remote server if settings changed
                LibmpvIptvClient.Services.WebRemote.WebRemoteManager.RestartIfNeeded();
            }
            catch { }
            // HTTP/RTSP Header settings
            try
            {
                if (settings.HttpHeaders != null)
                {
                    AppSettings.Current.HttpHeaders.Headers = settings.HttpHeaders.Headers ?? "";
                    AppSettings.Current.HttpHeaders.RtspUserAgent = settings.HttpHeaders.RtspUserAgent ?? "";
                    AppSettings.Current.HttpHeaders.RtspUser = settings.HttpHeaders.RtspUser ?? "";
                    AppSettings.Current.HttpHeaders.EncryptedRtspPassword = settings.HttpHeaders.EncryptedRtspPassword ?? "";
                    AppSettings.Current.HttpHeaders.RtspTransport = settings.HttpHeaders.RtspTransport ?? "tcp";
                }
            }
            catch { }
            // Recording settings
            try
            {
                if (settings.Recording != null)
                {
                    AppSettings.Current.Recording.Enabled = settings.Recording.Enabled;
                    AppSettings.Current.Recording.DefaultPlayChoice = settings.Recording.DefaultPlayChoice ?? "prompt";
                    AppSettings.Current.Recording.LastPlayChoice = settings.Recording.LastPlayChoice ?? "";
                    AppSettings.Current.Recording.SaveMode = settings.Recording.SaveMode ?? "local_then_upload";
                    AppSettings.Current.Recording.DirTemplate = settings.Recording.DirTemplate ?? "recordings/{channel}";
                    AppSettings.Current.Recording.FileTemplate = settings.Recording.FileTemplate ?? "{yyyyMMdd_HHmmss}.ts";
                    AppSettings.Current.Recording.VerifyDirReady = settings.Recording.VerifyDirReady;
                    AppSettings.Current.Recording.GrowthTimeoutSec = settings.Recording.GrowthTimeoutSec;
                    AppSettings.Current.Recording.RetryCount = settings.Recording.RetryCount;
                    AppSettings.Current.Recording.UploadMaxConcurrency = settings.Recording.UploadMaxConcurrency;
                    AppSettings.Current.Recording.UploadRetry = settings.Recording.UploadRetry;
                    AppSettings.Current.Recording.UploadRetryBackoffMs = settings.Recording.UploadRetryBackoffMs;
                    AppSettings.Current.Recording.UploadMaxKBps = settings.Recording.UploadMaxKBps;
                    AppSettings.Current.Recording.ResumeUpload = settings.Recording.ResumeUpload;
                    AppSettings.Current.Recording.RealtimeUploadIntervalSec = settings.Recording.RealtimeUploadIntervalSec;
                    AppSettings.Current.Recording.RemoteTempSuffix = settings.Recording.RemoteTempSuffix ?? ".part";
                    AppSettings.Current.Recording.RealtimeFinalizeEnabled = settings.Recording.RealtimeFinalizeEnabled;
                    AppSettings.Current.Recording.RealtimeFinalizeDelaySec = settings.Recording.RealtimeFinalizeDelaySec;
                    AppSettings.Current.Recording.RealtimeFinalizeMaxKBps = settings.Recording.RealtimeFinalizeMaxKBps;
                }
            }
            catch { }
            try
            {
                if (settings.TimeOverride != null)
                {
                    if (AppSettings.Current.TimeOverride == null)
                        AppSettings.Current.TimeOverride = new TimeOverrideConfig();
                    AppSettings.Current.TimeOverride.Enabled = settings.TimeOverride.Enabled;
                    AppSettings.Current.TimeOverride.Mode = settings.TimeOverride.Mode ?? "time_only";
                    AppSettings.Current.TimeOverride.Layout = settings.TimeOverride.Layout ?? "start_end";
                    AppSettings.Current.TimeOverride.Encoding = settings.TimeOverride.Encoding ?? "local";
                    AppSettings.Current.TimeOverride.StartKey = settings.TimeOverride.StartKey ?? "start";
                    AppSettings.Current.TimeOverride.EndKey = settings.TimeOverride.EndKey ?? "end";
                    AppSettings.Current.TimeOverride.DurationKey = settings.TimeOverride.DurationKey ?? "duration";
                    AppSettings.Current.TimeOverride.PlayseekKey = settings.TimeOverride.PlayseekKey ?? "playseek";
                    AppSettings.Current.TimeOverride.UrlEncode = settings.TimeOverride.UrlEncode;
                }
            }
            catch { }
            try
            {
                Logger.Debug($"[Settings] apply decoder={settings.Decoder} cache={settings.CacheSecs} max={settings.DemuxerMaxBytesMiB} back={settings.DemuxerMaxBackBytesMiB} fcc={settings.FccPrefetchCount} src_to={settings.SourceTimeoutSec} adaptive={settings.EnableProtocolAdaptive} hls_live={settings.HlsStartAtLiveEdge} hls_ra={settings.HlsReadaheadSecs} alang={settings.Alang} slang={settings.Slang} mpv_to={settings.MpvNetworkTimeoutSec}");
            }
            catch { }
            AppSettings.Current.Save();
            if (_window.PlayerInterop != null && mpvWillChange)
            {
                _window.PlayerInterop.SetSettings(AppSettings.Current);
                _window.PlayerInterop.Initialize();
            }

            // 反交错 (Deinterlace) 立即应用：即使 mpv 不重 init，也需更新 deinterlace/vf
            try
            {
                _shell.PlayerEngine?.SetDeinterlace(
                    AppSettings.Current.Deinterlace,
                    AppSettings.Current.DeinterlaceFieldParity,
                    AppSettings.Current.DeinterlaceAlgorithm);
            }
            catch { }
            // 音频设置立即应用（volume-gain, volume-max, audio-delay）
            try
            {
                if (_window.PlayerInterop != null)
                {
                    var gain = Math.Max(-200.0, Math.Min(60.0, AppSettings.Current.VolumeGain));
                    _window.PlayerInterop.SetString("volume-gain", gain.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var volMax = Math.Max(100, Math.Min(1000, AppSettings.Current.VolumeMax));
                    _window.PlayerInterop.SetString("volume-max", volMax.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var delay = Math.Max(-100.0, Math.Min(100.0, AppSettings.Current.AudioDelay));
                    _window.PlayerInterop.SetString("audio-delay", delay.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Logger.Debug($"[Settings] apply volume-gain={gain}dB volume-max={volMax}% audio-delay={delay}s");
                }
            }
            catch { }
            try
            {
                App.ApplyLanguage(AppSettings.Current.Language);
                App.ApplyTheme(AppSettings.Current.ThemeMode);
            }
            catch { }
            try
            {
                if (_window.PlayerInterop != null && mpvWillChange && !string.IsNullOrWhiteSpace(resumeUrl) && (wasTimeshift || wasReplay))
                {
                    _window.PlayerInterop.LoadFile(resumeUrl);
                    var dt = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    dt.Tick += (s2, e2) =>
                    {
                        dt.Stop();
                        try
                        {
                            if (resumePos.HasValue) _window.PlayerInterop.SeekAbsolute(Math.Max(0, resumePos.Value));
                        }
                        catch { }
                    };
                    dt.Start();
                    try
                    {
                        if (wasTimeshift)
                        {
                            _shell.DispatchPlaybackEvent(new StartTimeshiftPlayback("", DateTime.Now, null, resumeUrl));
                        }
                        else if (wasReplay)
                        {
                            _shell.DispatchPlaybackEvent(new StartReplayPlayback("", null, resumeUrl));
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
