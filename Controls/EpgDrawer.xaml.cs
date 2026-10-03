using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace LibmpvIptvClient.Controls
{
    public partial class EpgDrawer : UserControl
    {
        private EpgConfig _config;

        public EpgDrawer()
        {
            InitializeComponent();
        }

        public void Load(EpgConfig config)
        {
            _config = config;
            CbEnabled.IsChecked = config.Enabled;
            // One EPG url per line: several playlists can each bring their own guide (issue #38).
            TbUrl.Text = string.Join(Environment.NewLine, config.GetEffectiveUrls());
            TbRefresh.Text = config.RefreshIntervalHours.ToString(CultureInfo.InvariantCulture);
            CbSmartMatch.IsChecked = config.EnableSmartMatch;
        }

        public void Save(EpgConfig config)
        {
            config.Enabled = CbEnabled.IsChecked == true;
            var urls = ParseUrls(TbUrl.Text);
            config.Url = urls.Count > 0 ? urls[0] : "";
            config.Urls = urls.Skip(1).ToList();
            if (double.TryParse(TbRefresh.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                config.RefreshIntervalHours = Math.Max(0.1, val);
            }
            config.EnableSmartMatch = CbSmartMatch.IsChecked == true;
        }

        public bool HasChanges(EpgConfig original)
        {
            if (original.Enabled != (CbEnabled.IsChecked == true)) return true;
            var current = ParseUrls(TbUrl.Text);
            var previous = original.GetEffectiveUrls();
            if (current.Count != previous.Count || !current.SequenceEqual(previous, StringComparer.OrdinalIgnoreCase)) return true;
            if (original.EnableSmartMatch != (CbSmartMatch.IsChecked == true)) return true;
            
            double uiVal = 24;
            double.TryParse(TbRefresh.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out uiVal);
            if (Math.Abs(original.RefreshIntervalHours - uiVal) > 0.01) return true;
            
            return false;
        }

        static List<string> ParseUrls(string? text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var url = raw.Trim();
                if (url.Length == 0) continue;
                if (!list.Contains(url, StringComparer.OrdinalIgnoreCase)) list.Add(url);
            }
            return list;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch { }
        }
    }
}
