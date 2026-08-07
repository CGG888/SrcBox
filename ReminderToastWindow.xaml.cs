using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace LibmpvIptvClient
{
    public partial class ReminderToastWindow : Window
    {
        private readonly string _channelId;
        private readonly string _channelName;
        private readonly DispatcherTimer _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _reposition = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        private int _remainSec = 10;
        private readonly bool _isDue;
        private readonly Func<System.Windows.Rect>? _anchorProvider;
        private string? _soundKey;
        private bool _fadeClosing;
        private int _autoHideMs = -1;
            private bool _autoPlayOnCountdown = false;
            private bool _autoPlayed = false;
            private string? _playMode = "default";
            private bool _isRecordBack = false;
        public ReminderToastWindow(string channelId, string channelName, string program, DateTime startLocal, string? logoPath, bool isDue = false, Func<System.Windows.Rect>? anchorProvider = null, string? statusOverride = null, bool showCountdown = true, string? playMode = null, int? countdownSec = null, bool isRecordBack = false)
        {
            InitializeComponent();
            _channelId = channelId ?? "";
            _channelName = channelName ?? "";
            _isDue = isDue;
            _anchorProvider = anchorProvider;
            _playMode = string.IsNullOrWhiteSpace(playMode) ? "default" : playMode;
            _isRecordBack = isRecordBack;
            if (countdownSec.HasValue && countdownSec.Value > 0) _remainSec = countdownSec.Value;
            TxtChannel.Text = channelName ?? "";
            TxtProgram.Text = program ?? "";
            TxtTime.Text = startLocal.ToString("yyyy-MM-dd HH:mm");
            try
            {
                var lblRemain = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_RemainFmt", "剩余 {0} 秒");
                TxtCountdown.Text = string.Format(lblRemain, _remainSec.ToString("00"));
                var lblStart = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Status_Start", "节目开播");
                var lblBooked = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Status_Booked", "预约成功");
                TxtStatus.Text = statusOverride ?? (isDue ? lblStart : lblBooked);
                var lblPlayNow = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_PlayNow", "立即播放");
                var lblDismiss = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Dismiss", "知道了");
                try { BtnPlay.Content = lblPlayNow; } catch { }
                try { BtnDismiss.Content = lblDismiss; } catch { }
                if (!showCountdown) { try { TxtCountdown.Visibility = Visibility.Collapsed; } catch { } }
            }
            catch
            {
                TxtCountdown.Text = $"剩余 {_remainSec:00} 秒";
                TxtStatus.Text = statusOverride ?? (isDue ? "节目开播" : "预约成功");
            }
            try { BtnPlay.Visibility = isDue ? Visibility.Visible : Visibility.Collapsed; } catch { }
            try
            {
                if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    ImgLogo.Source = bmp;
                }
            }
            catch { }
            Loaded += (s, e) =>
            {
                try
                {
                    CenterToAnchor();
                    // Sound immediately on render
                    if (!string.IsNullOrWhiteSpace(_soundKey))
                    {
                        try { LibmpvIptvClient.Services.AudioManager.Instance.Play(_soundKey!); } catch { }
                    }
                    _autoClose.Tick += (s2, e2) =>
                    {
                        try
                        {
                            if (_autoHideMs > 0)
                            {
                                _autoHideMs -= 1000;
                                if (_autoHideMs <= 0) { BeginFadeAndClose(); return; }
                            }
                            _remainSec = Math.Max(0, _remainSec - 1);
                            try
                            {
                                var lblRemain2 = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_RemainFmt", "剩余 {0} 秒");
                                TxtCountdown.Text = string.Format(lblRemain2, _remainSec.ToString("00"));
                            }
                            catch
                            {
                                TxtCountdown.Text = $"剩余 {_remainSec:00} 秒";
                            }
                                if (_remainSec <= 0 && _autoHideMs < 0)
                                {
                                    if (_autoPlayOnCountdown && !_autoPlayed && !_isRecordBack)
                                    {
                                        _autoPlayed = true;
                                        try
                                        {
                                            var mw = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                                            try
                                            {
                                                if (mw != null)
                                                {
                                                    BringToFront(mw);
                                                    ApplyPlayMode(mw, _playMode ?? "default");
                                                    mw.JumpToChannelByIdOrName(_channelId, _channelName);
                                                }
                                            }
                                            catch { }
                                            var started = LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Status_AutoPlayed", "已开始播放");
                                            try { TxtStatus.Text = started; } catch { }
                                            try { BtnPlay.IsEnabled = false; } catch { }
                                        }
                                        catch { }
                                        try { _autoClose.Stop(); } catch { }
                                        return;
                                    }
                                    BeginFadeAndClose();
                                }
                        }
                        catch { }
                    };
                    _autoClose.Start();
                    _reposition.Tick += (s3, e3) => { try { CenterToAnchor(); } catch { } };
                    _reposition.Start();
                }
                catch { }
            };
        }
        public void SetSound(string key) { _soundKey = key; }
        public void SetAutoHide(int milliseconds)
        {
            _autoHideMs = Math.Max(0, milliseconds);
            try { TxtCountdown.Visibility = Visibility.Collapsed; } catch { }
        }
            [DllImport("user32.dll")]
            private static extern bool SetForegroundWindow(IntPtr hWnd);
            [DllImport("user32.dll")]
            private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
            const int SW_RESTORE = 9;
            static void BringToFront(Window w)
            {
                try
                {
                    w.ShowInTaskbar = true;
                    w.Show();
                    if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindow(hwnd, SW_RESTORE);
                        SetForegroundWindow(hwnd);
                    }
                    w.Topmost = true;
                    w.Topmost = false;
                    w.Activate();
                    w.Focus();
                }
                catch { }
            }
            static void ApplyPlayMode(MainWindow mw, string mode)
            {
                try
                {
                    if (string.Equals(mode, "window", StringComparison.OrdinalIgnoreCase)) mw.SetFullscreenMode(false);
                    else if (string.Equals(mode, "fullscreen", StringComparison.OrdinalIgnoreCase)) mw.SetFullscreenMode(true);
                }
                catch { }
            }
            public void EnableAutoPlayManualClose()
            {
                _autoPlayOnCountdown = true;
                _autoHideMs = -1;
                try { BtnPlay.Visibility = Visibility.Visible; } catch { }
                try { TxtCountdown.Visibility = Visibility.Visible; } catch { }
            }
        void BeginFadeAndClose()
        {
            if (_fadeClosing) return;
            _fadeClosing = true;
            try
            {
                var ani = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(300)));
                ani.Completed += (s, e) => { try { Close(); } catch { } };
                this.BeginAnimation(Window.OpacityProperty, ani);
            }
            catch { Close(); }
        }
        void CenterToAnchor()
        {
            if (_anchorProvider != null)
            {
                var r = _anchorProvider();
                if (r.Width > 0 && r.Height > 0)
                {
                    Left = r.Left + (r.Width - Width) / 2;
                    Top = r.Top + (r.Height - Height) / 2;
                    return;
                }
            }
            // Default: bottom-right of workarea
            Left = SystemParameters.WorkArea.Right - Width - 16;
            Top = SystemParameters.WorkArea.Bottom - Height - 16;
        }
        void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mw = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                try
                {
                    if (mw != null)
                    {
                        BringToFront(mw);
                        ApplyPlayMode(mw, _playMode ?? "default");
                        mw.JumpToChannelByIdOrName(_channelId, _channelName);
                    }
                }
                catch { }
            }
            catch { }
            Close();
        }
        void BtnDismiss_Click(object sender, RoutedEventArgs e) => Close();
        protected override void OnClosed(EventArgs e)
        {
            try { _autoClose.Stop(); } catch { }
            try { _reposition.Stop(); } catch { }
            try { if (!string.IsNullOrWhiteSpace(_soundKey)) LibmpvIptvClient.Services.AudioManager.Instance.Stop(_soundKey!); } catch { }
            base.OnClosed(e);
        }
    }
}
