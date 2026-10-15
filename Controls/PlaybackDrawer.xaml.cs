using System;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace LibmpvIptvClient.Controls
{
    public partial class PlaybackDrawer : UserControl
    {
        private ReplayConfig _config;
        private bool _suppressPresetChange;

        public PlaybackDrawer()
        {
            InitializeComponent();
            CatchupFormatPresetUi.Fill(CbPreset);
        }

        public void Load(ReplayConfig config)
        {
            _config = config;
            _suppressPresetChange = true;
            try
            {
                CbEnabled.IsChecked = config.Enabled;
                TbUrlFormat.Text = config.UrlFormat;
                SldDuration.Value = Math.Max(1, Math.Min(168, config.DurationHours));
                CbAppendEpgTime.IsChecked = config.AppendEpgTime;
                // Only highlight a saved template that is one of the presets; a hand written one stays
                // untouched (and is never overwritten by the selection).
                CatchupFormatPresetUi.SelectMatching(CbPreset, config.UrlFormat);
            }
            finally { _suppressPresetChange = false; }
        }

        private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPresetChange) return;
            if (CatchupFormatPresetUi.TemplateOf(CbPreset) is string template) TbUrlFormat.Text = template;
        }

        public void Save(ReplayConfig config)
        {
            config.Enabled = CbEnabled.IsChecked == true;
            config.UrlFormat = TbUrlFormat.Text;
            config.DurationHours = (int)SldDuration.Value;
            config.AppendEpgTime = CbAppendEpgTime.IsChecked == true;
        }

        public bool HasChanges(ReplayConfig original)
        {
            if (original.Enabled != (CbEnabled.IsChecked == true)) return true;
            if (original.UrlFormat != TbUrlFormat.Text) return true;
            if (original.DurationHours != (int)SldDuration.Value) return true;
            if (original.AppendEpgTime != (CbAppendEpgTime.IsChecked == true)) return true;
            return false;
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
