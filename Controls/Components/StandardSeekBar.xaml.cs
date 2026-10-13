using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace LibmpvIptvClient
{
    /// <summary>
    /// The plain "standard" progress bar: one native slider plus the embedded volume group. It owns exactly one
    /// shape and knows nothing about the EPG band, so working on either style can no longer disturb the other.
    /// The hosts show this control instead of <see cref="TimeshiftTimeline"/> whenever the configured seek bar
    /// style is <c>Standard</c>, or while a channel has no EPG schedule to draw.
    /// </summary>
    public partial class StandardSeekBar : UserControl, ISeekMeter
    {
        /// <summary>The single row this control occupies; the hosts reserve it for the bottom bar height.</summary>
        public const double MeterRowHeight = 20d;

        /// <summary>Raised when the user presses the slider (the host starts a seek interaction).</summary>
        public event Action? SeekStart;

        /// <summary>Raised when the drag ends; the value is in the units the host's seek manager expects.</summary>
        public event Action<double>? SeekEnd;

        /// <summary>Raised for a volume knob change, the mute button included.</summary>
        public event Action<double>? VolumeChanged;

        /// <summary>Raised when the mute button toggles.</summary>
        public event Action<bool>? MuteChanged;

        /// <summary>Raised when the volume group is shown or hidden so the host can re-measure.</summary>
        public event Action? LayoutChanged;

        private const string VolumeData = "M2,5 L2,11 L5,11 L9,15 L9,1 L5,5 Z M11,5 Q13,8 11,11 M14,3 Q17,8 14,13";
        private const string MuteData = "M2,5 L2,11 L5,11 L9,15 L9,1 L5,5 Z M11,3 L15,13 M15,3 L11,13";

        private bool _seeking;
        private bool _muted;

        public StandardSeekBar()
        {
            InitializeComponent();
        }

        /// <summary>Height the host has to reserve for this bar.</summary>
        public double RequiredHeight => MeterRowHeight;

        /// <summary>The embedded volume slider (the fullscreen overlay drives its volume UI through it).</summary>
        public LibmpvIptvClient.Controls.VolumeSlider VolumeSlider => SliderVolume;

        /// <summary>The embedded mute button.</summary>
        public Button MuteButton => BtnMute;

        /// <summary>The muting state this bar paints.</summary>
        public bool IsMuted => _muted;

        /// <summary>Slider position; the unit is seconds, matching <see cref="SeekEnd"/>.</summary>
        public double SeekValue
        {
            get => SliderSeek.Value;
            set
            {
                try
                {
                    var max = SliderSeek.Maximum;
                    var min = SliderSeek.Minimum;
                    SliderSeek.Value = Math.Max(min, Math.Min(max <= min ? min : max, value));
                }
                catch { }
            }
        }

        /// <summary>Upper bound of the bar; plain playback maps 0..duration.</summary>
        public double SeekMaximum
        {
            get => SliderSeek.Maximum;
            set
            {
                try
                {
                    SliderSeek.Maximum = value <= 0 ? 1 : value;
                    if (SliderSeek.Value > SliderSeek.Maximum) SliderSeek.Value = SliderSeek.Maximum;
                }
                catch { }
            }
        }

        /// <summary>Lower bound of the bar.</summary>
        public double SeekMinimum
        {
            get => SliderSeek.Minimum;
            set
            {
                try
                {
                    SliderSeek.Minimum = value;
                    if (SliderSeek.Value < value) SliderSeek.Value = value;
                }
                catch { }
            }
        }

        /// <summary>Plain-playback progress: the bar maps 0..<paramref name="duration"/>.</summary>
        public void SetTime(double current, double duration)
        {
            try
            {
                SliderSeek.Minimum = 0;
                SliderSeek.Maximum = duration <= 0 ? 1 : duration;
                if (!_seeking) SliderSeek.Value = Math.Max(0, Math.Min(SliderSeek.Maximum, current));
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[SeekBar] SetTime failed: {ex.Message}");
            }
        }

        /// <summary>Sets the embedded volume slider without raising <see cref="VolumeChanged"/>.</summary>
        public void SetVolume(double val)
        {
            try { SliderVolume.Volume = val; } catch { }
        }

        /// <summary>Hosts that keep their own volume control (the windowed bar) hide this group.</summary>
        public void SetVolumeGroupVisible(bool visible)
        {
            try
            {
                var next = visible ? Visibility.Visible : Visibility.Collapsed;
                if (VolumeGroup.Visibility == next) return;
                VolumeGroup.Visibility = next;
                LayoutChanged?.Invoke();
            }
            catch { }
        }

        /// <summary>Paints the mute state (icon, tint, volume slider).</summary>
        public void SetMuted(bool muted)
        {
            _muted = muted;
            try
            {
                IconMute.Data = Geometry.Parse(_muted ? MuteData : VolumeData);
                // 静音时按钮背景变为浅红色
                BtnMute.Background = _muted
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0x45, 0x3A))
                    : null;
                SliderVolume.IsMuted = _muted;
            }
            catch { }
        }

        // ---- slider interaction ----

        void SliderSeek_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _seeking = true;
            try { SeekValue = ValueFromMouse(e.GetPosition(SliderSeek), SliderSeek.ActualWidth); } catch { }
            SeekStart?.Invoke();
        }

        void SliderSeek_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _seeking = false;
            SeekEnd?.Invoke(SliderSeek.Value);
        }

        void SliderSeek_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                var sec = ValueFromMouse(e.GetPosition(SliderSeek), SliderSeek.ActualWidth);
                SliderSeek.ToolTip = FormatSeconds(sec);
            }
            catch { }
        }

        void SliderSeek_MouseLeave(object sender, MouseEventArgs e)
        {
            try { SliderSeek.ClearValue(ToolTipProperty); } catch { }
        }

        double ValueFromMouse(Point p, double width)
        {
            var ratio = Math.Max(0, Math.Min(1, width > 0 ? p.X / width : 0));
            return ratio * (SliderSeek.Maximum - SliderSeek.Minimum) + SliderSeek.Minimum;
        }

        /// <summary>mm:ss / h:mm:ss caption for the hover tooltip.</summary>
        static string FormatSeconds(double sec)
        {
            if (sec < 0) sec = 0;
            var ts = TimeSpan.FromSeconds(sec);
            return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }

        // ---- volume interaction ----

        void SliderVolume_VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            VolumeChanged?.Invoke(SliderVolume.Volume);
        }

        void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            SetMuted(!_muted);
            VolumeChanged?.Invoke(SliderVolume.Volume);
            MuteChanged?.Invoke(_muted);
        }
    }
}
