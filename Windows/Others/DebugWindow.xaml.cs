using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LibmpvIptvClient.Diagnostics;
using WinDialog = System.Windows.MessageBox;

namespace LibmpvIptvClient
{
    public partial class DebugWindow : Window
    {
        private bool _isDebugMode;

        public DebugWindow()
        {
            InitializeComponent();
            _isDebugMode = LibmpvIptvClient.AppSettings.Current.DebugLoggingEnabled || Logger.DebugEnabled;
            Logger.OnMessage += OnLog;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;
            UpdateDebugButton();
            LoadRecentLog();
        }

        private void LoadRecentLog()
        {
            const int MAX_LINES = 2000;
            try
            {
                var latestLog = Logger.GetLatestLogFile();
                if (!string.IsNullOrEmpty(latestLog) && File.Exists(latestLog))
                {
                    var lines = File.ReadLines(latestLog).TakeLast(MAX_LINES);
                    var sb = new System.Text.StringBuilder();
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            sb.AppendLine(line);
                    }
                    TxtLog.Text = sb.ToString();
                    TxtLog.ScrollToEnd();
                }
            }
            catch { }
        }

        void OnLog(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(msg + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });
        }

        void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (e.Key == System.Windows.Input.Key.F1)
                {
                    Close();
                    e.Handled = true;
                }
            }
            catch { }
        }

        void OnClosed(object? sender, EventArgs e)
        {
            Logger.OnMessage -= OnLog;
        }

        void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        void BtnDebug_Click(object sender, RoutedEventArgs e)
        {
            _isDebugMode = !_isDebugMode;
            Logger.DebugEnabled = _isDebugMode;
            UpdateDebugButton();

            // Remembered across restarts: mpv's own log file and the RTSP archive probe only attach while
            // starting up, so a user who needs to send those files must not have to set an env var.
            try
            {
                LibmpvIptvClient.AppSettings.Current.DebugLoggingEnabled = _isDebugMode;
                LibmpvIptvClient.AppSettings.Current.Save();
            }
            catch { }

            var status = _isDebugMode ? "开启" : "关闭";
            Logger.Info($"[App] Debug模式 {status}");
        }

        private void UpdateDebugButton()
        {
            BtnDebug.Content = _isDebugMode
                ? $"[ON] {Helpers.ResxLocalizer.Get("UI_DebugMode", "调试模式")}"
                : Helpers.ResxLocalizer.Get("UI_DebugMode", "调试模式");
        }

        void CmbLogLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLogLevel.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int level))
                {
                    Logger.MinimumLevel = (LogLevel)level;
                    Logger.Info($"[App] 日志级别调整为 {(LogLevel)level}");
                }
            }
        }

        void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logDir = Logger.GetLogDirectory();
                if (string.IsNullOrEmpty(logDir) || !Directory.Exists(logDir))
                {
                    WinDialog.Show("日志目录不存在", "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (Directory.GetFiles(logDir, "SrcBox-*.log").Length == 0)
                {
                    WinDialog.Show("没有找到日志文件", "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    // One zip instead of one file: a playback report needs the player log *and* the newest mpv
                    // logs (mpv-*-main-*.log names the RTSP session that stayed silent), and users kept sending
                    // only the first one.
                    Filter = "日志压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    FileName = $"SrcBox-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                    Title = "导出日志"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var count = Diagnostics.LogBundleExporter.Build(
                        saveDialog.FileName,
                        logDir,
                        Logger.GetLatestLogFile(),
                        Logger.GetLatestDebugLogFile());

                    if (count == 0)
                    {
                        WinDialog.Show("没有找到日志文件", "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    WinDialog.Show($"已打包 {count} 个日志文件到:\n{saveDialog.FileName}\n\n反馈播放问题时把整个 zip 发到 issue 即可。",
                        "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                WinDialog.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
