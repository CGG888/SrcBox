using System;
using System.IO;
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
            Logger.OnMessage += OnLog;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;
            UpdateDebugButton();
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

                var files = Directory.GetFiles(logDir, "iptv_*.log");
                if (files.Length == 0)
                {
                    WinDialog.Show("没有找到日志文件", "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
                    FileName = $"iptv_log_{DateTime.Now:yyyyMMdd_HHmmss}.log",
                    Title = "导出日志"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var latestLog = Logger.GetLatestLogFile();
                    if (!string.IsNullOrEmpty(latestLog) && File.Exists(latestLog))
                    {
                        File.Copy(latestLog, saveDialog.FileName, true);
                        WinDialog.Show($"日志已导出到:\n{saveDialog.FileName}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        using (var writer = new StreamWriter(saveDialog.FileName))
                        {
                            writer.Write(TxtLog.Text);
                        }
                        WinDialog.Show($"当前会话日志已导出到:\n{saveDialog.FileName}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                WinDialog.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
