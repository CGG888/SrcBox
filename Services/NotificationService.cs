using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace LibmpvIptvClient.Services
{
    public class NotificationService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static readonly Lazy<NotificationService> _lazy = new Lazy<NotificationService>(() => new NotificationService());
        public static NotificationService Instance => _lazy.Value;
        private readonly NotifyIcon _icon;
        private ContextMenuStrip _menu;
        private Action? _openMain;
        private Action? _openSettings;
        private Action? _exitApp;
        private Action? _openReminderNotify;
        private Action? _openReminderAutoplay;
        private Action? _openReminderRecord;
        private Action? _openM3uManage;

        private NotificationService()
        {
            _icon = new NotifyIcon();
            try
            {
                var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "srcbox.ico");
                if (File.Exists(icoPath)) _icon.Icon = new Icon(icoPath);
                if (_icon.Icon == null)
                {
                    var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(exe))
                    {
                        try { _icon.Icon = Icon.ExtractAssociatedIcon(exe); } catch { }
                    }
                }
            }
            catch { }
            _icon.Visible = true;
            _icon.Text = "SrcBox";
            _icon.DoubleClick += (s, e) => { try { _openMain?.Invoke(); } catch { } };
            BuildContextMenu();
        }

        public void Show(string title, string message, int timeoutMs = 8000)
        {
            try
            {
                _icon.BalloonTipTitle = title ?? "SrcBox";
                _icon.BalloonTipText = message ?? "";
                _icon.BalloonTipIcon = ToolTipIcon.Info;
                _icon.ShowBalloonTip(timeoutMs);
            }
            catch { }
        }
        public void SetTrayTooltip(string text)
        {
            try
            {
                var value = string.IsNullOrWhiteSpace(text) ? "SrcBox" : text.Trim();
                const int maxLen = 63;
                if (value.Length > maxLen) value = value.Substring(0, maxLen);
                _icon.Text = value;
            }
            catch { }
        }
        void BuildContextMenu()
        {
            _menu = new ContextMenuStrip();
            var miOpen = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Tray_OpenMain", "打开主界面"));
            miOpen.Click += (s, e) => { try { _openMain?.Invoke(); } catch { } };
            var miReminder = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Menu_Reminders", "预约"));
            var miReminderNotify = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Menu_ReminderNotify", "通知"));
            miReminderNotify.Click += (s, e) => { try { _openReminderNotify?.Invoke(); } catch { } };
            var miReminderAutoplay = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Menu_ReminderAutoplay", "播放")) { Enabled = true };
            miReminderAutoplay.Click += (s, e) => { try { _openReminderAutoplay?.Invoke(); } catch { } };
            var miReminderRecord = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Menu_ReminderRecord", "录播")) { Enabled = false };
            miReminderRecord.Click += (s, e) => { try { _openReminderRecord?.Invoke(); } catch { } };
            miReminder.DropDownItems.Add(miReminderNotify);
            miReminder.DropDownItems.Add(miReminderAutoplay);
            miReminder.DropDownItems.Add(miReminderRecord);
            var miM3uManage = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Menu_ManageM3u", "管理 M3U 列表"));
            miM3uManage.Click += (s, e) => { try { _openM3uManage?.Invoke(); } catch { } };
            var miSettings = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Menu_Settings", "设置"));
            miSettings.Click += (s, e) => { try { _openSettings?.Invoke(); } catch { } };
            var miExit = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Menu_Exit", "退出"));
            miExit.Click += (s, e) => { try { _exitApp?.Invoke(); } catch { } };
            _menu.Items.Add(miOpen);
            _menu.Items.Add(miReminder);
            _menu.Items.Add(miM3uManage);
            _menu.Items.Add(miSettings);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miExit);
            _icon.ContextMenuStrip = _menu;
        }
        public void SetMenuCallbacks(Action openMain, Action openSettings, Action exitApp,
                                     Action? openReminderNotify = null, Action? openReminderAutoplay = null, Action? openReminderRecord = null,
                                     Action? openM3uManage = null)
        {
            _openMain = openMain;
            _openSettings = openSettings;
            _exitApp = exitApp;
            _openReminderNotify = openReminderNotify;
            _openReminderAutoplay = openReminderAutoplay;
            _openReminderRecord = openReminderRecord;
            _openM3uManage = openM3uManage;
        }
        public void ShowWithLogo(string channel, string program, DateTime startLocal, string? logoPath, int timeoutMs = 8000)
        {
            Icon? old = null;
            IntPtr hIconToDestroy = IntPtr.Zero;
            try
            {
                old = _icon.Icon;
                if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                {
                    using var bmp = new Bitmap(logoPath);
                    using var small = new Bitmap(bmp, new Size(32, 32));
                    var hIcon = small.GetHicon();
                    hIconToDestroy = hIcon;
                    _icon.Icon = Icon.FromHandle(hIcon);
                }
            }
            catch { }
            try
            {
                var title = string.IsNullOrWhiteSpace(channel) ? "SrcBox" : channel;
                var body = $"{program}  {startLocal:yyyy-MM-dd HH:mm}";
                Show(title, body, timeoutMs);
            }
            finally
            {
                try { if (old != null) _icon.Icon = old; } catch { }
                if (hIconToDestroy != IntPtr.Zero) DestroyIcon(hIconToDestroy);
            }
        }

        public void Dispose()
        {
            try { _icon.Visible = false; _icon.Dispose(); } catch { }
        }
    }
}
