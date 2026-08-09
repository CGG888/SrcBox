using System;
using System.Globalization;
using System.Windows;

namespace LibmpvIptvClient
{
    public partial class ReminderDialog : Window
    {
        public string Action { get; private set; } = "notify";
        public int PreAlertSeconds { get; private set; } = 60;
        public string PlayMode { get; private set; } = "default";
        public string? RecordMode { get; private set; }
        public int? RecordDurationMin { get; private set; }
        private DateTime _endLocal;

        public ReminderDialog(string channel, string title, DateTime startLocal) : this(channel, title, startLocal, startLocal.AddHours(1))
        {
        }

        public ReminderDialog(string channel, string title, DateTime startLocal, DateTime endLocal)
        {
            InitializeComponent();
            TxtTitle.Text = channel + " - " + title;
            TxtTime.Text = startLocal.ToString("yyyy-MM-dd HH:mm:ss");
            _endLocal = endLocal;
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try { LibmpvIptvClient.Helpers.ThemeHelper.ApplyTitleBarByTheme(this); } catch { }
        }
        void BtnNotify_Click(object sender, RoutedEventArgs e)
        {
            Action = "notify";
            if (int.TryParse(TbPreAlert.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) PreAlertSeconds = Math.Max(0, v);
            DialogResult = true;
            Close();
        }
        void BtnAutoplay_Click(object sender, RoutedEventArgs e)
        {
            Action = "play";
            if (int.TryParse(TbPreAlert.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) PreAlertSeconds = Math.Max(0, v);
            try
            {
                var dlg = new AutoPlayModeDialog { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = this.Topmost };
                if (dlg.ShowDialog() == true)
                {
                    PlayMode = dlg.Mode ?? "default";
                }
                else
                {
                    PlayMode = "default";
                }
            }
            catch { PlayMode = "default"; }
            DialogResult = true;
            Close();
        }
        void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TbPreAlert.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) PreAlertSeconds = Math.Max(0, v);
            try
            {
                var channelInfo = TxtTitle.Text.Split(new[] { " - " }, 2, StringSplitOptions.None);
                var channel = channelInfo.Length > 0 ? channelInfo[0] : "";
                var title = channelInfo.Length > 1 ? channelInfo[1] : "";
                var startLocal = DateTime.Parse(TxtTime.Text);

                var dlg = new RecordModeDialog(channel, title, startLocal, _endLocal) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = this.Topmost };
                if (dlg.ShowDialog() == true)
                {
                    RecordMode = dlg.RecordingType;
                    Action = dlg.RecordingType == "front" ? "record_front" : "record_back";
                    RecordDurationMin = dlg.UseCustomDuration ? dlg.CustomDurationMin : null;
                }
                else
                {
                    Action = "notify";
                    DialogResult = false;
                    Close();
                    return;
                }
            }
            catch { Action = "notify"; }
            DialogResult = true;
            Close();
        }
    }
}
