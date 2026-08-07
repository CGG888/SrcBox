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
        private Action? _openReminder;
        private Action? _openRecordingList;
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
            App.LanguageChanged += OnLanguageChanged;
            App.ThemeChanged += OnThemeChanged;
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
        bool IsDarkMode()
        {
            try
            {
                var mode = AppSettings.Current?.ThemeMode ?? "System";
                if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase)) return false;
                // System default - check registry
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    var v = key.GetValue("AppsUseLightTheme");
                    if (v is int i) return i == 0; // 0 = dark, 1 = light
                }
            }
            catch { }
            return false;
        }
        void BuildContextMenu()
        {
            _menu = new ContextMenuStrip();
            var dark = IsDarkMode();
            if (dark)
                _menu.Renderer = new DarkMenuRenderer();
            else
                _menu.Renderer = new ToolStripProfessionalRenderer();
            var miOpen = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Tray_OpenMain", "打开主界面"));
            miOpen.Click += (s, e) => { try { _openMain?.Invoke(); } catch { } };

            var miReminder = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Tray_OpenReminder", "预约管理"));
            miReminder.Click += (s, e) => { try { _openReminder?.Invoke(); } catch { } };

            var miRecordingList = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Tray_OpenRecordingList", "正在录制"));
            miRecordingList.Click += (s, e) => { try { _openRecordingList?.Invoke(); } catch { } };

            var miM3uManage = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Tray_ChannelData", "频道数据"));
            miM3uManage.Click += (s, e) => { try { _openM3uManage?.Invoke(); } catch { } };
            var miSettings = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Menu_Settings", "设置"));
            miSettings.Click += (s, e) => { try { _openSettings?.Invoke(); } catch { } };
            var miExit = new ToolStripMenuItem(LibmpvIptvClient.Helpers.Localizer.S("Menu_Exit", "退出"));
            miExit.Click += (s, e) => { try { _exitApp?.Invoke(); } catch { } };
            _menu.Items.Add(miOpen);
            _menu.Items.Add(miReminder);
            _menu.Items.Add(miRecordingList);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miM3uManage);
            _menu.Items.Add(miSettings);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miExit);
            _icon.ContextMenuStrip = _menu;
        }
        void OnLanguageChanged()
        {
            try { BuildContextMenu(); } catch { }
        }
        void OnThemeChanged()
        {
            try { BuildContextMenu(); } catch { }
        }
        public void SetMenuCallbacks(Action openMain, Action openSettings, Action exitApp,
                                     Action? openReminder = null, Action? openRecordingList = null,
                                     Action? openM3uManage = null)
        {
            _openMain = openMain;
            _openSettings = openSettings;
            _exitApp = exitApp;
            _openReminder = openReminder;
            _openRecordingList = openRecordingList;
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

    internal class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(20, 20, 20);
        public override Color ImageMarginGradientBegin => Color.FromArgb(20, 20, 20);
        public override Color ImageMarginGradientEnd => Color.FromArgb(20, 20, 20);
    }

    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var textColor = e.Item.Enabled ? Color.White : Color.FromArgb(160, 160, 160);
            using (var brush = new SolidBrush(textColor))
            {
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                };
                var textRect = new Rectangle(e.Item.ContentRectangle.X + 2, 0, e.Item.Bounds.Width - 4, e.Item.Bounds.Height);
                e.Graphics.DrawString(e.Item.Text, e.Item.Font ?? SystemFonts.MenuFont, brush, textRect, format);
            }
        }
    }
}
