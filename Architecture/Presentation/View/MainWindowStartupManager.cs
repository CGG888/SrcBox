using System;
using System.IO;
using System.Linq;
using System.Windows;
using LibmpvIptvClient.Architecture.Application.Settings;
using LibmpvIptvClient.Architecture.Core;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Helpers;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    public class MainWindowStartupManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;

        /// <summary>Guards against a second check inside one process; the check itself runs on every launch.</summary>
        private bool _updateCheckStarted;

        public MainWindowStartupManager(MainWindow window, MainShellViewModel shell)
        {
            _window = window;
            _shell = shell;
        }

        public void InitializeAppServices()
        {
            _window.EnableDarkTitleBarFromManager();
            try
            {
                NotificationService.Instance.SetMenuCallbacks(
                    openMain: () => { try { _window.Dispatcher.Invoke(() => { _window.Show(); _window.WindowState = WindowState.Normal; _window.Activate(); }); } catch { } },
                    openSettings: () => { try { _window.Dispatcher.Invoke(() => _window.OpenSettings()); } catch { } },
                    exitApp: () => { try { System.Windows.Application.Current.Shutdown(); } catch { } },
                    openReminder: () => { try { _window.Dispatcher.Invoke(() => ReminderWindowManager.OpenOrActivate()); } catch { } },
                    openRecordingList: () => { try { _window.Dispatcher.Invoke(() => { var w = new ScheduledRecordingListWindow { Owner = _window }; w.Show(); }); } catch { } },
                    openM3uManage: () => { try { _window.Dispatcher.Invoke(() => M3uWindowManager.OpenOrActivate()); } catch { } },
                    openMultiScreen: (count) => { try { _window.Dispatcher.Invoke(() => _window.OpenMultiScreen(count)); } catch { } }
                );
                NotificationService.Instance.SetTrayTooltip("SrcBox");
            }
            catch { }
            try { ReminderService.Instance.Start(); } catch { }
            try { ReminderService.Instance.RecordingTriggered += OnRecordingTriggered; } catch { }
            try
            {
                var oldDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SrcBox", "logo-cache");
                if (!string.IsNullOrWhiteSpace(AppSettings.Current.Logo.CacheDir) &&
                    string.Equals(AppSettings.Current.Logo.CacheDir, oldDefault, StringComparison.OrdinalIgnoreCase))
                {
                    AppSettings.Current.Logo.CacheDir = "";
                    AppSettings.Current.Save();
                    try { Logger.Info("Logo缓存目录已迁移"); } catch { }
                }
            }
            catch { }
        }

        public void InitializeAutoLoad()
        {
            if (!AppSettings.Current.AutoLoadLastSource)
            {
                // "Remember the last playlist source" is off: the user picks the source manually.
                Logger.Info("启动时不自动载入列表源（设置已关闭）");
                return;
            }

            if (!string.IsNullOrWhiteSpace(AppSettings.Current.LastLocalM3uPath) &&
                File.Exists(AppSettings.Current.LastLocalM3uPath))
            {
                Logger.Info("正在自动加载上次本地 M3U...");
                _ = _shell.LoadChannels(AppSettings.Current.LastLocalM3uPath);
            }
            else if (AppSettings.Current.SavedSources != null)
            {
                var lastSelected = AppSettings.Current.SavedSources.FirstOrDefault(s => s.IsSelected);
                if (lastSelected != null)
                {
                    Logger.Info("正在自动加载上次选择的源: " + lastSelected.Name);
                    // Startup auto-load must not start playback: only a manual playlist switch resumes
                    // the channel remembered for that playlist.
                    _shell.MenuActions.LoadM3u(lastSelected, restoreChannel: false);
                }
            }

            // No full-list source health scan on startup: sources are probed only when their
            // channel starts playing (SourceHealthService.ProbeChannelSourcesAsync) so ONU /
            // modem line limits are not consumed at launch.
        }

        /// <summary>
        /// Runs the silent version check on every launch, in the background. It never prompts the user and
        /// never blocks startup; a newer release lights the title-bar badge. A daily throttle used to skip
        /// the check, which kept a release that appeared right after the previous check invisible until the
        /// next day, so the check is no longer gated by time.
        /// </summary>
        public void StartBackgroundUpdateCheck()
        {
            if (_updateCheckStarted) return;
            _updateCheckStarted = true;

            try
            {
                var service = ResolveUpdateService();
                var currentVersion = service.GetCurrentVersion();

                // An update found earlier stays visible: this launch's lookup may fail (offline, blocked,
                // rate limited) and must not hide a release the user has not installed yet.
                RestoreKnownUpdate(currentVersion);

                _ = RunUpdateCheckAsync(service);
            }
            catch (Exception ex)
            {
                try { Logger.Debug("[Update] 启动检查异常: " + ex.Message); } catch { }
            }
        }

        /// <summary>Lights the badge from the version a previous session found, without a network call.</summary>
        private void RestoreKnownUpdate(string currentVersion)
        {
            try
            {
                var known = AppSettings.Current.KnownNewerVersion;
                if (string.IsNullOrEmpty(known) || !GithubUpdateService.IsNewerVersion(known, currentVersion))
                {
                    return;
                }

                _window.Dispatcher.Invoke(() =>
                {
                    try { _shell.UpdateBadge.ApplyKnownVersion(known); } catch { }
                });
            }
            catch { }
        }

        private async System.Threading.Tasks.Task RunUpdateCheckAsync(IUpdateService service)
        {
            try
            {
                // Let the startup I/O settle (channel list, EPG download, logo warmup) before competing
                // with it: the check is invisible until it finds something, and the first seconds of a
                // launch are the busiest - a lookup squeezed into them used to time out.
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                var result = await service.CheckForUpdatesAsync(useCdn: true).ConfigureAwait(false);

                // Only a real answer rewrites the remembered state: a failed lookup (offline, blocked,
                // rate limited) must keep the release found earlier instead of clearing the badge. The
                // two timestamps stay as diagnostics for the settings page and for support.
                if (result.ReleaseInfo != null)
                {
                    try
                    {
                        AppSettings.Current.LastUpdateCheckUtc = DateTime.UtcNow;
                        AppSettings.Current.LastUpdateCheckVersion = service.GetCurrentVersion();
                        AppSettings.Current.KnownNewerVersion = result.IsNewer ? (result.ReleaseInfo.Version ?? "") : "";
                        AppSettings.Current.Save();
                    }
                    catch { }
                }

                if (!result.IsNewer || result.ReleaseInfo == null)
                {
                    return;
                }

                _window.Dispatcher.Invoke(() =>
                {
                    try { _shell.UpdateBadge.Apply(result); } catch { }
                });
            }
            catch (Exception ex)
            {
                try { Logger.Debug("[Update] 静默检查失败: " + ex.Message); } catch { }
            }
        }

        private static IUpdateService ResolveUpdateService()
        {
            try { return SrcBoxArchitectureHost.Kernel.Resolve<IUpdateService>(); }
            catch { return new GithubUpdateService(HttpClientService.Instance.Client); }
        }

        private void OnRecordingTriggered(object? sender, ScheduledRecordingTriggerArgs e)
        {
            try
            {
                _window.Dispatcher.Invoke(() =>
                {
                    try { _window.HandleScheduledRecordingTrigger(e); } catch { }
                });
            }
            catch { }
        }
    }
}
