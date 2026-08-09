using System.Globalization;
using System.Windows;

namespace LibmpvIptvClient
{
    public partial class RecordModeDialog : Window
    {
        public string RecordingType { get; private set; } = "front";
        public bool UseCustomDuration { get; private set; }
        public int CustomDurationMin { get; private set; } = 60;

        public RecordModeDialog(string channel, string title, DateTime startLocal, DateTime endLocal)
        {
            InitializeComponent();
            TxtTitle.Text = channel + " - " + title;
            TxtTime.Text = $"{startLocal:yyyy-MM-dd HH:mm} - {endLocal:HH:mm}";

            var durationMin = (int)(endLocal - startLocal).TotalMinutes;
            TbDuration.Text = durationMin.ToString();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try { Helpers.ThemeHelper.ApplyTitleBarByTheme(this); } catch { }
        }

        void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            RecordingType = RbFront.IsChecked == true ? "front" : "back";
            UseCustomDuration = CbCustomDuration.IsChecked == true;

            if (UseCustomDuration && int.TryParse(TbDuration.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
            {
                CustomDurationMin = Math.Max(1, duration);
            }

            DialogResult = true;
            Close();
        }

        void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
