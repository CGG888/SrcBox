using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
// The project's global usings pull in System.Windows.Forms and System.Drawing, which collide with WPF's
// namesakes; alias the WPF meaning so the control reads like the rest of the WPF code.
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace LibmpvIptvClient
{
    /// <summary>
    /// Reusable EPG timeline bar: the programme-relative meter (with quarter ticks), the scrollable programme
    /// strip, the detail row and the timeshift/volume interaction. It is hosted by the fullscreen floating
    /// overlay (<see cref="OverlayControls"/>) and by the in-layout bottom bar of <see cref="MainWindow"/>, so
    /// the two bars cannot drift apart again. The host owns the height: it listens to <see cref="LayoutChanged"/>
    /// and applies <see cref="RequiredHeight"/>.
    /// </summary>
    public partial class TimeshiftTimeline : UserControl
    {
        /// <summary>Raised when the user starts seeking on the meter; the host locks its tick-driven value updates.</summary>
        public event Action? SeekStart;

        /// <summary>Raised with the meter's own units (seconds from the meter minimum) when the user releases.</summary>
        public event Action<double>? SeekEnd;

        /// <summary>Raised when a playable programme card is clicked; the payload is the programme start (wall clock).</summary>
        public event Action<DateTime>? ProgrammeSelected;

        /// <summary>Raised whenever the preferred height changes (timeline shown/hidden, compact toggled).</summary>
        public event Action? LayoutChanged;

        /// <summary>Raised when the volume slider moved (the host mirrors it into the shell).</summary>
        public event Action<double>? VolumeChanged;

        /// <summary>Raised when the mute button was pressed (the host mirrors it into the shell).</summary>
        public event Action<bool>? MuteChanged;

        private const double MeterRowHeight = 20;
        private const double ScaleRowHeight = 16;
        private const double StripRowHeight = 64;
        private const double DetailRowHeight = 26;
        private const double StripRecentreHoldSeconds = 5;
        private const double StripWheelStep = 60;

        private readonly List<TimelineProgramme> _stripCards = new List<TimelineProgramme>();
        private string _stripSignature = string.Empty;
        private DateTime _stripHoldUntil = DateTime.MinValue;
        private DateTime _lastCentredCardStart = DateTime.MinValue;
        private double _programmaticOffset = double.NaN;
        private bool _recentrePending;
        private bool _timelineVisible;
        private bool _compactMode;
        private bool _meterHasProgrammeWindow;
        private DateTime _meterWindowStart;
        private bool _seeking;
        private bool _timeshiftOn;
        private bool _muted;
        private DateTime _tsMin;
        private DateTime _tsMax;
        private string _programmeName = string.Empty;

        private const string VolumeData = "M2,5 L2,11 L5,11 L9,15 L9,1 L5,5 Z M11,5 Q13,8 11,11 M14,3 Q17,8 14,13";
        private const string MuteData = "M2,5 L2,11 L5,11 L9,15 L9,1 L5,5 Z M11,3 L15,13 M15,3 L11,13";

        public TimeshiftTimeline()
        {
            InitializeComponent();
            PreviewMouseWheel += OnPreviewMouseWheel;
            try { BtnMute.Click += BtnMute_Click; } catch { }
            ApplyCompactMode(true);
        }

        // ---------------------------------------------------------------------
        // Public API (frozen: both hosts and the shell talk to these members)
        // ---------------------------------------------------------------------

        /// <summary>Meter value in the units <see cref="SeekEnd"/> reports (seconds from the meter minimum).</summary>
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

        /// <summary>Upper bound of the meter when it is not scoped to a programme window (0..duration).</summary>
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

        /// <summary>Lower bound of the meter (0 for plain playback, the timeshift offset while timeshifting).</summary>
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

        /// <summary>Compact = meter only (the old 80/64 px bar); false = ticks + strip + detail.</summary>
        public bool IsCompactMode => _compactMode;

        /// <summary>Height the host should reserve for the timeline itself right now.</summary>
        public double RequiredHeight
        {
            get
            {
                if (_compactMode) return MeterRowHeight;
                double h = MeterRowHeight;
                if (MeterScale.Visibility == Visibility.Visible) h += ScaleRowHeight;
                if (StripScroll.Visibility == Visibility.Visible) h += StripRowHeight;
                if (PnlProgrammeDetail.Visibility == Visibility.Visible) h += DetailRowHeight;
                return h;
            }
        }

        /// <summary>The meter element; hosts use it for pointer-relative seek math.</summary>
        public FrameworkElement SeekBar => SliderSeek;

        /// <summary>The embedded volume slider.</summary>
        public LibmpvIptvClient.Controls.VolumeSlider VolumeSlider => SliderVolume;

        /// <summary>The embedded mute button.</summary>
        public Button MuteButton => BtnMute;

        /// <summary>The muting state this control paints (the host mirrors it into the shell).</summary>
        public bool IsMuted => _muted;

        /// <summary>Compact mode keeps only the meter row and returns the host to its short bar height.</summary>
        public void SetCompactMode(bool compact)
        {
            try { ApplyCompactMode(compact); }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[Timeline] SetCompactMode failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies a full EPG timeline snapshot. Callers push this every playback tick; the strip itself is only
        /// rebuilt when <see cref="OverlayTimelineState.Signature"/> changes so it does not flicker.
        /// </summary>
        public void SetTimelineState(OverlayTimelineState? state)
        {
            try
            {
                if (state == null || state.Programmes == null || state.Programmes.Count == 0)
                {
                    // No schedule to show: fall back to the short meter-only bar (the old 80/64 px height).
                    SetCompactMode(true);
                    HideTimeline();
                    return;
                }

                // EPG data arrived: the full timeline (ticks + strip + detail) replaces the compact meter.
                SetCompactMode(false);
                EnsureStripBuilt(state.Programmes, state.Signature);
                ApplyStripState(state.Programmes);
                ShowTimelineRows();
                ApplyMeterWindow(state, state.Programmes);
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[Timeline] SetTimelineState failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Convenience entry point for hosts that only have the programme list and a cursor: the strip signature is
        /// derived from the list itself (identical lists therefore never rebuild the cards).
        /// </summary>
        public void SetTimeshiftSchedule(IReadOnlyList<TimelineProgramme> programmes, DateTime cursor)
        {
            SetTimelineState(new OverlayTimelineState
            {
                Programmes = programmes ?? Array.Empty<TimelineProgramme>(),
                Cursor = cursor,
                ProgrammeAnchored = true,
                Signature = OverlayTimelineState.BuildSignature(programmes)
            });
        }

        /// <summary>
        /// Scopes the meter to a programme window. <paramref name="cursorSeconds"/> uses the meter's own units:
        /// seconds from <paramref name="windowMin"/> while timeshifting, otherwise seconds from the programme start.
        /// </summary>
        public void SetProgrammeWindow(DateTime? programmeStart, DateTime? programmeEnd, DateTime? windowMin,
                                       double cursorSeconds, bool timeshiftActive)
        {
            try
            {
                if (!programmeStart.HasValue || !programmeEnd.HasValue) return;
                var start = programmeStart.Value;
                var end = programmeEnd.Value;
                if (end <= start) return;

                _timeshiftOn = timeshiftActive;
                _meterWindowStart = start;

                double min, max;
                if (timeshiftActive)
                {
                    var basis = windowMin ?? start;
                    _tsMin = basis;
                    min = (start - basis).TotalSeconds;
                    max = (end - basis).TotalSeconds;
                }
                else
                {
                    min = 0;
                    max = (end - start).TotalSeconds;
                }
                if (max <= min) return;

                _meterHasProgrammeWindow = true;
                SliderSeek.Minimum = min;
                SliderSeek.Maximum = max;
                if (!_seeking) SliderSeek.Value = Math.Max(min, Math.Min(max, cursorSeconds));
                UpdateMeterScale(start, end);
                UpdateProgrammeDetail(start, end);
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[Timeline] SetProgrammeWindow failed: {ex.Message}");
            }
        }

        /// <summary>Timeshift window bounds; the meter reports seconds measured from <paramref name="min"/>.</summary>
        public void SetTimeshiftRange(DateTime min, DateTime max)
        {
            _tsMin = min;
            _tsMax = max;
        }

        /// <summary>Mirrors the shell's timeshift state onto the control's own bookkeeping.</summary>
        public void SetTimeshift(bool on)
        {
            _timeshiftOn = on;
        }

        /// <summary>Progress in seconds; ignored while the meter is anchored to a programme or the user is seeking.</summary>
        public void SetTime(double current, double duration)
        {
            // While the meter is anchored to the currently playing programme its Minimum/Maximum belong to
            // that programme window, so the plain "0..mpv duration" mapping must not overwrite them.
            if (!_meterHasProgrammeWindow)
            {
                // Plain playback always maps 0..duration, so a window left over from a previous programme
                // (or from a timeshift session) must not keep the track scoped to its old offset.
                SliderSeek.Minimum = 0;
                SliderSeek.Maximum = duration <= 0 ? 1 : duration;
                if (!_seeking)
                {
                    SliderSeek.Value = Math.Max(0, Math.Min(SliderSeek.Maximum, current));
                }
            }
            try { OnProgressChanged(current, duration); } catch { }
        }

        /// <summary>Host hook: plain-playback progress tick (used for the host's own elapsed labels).</summary>
        protected virtual void OnProgressChanged(double current, double duration) { }

        /// <summary>Host hook: the meter moved while the user drags it; <paramref name="seconds"/> is in meter units.</summary>
        protected virtual void OnSeekValueChanged(double seconds) { }

        /// <summary>Sets the programme title shown in the detail row.</summary>
        public void SetProgrammeName(string name)
        {
            _programmeName = name ?? string.Empty;
            try { LblProgrammeName.Text = _programmeName; } catch { }
        }

        /// <summary>Sets the embedded volume slider without raising <see cref="VolumeChanged"/>.</summary>
        public void SetVolume(double val)
        {
            try { SliderVolume.Volume = val; } catch { }
        }

        /// <summary>Hosts that keep their own mute + volume controls (the windowed bar) hide this group.</summary>
        public void SetVolumeGroupVisible(bool visible)
        {
            try { VolumeGroup.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; } catch { }
        }

        /// <summary>Paints the mute state (icon, tint, volume slider).</summary>
        public void SetMuted(bool muted)
        {
            _muted = muted;
            try
            {
                IconMute.Data = _muted
                    ? System.Windows.Media.Geometry.Parse(MuteData)
                    : System.Windows.Media.Geometry.Parse(VolumeData);
                // 静音时按钮背景变为浅红色
                BtnMute.Background = _muted
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0x45, 0x3A))
                    : null;
                SliderVolume.IsMuted = _muted;
            }
            catch { }
        }

        // ---------------------------------------------------------------------
        // Timeline layout
        // ---------------------------------------------------------------------

        /// <summary>Rebuilds the strip items only when the schedule identity changed; keeps the existing cards otherwise.</summary>
        bool EnsureStripBuilt(IReadOnlyList<TimelineProgramme> programmes, string? signature)
        {
            var sig = string.IsNullOrEmpty(signature) ? OverlayTimelineState.BuildSignature(programmes) : signature!;
            bool rebuild = sig != _stripSignature || _stripCards.Count != programmes.Count;
            if (!rebuild) return false;

            _stripSignature = sig;
            _stripCards.Clear();
            for (int i = 0; i < programmes.Count; i++) _stripCards.Add(programmes[i]);
            StripItems.ItemsSource = null;
            StripItems.ItemsSource = _stripCards;
            _lastCentredCardStart = DateTime.MinValue;
            _recentrePending = true;
            StripScroll.UpdateLayout();
            RecentreStrip(_stripCards);
            return true;
        }

        /// <summary>Copies the mutating flags from the freshly built snapshot onto the retained cards.</summary>
        void ApplyStripState(IReadOnlyList<TimelineProgramme> programmes)
        {
            int count = Math.Min(_stripCards.Count, programmes.Count);
            for (int i = 0; i < count; i++)
            {
                var src = programmes[i];
                var dst = _stripCards[i];
                if (ReferenceEquals(src, dst)) continue;
                dst.CanPlay = src.CanPlay;
                dst.IsLive = src.IsLive;
                dst.IsCurrent = src.IsCurrent;
                dst.Name = src.Name;
                if (!string.Equals(dst.TimeText, src.TimeText, StringComparison.Ordinal)) dst.TimeText = src.TimeText;
            }
            RecentreStrip(_stripCards);
        }

        /// <summary>
        /// Centres the current programme card unless the user scrolled the strip within the last few seconds
        /// (mirrors the web remote's tsHoldBand behaviour).
        /// </summary>
        void RecentreStrip(IReadOnlyList<TimelineProgramme> cards)
        {
            if (cards == null || cards.Count == 0) return;
            if (DateTime.Now < _stripHoldUntil) return;

            TimelineProgramme? current = null;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].IsCurrent) { current = cards[i]; break; }
            }
            if (current == null) return;
            if (!_recentrePending && current.Start == _lastCentredCardStart) return;

            _lastCentredCardStart = current.Start;
            _recentrePending = false;
            ScrollCardIntoView(current);
        }

        void ScrollCardIntoView(TimelineProgramme card)
        {
            try
            {
                double offset = 0;
                foreach (var c in _stripCards)
                {
                    if (ReferenceEquals(c, card)) break;
                    offset += c.CardWidth + TimelineStripMetrics.CardGap;
                }
                double centred = offset - Math.Max(0, (StripScroll.ViewportWidth - card.CardWidth) / 2);
                double maxOffset = Math.Max(0, StripScroll.ExtentWidth - StripScroll.ViewportWidth);
                double target = Math.Max(0, Math.Min(maxOffset, centred));
                // Remember our own target so ScrollChanged does not mistake it for a manual scroll.
                _programmaticOffset = target;
                StripScroll.ScrollToHorizontalOffset(target);
            }
            catch { }
        }

        void ShowTimelineRows()
        {
            if (_compactMode)
            {
                // The host decides whether the compact bar has room for the strip: never grow on our own.
                StripScroll.Visibility = Visibility.Collapsed;
                PnlProgrammeDetail.Visibility = Visibility.Collapsed;
                return;
            }

            StripScroll.Visibility = Visibility.Visible;
            PnlProgrammeDetail.Visibility = Visibility.Visible;
            if (!_timelineVisible)
            {
                _timelineVisible = true;
                RaiseLayoutChanged();
            }
        }

        void HideTimeline()
        {
            _meterHasProgrammeWindow = false;
            MeterScale.Visibility = Visibility.Collapsed;
            // Without a programme window the meter falls back to the plain 0..duration mapping.
            try { SliderSeek.Minimum = 0; } catch { }
            if (!_timelineVisible) return;

            _timelineVisible = false;
            StripScroll.Visibility = Visibility.Collapsed;
            PnlProgrammeDetail.Visibility = Visibility.Collapsed;
            _stripCards.Clear();
            _stripSignature = string.Empty;
            _stripHoldUntil = DateTime.MinValue;
            _lastCentredCardStart = DateTime.MinValue;
            _recentrePending = false;
            _programmaticOffset = double.NaN;
            StripItems.ItemsSource = null;
            RaiseLayoutChanged();
        }

        void ApplyCompactMode(bool compact)
        {
            bool changed = _compactMode != compact;
            _compactMode = compact;
            try
            {
                RowScale.Height = compact ? new GridLength(0) : GridLength.Auto;
                RowStrip.Height = compact ? new GridLength(0) : GridLength.Auto;
                RowDetail.Height = compact ? new GridLength(0) : GridLength.Auto;
                if (compact)
                {
                    StripScroll.Visibility = Visibility.Collapsed;
                    PnlProgrammeDetail.Visibility = Visibility.Collapsed;
                }
                else if (_timelineVisible)
                {
                    StripScroll.Visibility = Visibility.Visible;
                    PnlProgrammeDetail.Visibility = Visibility.Visible;
                }
            }
            catch { }
            if (changed) RaiseLayoutChanged();
        }

        void RaiseLayoutChanged()
        {
            try { LayoutChanged?.Invoke(); } catch { }
        }

        /// <summary>Scopes the meter to the currently playing programme while keeping Value in the units SeekEnd reports.</summary>
        void ApplyMeterWindow(OverlayTimelineState state, IReadOnlyList<TimelineProgramme> programmes)
        {
            TimelineProgramme? current = null;
            for (int i = 0; i < programmes.Count; i++)
            {
                var p = programmes[i];
                if (state.Cursor >= p.Start && state.Cursor < p.End) { current = p; break; }
            }
            if (current == null) current = programmes.FirstOrDefault(p => p.IsCurrent);

            if (current == null || current.End <= current.Start || !state.ProgrammeAnchored)
            {
                _meterHasProgrammeWindow = false;
                MeterScale.Visibility = Visibility.Collapsed;
                return;
            }

            double min, max, value;
            if (_timeshiftOn && _tsMin != default(DateTime))
            {
                // Timeshift: SeekEnd consumers expect seconds measured from the timeshift window start.
                min = (current.Start - _tsMin).TotalSeconds;
                max = (current.End - _tsMin).TotalSeconds;
                value = (state.Cursor - _tsMin).TotalSeconds;
            }
            else
            {
                // Plain playback: the programme window becomes the meter's own 0..duration range.
                min = 0;
                max = (current.End - current.Start).TotalSeconds;
                value = (state.Cursor - current.Start).TotalSeconds;
            }
            if (max <= min)
            {
                _meterHasProgrammeWindow = false;
                MeterScale.Visibility = Visibility.Collapsed;
                return;
            }

            _meterWindowStart = current.Start;
            _meterHasProgrammeWindow = true;
            SliderSeek.Minimum = min;
            SliderSeek.Maximum = max;
            if (!_seeking) SliderSeek.Value = Math.Max(min, Math.Min(max, value));
            UpdateMeterScale(current.Start, current.End);
            UpdateProgrammeDetail(current.Start, current.End);
            if (!string.IsNullOrEmpty(current.Name)) _programmeName = current.Name;
        }

        /// <summary>Five ticks: programme start / 1/4 / 1/2 / 3/4 / end (endpoints always carry the date).</summary>
        void UpdateMeterScale(DateTime start, DateTime end)
        {
            var span = end - start;
            bool withDate = span >= TimeSpan.FromHours(12) || start.Date != end.Date;
            TxtScaleStart.Text = TimelineStripMetrics.FormatClock(start, true);
            TxtScaleQuarter.Text = TimelineStripMetrics.FormatClock(start.AddTicks(span.Ticks / 4), withDate);
            TxtScaleHalf.Text = TimelineStripMetrics.FormatClock(start.AddTicks(span.Ticks / 2), withDate);
            TxtScaleThreeQuarter.Text = TimelineStripMetrics.FormatClock(start.AddTicks(span.Ticks * 3 / 4), withDate);
            TxtScaleEnd.Text = TimelineStripMetrics.FormatClock(end, true);
            MeterScale.Visibility = Visibility.Visible;
        }

        void UpdateProgrammeDetail(DateTime start, DateTime end)
        {
            LblProgrammeName.Text = _programmeName;
            LblProgrammeRange.Text = TimelineStripMetrics.FormatClock(start, true)
                + " - " + TimelineStripMetrics.FormatClock(end, true);
        }

        /// <summary>Wall clock for a meter value, whatever range the meter currently reports.</summary>
        protected DateTime MeterWallClock(double meterValue)
        {
            var basis = _timeshiftOn || _meterWindowStart == default(DateTime) ? _tsMin : _meterWindowStart;
            return basis.AddSeconds(Math.Max(0, meterValue));
        }

        /// <summary>mm:ss / h:mm:ss caption used by the host's playback labels.</summary>
        protected static string FormatSeconds(double sec)
        {
            if (sec < 0) sec = 0;
            var ts = TimeSpan.FromSeconds(sec);
            return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }

        // ---- meter interaction ----

        void SliderSeek_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _seeking = true;
            try
            {
                SeekValue = ValueFromMouse(e.GetPosition(SliderSeek), SliderSeek.ActualWidth);
            }
            catch { }
            SeekStart?.Invoke();
        }

        void SliderSeek_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _seeking = false;
            SeekEnd?.Invoke(SliderSeek.Value);
        }

        void SliderSeek_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                var sec = ValueFromMouse(e.GetPosition(SliderSeek), SliderSeek.ActualWidth);
                if (_timeshiftOn || _meterHasProgrammeWindow)
                {
                    SliderSeek.ToolTip = MeterWallClock(sec).ToString("yyyy-MM-dd HH:mm:ss");
                }
                else
                {
                    SliderSeek.ToolTip = FormatSeconds(sec);
                }
            }
            catch { }
        }

        void SliderSeek_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try { SliderSeek.ClearValue(ToolTipProperty); } catch { }
        }

        void SliderSeek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_seeking) return;
            try { OnSeekValueChanged(SliderSeek.Value); } catch { }
        }

        double ValueFromMouse(Point p, double width)
        {
            var ratio = Math.Max(0, Math.Min(1, width > 0 ? p.X / width : 0));
            return ratio * (SliderSeek.Maximum - SliderSeek.Minimum) + SliderSeek.Minimum;
        }

        // ---- timeline strip interaction ----

        void MarkStripManualScroll()
        {
            _stripHoldUntil = DateTime.Now.AddSeconds(StripRecentreHoldSeconds);
            _recentrePending = true;
            _programmaticOffset = double.NaN;
        }

        void StripScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.HorizontalChange) < 0.01) return;
            if (!double.IsNaN(_programmaticOffset) && Math.Abs(StripScroll.HorizontalOffset - _programmaticOffset) < 0.5) return;
            // The user scrolled the strip: keep their position for a few seconds instead of snapping back.
            MarkStripManualScroll();
        }

        void StripScroll_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MarkStripManualScroll();
        }

        void StripScroll_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var card = ResolveTimelineCard(e.OriginalSource);
                if (card == null || !card.CanPlay) return;
                e.Handled = true;
                ProgrammeSelected?.Invoke(card.Start);
            }
            catch { }
        }

        static TimelineProgramme? ResolveTimelineCard(object? source)
        {
            var element = source as System.Windows.DependencyObject;
            while (element != null)
            {
                if (element is System.Windows.FrameworkElement fe && fe.DataContext is TimelineProgramme tp) return tp;
                element = element is System.Windows.Media.Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                    : null;
            }
            return null;
        }

        /// <summary>True when the pointer of a wheel event sits inside the visible programme strip.</summary>
        bool IsPointerOverStrip(System.Windows.Input.MouseWheelEventArgs e)
        {
            try
            {
                if (StripScroll.Visibility != Visibility.Visible) return false;
                var p = e.GetPosition(StripScroll);
                return p.X >= 0 && p.Y >= 0 && p.X <= StripScroll.ActualWidth && p.Y <= StripScroll.ActualHeight;
            }
            catch { return false; }
        }

        /// <summary>
        /// Wheel over the strip scrolls it horizontally, anywhere else keeps the host's old volume-wheel behaviour.
        /// Returns true when the wheel was consumed by the strip.
        /// </summary>
        public bool HandleWheel(System.Windows.Input.MouseWheelEventArgs e)
        {
            if (!_timelineVisible || !IsPointerOverStrip(e)) return false;
            MarkStripManualScroll();
            double delta = e.Delta > 0 ? -StripWheelStep : StripWheelStep;
            StripScroll.ScrollToHorizontalOffset(Math.Max(0, StripScroll.HorizontalOffset + delta));
            e.Handled = true;
            return true;
        }

        void OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // The bubbling preview event stops here; the host's own handler runs first (tunneling) for the overlay.
            try { HandleWheel(e); } catch { }
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

    /// <summary>
    /// One programme card of the EPG timeline. The hosts build these snapshots and the control keeps
    /// the instances it already laid out, so the state flags must be observable (INotifyPropertyChanged).
    /// </summary>
    public sealed class TimelineProgramme : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _timeText = string.Empty;
        private bool _canPlay = true;
        private bool _isCurrent;
        private bool _isLive;

        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } }
        }

        /// <summary>Precomputed card caption: "HH:mm", or "MM-DD HH:mm" when the day changes.</summary>
        public string TimeText
        {
            get => _timeText;
            set { if (_timeText != value) { _timeText = value; OnPropertyChanged(nameof(TimeText)); } }
        }

        /// <summary>False marks the card as unavailable for timeshift (dashed, muted, not clickable).</summary>
        public bool CanPlay
        {
            get => _canPlay;
            set { if (_canPlay != value) { _canPlay = value; OnPropertyChanged(nameof(CanPlay)); } }
        }

        /// <summary>True for the programme the playback cursor currently sits in.</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set { if (_isCurrent != value) { _isCurrent = value; OnPropertyChanged(nameof(IsCurrent)); } }
        }

        /// <summary>True for the programme that is on air right now.</summary>
        public bool IsLive
        {
            get => _isLive;
            set { if (_isLive != value) { _isLive = value; OnPropertyChanged(nameof(IsLive)); } }
        }

        public double CardWidth => TimelineStripMetrics.CardWidth(End - Start);

        public event PropertyChangedEventHandler? PropertyChanged;

        void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Layout metrics of the EPG timeline strip; mirrors the web remote's TS_* constants.</summary>
    public static class TimelineStripMetrics
    {
        public const double PixelsPerMinute = 2.0;
        public const double MinCardWidth = 84.0;
        public const double MaxCardWidth = 220.0;
        public const double CardGap = 2.0;

        /// <summary>Duration proportional width in DIPs, clamped to [MinCardWidth, MaxCardWidth].</summary>
        public static double CardWidth(TimeSpan duration)
            => Math.Max(MinCardWidth, Math.Min(MaxCardWidth, duration.TotalMinutes * PixelsPerMinute));

        /// <summary>Timeline caption: time only, or "M-dd HH:mm" when the date matters (mirrors the web scale).</summary>
        public static string FormatClock(DateTime value, bool withDate)
            => withDate ? value.ToString("M-dd HH:mm") : value.ToString("HH:mm");
    }

    /// <summary>Snapshot pushed from the shell each playback tick to feed the timeline strip.</summary>
    public sealed class OverlayTimelineState
    {
        public IReadOnlyList<TimelineProgramme> Programmes { get; set; } = Array.Empty<TimelineProgramme>();

        /// <summary>Wall-clock cursor that the meter and the current-card highlight follow.</summary>
        public DateTime Cursor { get; set; }

        /// <summary>True when the meter may be anchored to the current programme's start/end instead of mpv's duration.</summary>
        public bool ProgrammeAnchored { get; set; }

        /// <summary>Strip content identity (programme starts); any change rebuilds the cards.</summary>
        public string Signature { get; set; } = string.Empty;

        public static string BuildSignature(IReadOnlyList<TimelineProgramme>? programmes)
        {
            if (programmes == null || programmes.Count == 0) return string.Empty;
            var sb = new StringBuilder(programmes.Count * 20);
            sb.Append(programmes.Count).Append('|');
            for (int i = 0; i < programmes.Count; i++) sb.Append(programmes[i].Start.Ticks).Append(',');
            return sb.ToString();
        }
    }
}
