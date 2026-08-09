using System.Windows;

namespace LibmpvIptvClient
{
    public partial class AutoPlayModeDialog : Window
    {
        public string Mode { get; private set; } = "default";
        public AutoPlayModeDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => { try { LibmpvIptvClient.Helpers.ThemeHelper.ApplyTitleBarByTheme(this); } catch { } };
        }
        void BtnDefault_Click(object sender, RoutedEventArgs e) { Mode = "default"; DialogResult = true; Close(); }
        void BtnWindow_Click(object sender, RoutedEventArgs e) { Mode = "window"; DialogResult = true; Close(); }
        void BtnFullscreen_Click(object sender, RoutedEventArgs e) { Mode = "fullscreen"; DialogResult = true; Close(); }
    }
}
