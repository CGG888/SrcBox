using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace LibmpvIptvClient
{
    public partial class ScheduledRecordingListWindow : Window
    {
        public ScheduledRecordingListWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                try { Helpers.ThemeHelper.ApplyTitleBarByTheme(this); } catch { }
                LoadData();
            };
            try { Services.ScheduledRecordingManager.Instance.RecordingUpdated += OnRecordingUpdated; } catch { }
            try { Services.ScheduledRecordingManager.Instance.RecordingStarted += OnRecordingStarted; } catch { }
            Closed += (s, e) =>
            {
                try { Services.ScheduledRecordingManager.Instance.RecordingUpdated -= OnRecordingUpdated; } catch { }
                try { Services.ScheduledRecordingManager.Instance.RecordingStarted -= OnRecordingStarted; } catch { }
            };
        }

        private void OnRecordingUpdated(object? sender, Models.ScheduledRecordingInfo e)
        {
            try
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[RecordingList] OnRecordingUpdated: {e.ChannelName} status={e.Status}");
                Dispatcher.Invoke(LoadData);
            }
            catch { }
        }

        private void OnRecordingStarted(object? sender, Models.ScheduledRecordingInfo e)
        {
            try
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[RecordingList] OnRecordingStarted: {e.ChannelName} status={e.Status}");
                Dispatcher.Invoke(LoadData);
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try { Helpers.ThemeHelper.ApplyTitleBarByTheme(this); } catch { }
        }

        void LoadData()
        {
            try
            {
                var manager = Services.ScheduledRecordingManager.Instance;
                // Only show items that are currently recording
                var recordings = manager.GetAll()
                    .Where(r => r.Status == Models.ScheduledRecordingStatus.Recording)
                    .ToList();

                Grid.ItemsSource = null;
                Grid.ItemsSource = recordings;

                var frontCount = manager.ActiveFrontRecordingCount;
                var backCount = manager.ActiveBackRecordingCount;
                var maxBack = manager.MaxBackgroundRecordings;

                TxtFrontCount.Text = string.Format(Helpers.ResxLocalizer.Get("ScheduledRecordingList_FrontCount", "前台: {0}/1"), frontCount);
                TxtBackCount.Text = string.Format(Helpers.ResxLocalizer.Get("ScheduledRecordingList_BackCount", "后台: {0}/{1}"), backCount, maxBack);
            }
            catch { }
        }

        void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is Models.ScheduledRecordingInfo info && info.Status == Models.ScheduledRecordingStatus.Recording)
                {
                    Services.ScheduledRecordingManager.Instance.StopRecording(info.Id);
                    LoadData();
                }
            }
            catch { }
        }

        void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is Models.ScheduledRecordingInfo info && info.Status == Models.ScheduledRecordingStatus.Waiting)
                {
                    Services.ScheduledRecordingManager.Instance.CancelScheduled(info.Id);
                    LoadData();
                }
            }
            catch { }
        }

        void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is Models.ScheduledRecordingInfo info)
                {
                    var status = info.Status;
                    if (status == Models.ScheduledRecordingStatus.Completed ||
                        status == Models.ScheduledRecordingStatus.Failed ||
                        status == Models.ScheduledRecordingStatus.Cancelled)
                    {
                        Services.ScheduledRecordingManager.Instance.RemoveCompleted(info.Id);
                        LoadData();
                    }
                }
            }
            catch { }
        }

        void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is Models.ScheduledRecordingInfo info && info.Status == Models.ScheduledRecordingStatus.Completed)
                {
                    if (!string.IsNullOrEmpty(info.FilePath) && File.Exists(info.FilePath))
                    {
                        Process.Start(new ProcessStartInfo(info.FilePath) { UseShellExecute = true });
                    }
                }
            }
            catch { }
        }

        void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is Models.ScheduledRecordingInfo info)
                {
                    string? folder = null;
                    if (!string.IsNullOrEmpty(info.FilePath) && File.Exists(info.FilePath))
                    {
                        folder = Path.GetDirectoryName(info.FilePath);
                    }

                    if (string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(info.ChannelName))
                    {
                        var config = AppSettings.Current.Recording;
                        var template = config?.DirTemplate ?? "recordings/{channel}";
                        folder = template.Replace("{channel}", info.ChannelName);
                        folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folder);
                    }

                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
                    }
                }
            }
            catch { }
        }
    }
}
