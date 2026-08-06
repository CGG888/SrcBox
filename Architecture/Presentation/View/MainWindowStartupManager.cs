using System;
using System.IO;
using System.Linq;
using System.Windows;
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
                    openSettings: () => { try { _window.Dispatcher.Invoke(_window.OpenSettings); } catch { } },
                    exitApp: () => { try { System.Windows.Application.Current.Shutdown(); } catch { } },
                    openReminderNotify: () => { try { _window.Dispatcher.Invoke(ReminderWindowManager.OpenOrActivate); } catch { } },
                    openReminderAutoplay: () => { try { _window.Dispatcher.Invoke(ReminderWindowManager.OpenOrActivate); } catch { } },
                    openM3uManage: () => { try { _window.Dispatcher.Invoke(M3uWindowManager.OpenOrActivate); } catch { } }
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
            if (AppSettings.Current.AutoLoadLastSource &&
                !string.IsNullOrWhiteSpace(AppSettings.Current.LastLocalM3uPath) &&
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
                    _shell.MenuActions.LoadM3u(lastSelected);
                }
            }
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
