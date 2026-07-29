using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public class MainWindowRecordingPlaybackActionsViewModel : ViewModelBase
    {
        private readonly MainShellViewModel _shell;

        public Func<bool, bool, Task<bool?>>? RequestPlayModeChoice { get; set; }
        public event Action? RequestVideoShow;
        public event Action? RequestEpgRefresh;

        public MainWindowRecordingPlaybackActionsViewModel(MainShellViewModel shell)
        {
            _shell = shell;
        }

        public async Task PlayRecording(RecordingEntry item, IEnumerable<EpgProgram>? epgItems = null)
        {
            if (_shell.PlayerEngine == null || item == null) return;
            var url = item.PathOrUrl ?? "";
            if (string.IsNullOrWhiteSpace(url)) return;

            bool preferRemote = false;
            string? remoteHrefFromLocal = null;
            bool canLocal = false, canNetwork = false;

            try
            {
                var wd = AppSettings.Current.WebDav;
                if (!string.IsNullOrWhiteSpace(url) && System.IO.File.Exists(url)) canLocal = true;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) canNetwork = true;

                if (wd != null && wd.Enabled && !string.IsNullOrWhiteSpace(wd.BaseUrl) && !string.IsNullOrWhiteSpace(wd.RecordingsPath))
                {
                    var recPath = (wd.RecordingsPath ?? "/srcbox/recordings/").TrimEnd('/');
                    var localFull = url.Replace('\\', '/');
                    var anchor = "/recordings/";
                    var idx = localFull.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var rel = localFull.Substring(idx + anchor.Length); 
                        var cliTmp = new WebDavClient(wd);
                        remoteHrefFromLocal = cliTmp.Combine(recPath + "/" + rel);
                        try
                        {
                            // Fix: Reduce timeout for HEAD request to avoid UI blocking delay
                            var cts = new System.Threading.CancellationTokenSource(2000);
                            var head = await cliTmp.HeadAsync(remoteHrefFromLocal, cts.Token);
                            canNetwork = head.ok;
                        }
                        catch { canNetwork = false; }
                    }
                }

                var recCfg = AppSettings.Current?.Recording ?? new RecordingConfig();
                var modePref = (recCfg.DefaultPlayChoice ?? "prompt").ToLowerInvariant();
                var directChosen = false;
                if (modePref == "local" || modePref == "remote" || modePref == "remember")
                {
                    string desired = modePref == "remember" ? (recCfg.LastPlayChoice ?? "") : modePref;
                    if (desired == "local" && canLocal) { preferRemote = false; directChosen = true; }
                    else if (desired == "remote" && canNetwork) { preferRemote = true; directChosen = true; }
                    else if (!canLocal && canNetwork) { preferRemote = true; directChosen = true; }
                    else if (!canNetwork && canLocal) { preferRemote = false; directChosen = true; }
                }

                if (!directChosen)
                {
                    if (RequestPlayModeChoice != null)
                    {
                        // Use ConfigureAwait(true) to ensure we are on UI thread if needed, or false to avoid context capture?
                        // Actually, RequestPlayModeChoice invokes a UI dialog (MessageBox/ModernMessageBox).
                        // It MUST run on UI thread. But awaiting it might be causing context switch delays if we are in a task.
                        // However, the delay is likely in the "Network Probe" HEAD request above which runs before this dialog.
                        // The user said "Click Play -> Delay -> Dialog". 
                        // The code does `await cliTmp.HeadAsync(...)` BEFORE showing the dialog.
                        // This HEAD request is likely the cause of the delay!
                        // We should show the dialog FIRST or do the probe faster/in parallel?
                        // But we need to know `canNetwork` to decide whether to show the dialog.
                        // Optimization: Set a short timeout for the probe.
                        
                        var choice = await RequestPlayModeChoice.Invoke(canNetwork, canLocal);
                        if (choice == null) return;
                        preferRemote = choice.Value;
                        if (!canNetwork && canLocal) preferRemote = false;
                        if (canNetwork && !canLocal) preferRemote = true;
                        
                        try
                        {
                            if (string.Equals(AppSettings.Current?.Recording?.DefaultPlayChoice ?? "", "remember", StringComparison.OrdinalIgnoreCase))
                            {
                                AppSettings.Current.Recording.LastPlayChoice = preferRemote ? "remote" : "local";
                                AppSettings.Current.Save();
                            }
                        }
                        catch { }
                    }
                    else
                    {
                         if (canLocal) preferRemote = false;
                         else if (canNetwork) preferRemote = true;
                    }
                }
            }
            catch { }

            if (preferRemote && !string.IsNullOrWhiteSpace(remoteHrefFromLocal))
            {
                try
                {
                    var wd = AppSettings.Current.WebDav;
                    if (wd == null || !wd.Enabled || string.IsNullOrWhiteSpace(wd.BaseUrl)) return;
                    var cli = new WebDavClient(wd);
                    var href = remoteHrefFromLocal!;
                    string authUrl = href;
                    try
                    {
                        var u = new Uri(href);
                        var user = wd.Username ?? "";
                        var pass = wd.TokenOrPassword;
                        if (string.IsNullOrWhiteSpace(pass)) pass = CryptoUtil.UnprotectString(wd.EncryptedToken ?? "");
                        var userInfo = $"{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(pass)}";
                        authUrl = $"{u.Scheme}://{userInfo}@{u.Host}{(u.IsDefaultPort ? "" : ":" + u.Port)}{u.AbsolutePath}{u.Query}{u.Fragment}";
                    }
                    catch { authUrl = href; }

                    if (_shell.IsTimeshiftActive) { try { _shell.IsTimeshiftActive = false; } catch { } }
                    
                    _shell.ChannelPlaybackSyncActions.ClearLiveAndEpgIndicators(
                        _shell.Channels,
                        _shell.CurrentPlayingProgram,
                        epgItems,
                        _shell.EpgSelectionSyncActions.ClearPlaying);
                    _shell.CurrentPlayingProgram = null;
                    RequestEpgRefresh?.Invoke();

                    _shell.ClearRecordingPlayingIndicator();
                    try { LibmpvIptvClient.Diagnostics.Logger.Info("正在播放远程录制..."); } catch { }
                    _shell.PlayerEngine.Play(authUrl);
                    _shell.CurrentUrl = authUrl;
                    
                    try { RequestVideoShow?.Invoke(); } catch { }
                    try { _shell.PlayerEngine.SetPropertyString("pause", "no"); _shell.PlayerEngine.SeekAbsolute(0); } catch { }
                    
                    _shell.IsPaused = false;
                    _shell.CurrentRecordingPlaying = item;
                    
                    UpdateOverlayForRecording();
                    return;
                }
                catch { }
            }

            // Local fallback
            if (string.Equals(item.Source ?? "", "Local", StringComparison.OrdinalIgnoreCase) || true)
            {
                try
                {
                    if (_shell.IsTimeshiftActive) { try { _shell.IsTimeshiftActive = false; } catch { } }
                    
                    _shell.ChannelPlaybackSyncActions.ClearLiveAndEpgIndicators(
                        _shell.Channels,
                        _shell.CurrentPlayingProgram,
                        epgItems,
                        _shell.EpgSelectionSyncActions.ClearPlaying);
                    _shell.CurrentPlayingProgram = null;
                    RequestEpgRefresh?.Invoke();

                    _shell.ClearRecordingPlayingIndicator();
                    
                    var fu = new Uri(url, UriKind.Absolute);
                    var fileUri = fu.AbsoluteUri;
                    try { LibmpvIptvClient.Diagnostics.Logger.Info("正在播放本地录制..."); } catch { }
                    _shell.PlayerEngine.Play(fileUri);
                    _shell.CurrentUrl = fileUri;
                }
                catch 
                {
                    try
                    {
                        if (_shell.IsTimeshiftActive) { try { _shell.IsTimeshiftActive = false; } catch { } }
                        
                        _shell.ChannelPlaybackSyncActions.ClearLiveAndEpgIndicators(
                            _shell.Channels,
                            _shell.CurrentPlayingProgram,
                            epgItems,
                            _shell.EpgSelectionSyncActions.ClearPlaying);
                        _shell.CurrentPlayingProgram = null;
                        RequestEpgRefresh?.Invoke();

                        _shell.ClearRecordingPlayingIndicator();
                        
                        try { LibmpvIptvClient.Diagnostics.Logger.Info("正在播放本地录制(回退)..."); } catch { }
                        _shell.PlayerEngine.Play(url);
                        _shell.CurrentUrl = url;
                    }
                    catch { }
                }
                
                try { RequestVideoShow?.Invoke(); } catch { }
                _shell.IsPaused = false;
                try { _shell.PlayerEngine.SetPropertyString("pause", "no"); _shell.PlayerEngine.SeekAbsolute(0); } catch { }
                
                _shell.CurrentRecordingPlaying = item;
                UpdateOverlayForRecording();
            }
        }

        private void UpdateOverlayForRecording()
        {
            try
            {
                _shell.DispatchPlaybackEvent(new StartRecordingPlayback(_shell.CurrentUrl ?? ""));
            }
            catch { }
        }
    }
}
