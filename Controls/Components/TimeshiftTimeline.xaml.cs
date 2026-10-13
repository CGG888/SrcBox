using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
// The project's global usings pull in System.Windows.Forms and System.Drawing, which collide with WPF's
// namesakes; alias the WPF meaning so the control reads like the rest of the WPF code.
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;
using TextBlock = System.Windows.Controls.TextBlock;

namespace LibmpvIptvClient
{
    /// <summary>
    /// Reusable programme timeline bar: a compact rtp2httpd-style EPG band (programme segments carrying their
    /// names, a ruler with real time labels and prev/next segment buttons on both ends) plus the plain meter it
    /// falls back to when the channel has no schedule. It is hosted by the fullscreen floating overlay
    /// (<see cref="OverlayControls"/>) and by the in-layout bottom bar of <see cref="MainWindow"/>, so the two
    /// bars cannot drift apart again. The host owns the height: it listens to <see cref="LayoutChanged"/> and
    /// applies <see cref="RequiredHeight"/>.
    /// </summary>
    public partial class TimeshiftTimeline : UserControl
    {
        /// <summary>Raised when the user starts dragging the band; the host locks its tick-driven value updates until the release.</summary>
        public event Action? SeekStart;

        /// <summary>Raised with the meter's own units (seconds from the meter minimum) when the user releases.</summary>
        public event Action<double>? SeekEnd;

        /// <summary>Raised when a playable programme segment is clicked; the payload is the programme start (wall clock).</summary>
        public event Action<DateTime>? ProgrammeSelected;

        /// <summary>Raised whenever the preferred height changes (band shown/hidden, compact toggled).</summary>
        public event Action? LayoutChanged;

        /// <summary>Raised when the volume slider moved (the host mirrors it into the shell).</summary>
        public event Action<double>? VolumeChanged;

        /// <summary>Raised when the mute button was pressed (the host mirrors it into the shell).</summary>
        public event Action<bool>? MuteChanged;

        /// <summary>Height of the plain meter row (the classic compact bottom bar).</summary>
        private const double MeterRowHeight = 20;

        // The band is deliberately the height of the old seek bar (20 px) plus one short label row and one
        // short tick row: 16 (name + prev/next) + 13 (ruler) + 20 (segments) = 49.
        private const double NavRowHeight = 16;
        private const double RulerRowHeight = 13;
        private const double TrackRowHeight = 20;

        /// <summary>Total height of the programme band (used for <see cref="RequiredHeight"/>).</summary>
        public const double BandRowHeight = NavRowHeight + RulerRowHeight + TrackRowHeight;

        private const double BlockHeight = 18;
        private const double MinimumLabelWidth = 40;
        private const double LabelCollisionWidth = 34;
        private const double WheelPanMinutes = 15;

        private readonly List<TimelineProgramme> _programmes = new List<TimelineProgramme>();
        private readonly Dictionary<long, TimelineProgramme> _programmeCache = new Dictionary<long, TimelineProgramme>();
        private DataTemplate? _blockTemplate;

        private bool _compactMode = true;
        private string _scheduleSignature = string.Empty;

        private DateTime _cursor = DateTime.Now;
        private TimelineProgramme? _currentProgramme;

        // Viewport (recomputed from the band width on every render).
        private DateTime _windowStart = DateTime.Now;
        private TimeSpan _windowSpan = TimeSpan.FromMinutes(TimelineBandMetrics.WideWindowHalfMinutes * 2);
        private int _tickMinutes = TimelineBandMetrics.TickMinuteOptions[2];
        private string _bandKey = string.Empty;

        // Manual window: set by the prev/next buttons, a pan drag or the wheel, released on a schedule change.
        private bool _manualWindow;
        private DateTime _manualAnchor = DateTime.Now;

        // Pointer state.
        private bool _pointerDown;
        private bool _dragMoved;
        private bool _dragIsSeek;
        private bool _seekStarted;
        private double _dragStartX;
        private double _dragStartPercent;
        private DateTime _dragStartAnchor;
        private DateTime? _previewTime;

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

        /// <summary>Compact = the plain meter only (the old 64 px bar); false = the programme band.</summary>
        public bool IsCompactMode => _compactMode;

        /// <summary>True while the user has panned the window away from the playback cursor.</summary>
        public bool IsManualWindow => _manualWindow;

        /// <summary>Height the host should reserve for the timeline itself right now.</summary>
        public double RequiredHeight => _compactMode ? MeterRowHeight : BandRowHeight;

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

        /// <summary>Drops the manual window so the band follows the playback cursor again.</summary>
        public void ResetView()
        {
            if (!_manualWindow) return;
            DropManualWindow("reset requested");
            Render();
        }

        /// <summary>
        /// Applies a full EPG timeline snapshot. Callers push this every playback tick; the band itself is only
        /// rebuilt when the window or the schedule identity changes so it does not flicker.
        /// </summary>
        public void SetTimelineState(OverlayTimelineState? state)
        {
            try
            {
                if (state == null || state.Programmes == null || state.Programmes.Count == 0)
                {
                    // No schedule to show: fall back to the short meter-only bar (the old 64 px height).
                    SetCompactMode(true);
                    HideBand();
                    return;
                }

                bool scheduleChanged = !string.Equals(state.Signature, _scheduleSignature, StringComparison.Ordinal);
                SyncProgrammes(state.Programmes, scheduleChanged);
                _scheduleSignature = state.Signature ?? string.Empty;
                if (scheduleChanged) DropManualWindow("schedule changed");

                _cursor = state.Cursor;

                SetCompactMode(false);
                ApplyMeterWindow(state);
                Render();
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[Timeline] SetTimelineState failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Convenience entry point for hosts that only have the programme list and a cursor: the schedule signature
        /// is derived from the list itself (identical lists therefore never rebuild the segments).
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

        /// <summary>Sets the programme title shown at the head of the band.</summary>
        public void SetProgrammeName(string name)
        {
            _programmeName = name ?? string.Empty;
            try { TxtProgrammeName.Text = _programmeName; } catch { }
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
        // Band layout
        // ---------------------------------------------------------------------

        /// <summary>Keeps one <see cref="TimelineProgramme"/> instance per programme start so the block triggers stay live.</summary>
        void SyncProgrammes(IReadOnlyList<TimelineProgramme> incoming, bool scheduleChanged)
        {
            if (scheduleChanged) _programmeCache.Clear();
            _programmes.Clear();

            for (int i = 0; i < incoming.Count; i++)
            {
                var src = incoming[i];
                if (src == null || src.End <= src.Start) continue;

                long key = src.Start.Ticks;
                if (!_programmeCache.TryGetValue(key, out var dst))
                {
                    dst = new TimelineProgramme { Start = src.Start, End = src.End };
                    _programmeCache[key] = dst;
                }
                dst.End = src.End;
                dst.Name = src.Name;
                dst.TimeText = src.TimeText;
                dst.CanPlay = src.CanPlay;
                dst.IsCurrent = src.IsCurrent;
                dst.IsLive = src.IsLive;
                _programmes.Add(dst);
            }
        }

        /// <summary>Recomputes the viewport and repaints the band.</summary>
        void Render()
        {
            try
            {
                if (_compactMode) return;
                double width = TrackCanvas.ActualWidth;
                if (width <= 0) return;

                int half = TimelineBandMetrics.WindowHalfMinutesFor(width);
                var span = TimeSpan.FromMinutes(half * 2);
                var anchor = _manualWindow ? _manualAnchor : _cursor;
                int tick = TimelineBandMetrics.TickMinutesFor(span);
                var start = TimelineBandMetrics.SnapToTick(anchor - TimeSpan.FromTicks(span.Ticks / 2), tick);

                _windowSpan = span;
                _tickMinutes = tick;
                _windowStart = start;

                string key = start.Ticks + "|" + span.Ticks + "|" + tick + "|" + _scheduleSignature + "|" + _programmes.Count;
                if (!string.Equals(key, _bandKey, StringComparison.Ordinal))
                {
                    _bandKey = key;
                    RebuildRuler();
                    RebuildBlocks();
                }
                UpdateMarkers();
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[Timeline] render failed: {ex.Message}");
            }
        }

        void RebuildRuler()
        {
            RulerCanvas.Children.Clear();
            double width = TrackCanvas.ActualWidth;
            double height = RulerCanvas.ActualHeight;
            if (width <= 0 || height <= 0 || _tickMinutes <= 0) return;

            int count = (int) Math.Round(_windowSpan.TotalMinutes / _tickMinutes);
            if (count <= 0) return;
            double spacing = width / count;

            for (int i = 0; i <= count; i++)
            {
                var time = _windowStart.AddMinutes(i * _tickMinutes);
                double x = TimelineBandMetrics.PercentFor(_windowStart, _windowSpan, time) * width;

                var notch = new Rectangle { Width = 1, Height = 3 };
                notch.SetResourceReference(Shape.FillProperty, "BorderBrushCommon");
                Canvas.SetLeft(notch, Math.Round(x));
                Canvas.SetTop(notch, height - 3);
                RulerCanvas.Children.Add(notch);

                if (spacing < LabelCollisionWidth) continue;

                var caption = TimelineBandMetrics.FormatTickLabel(time, _windowStart);
                var label = new TextBlock
                {
                    Text = caption,
                    FontSize = 9.5,
                    Width = Math.Max(spacing, caption.Length * 6.2),
                    TextAlignment = TextAlignment.Center
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                double left = x - label.Width / 2;
                left = Math.Max(0, Math.Min(width - label.Width, left));
                Canvas.SetLeft(label, left);
                Canvas.SetTop(label, 0);
                RulerCanvas.Children.Add(label);
            }
        }

        void RebuildBlocks()
        {
            TrackCanvas.Children.Clear();
            double width = TrackCanvas.ActualWidth;
            double height = TrackCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            var plate = new Border
            {
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                Width = width,
                Height = height
            };
            plate.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            plate.SetResourceReference(Border.BorderBrushProperty, "BorderBrushCommon");
            Canvas.SetLeft(plate, 0);
            Canvas.SetTop(plate, 0);
            TrackCanvas.Children.Add(plate);

            _blockTemplate ??= Resources["TimelineProgrammeTemplate"] as DataTemplate;
            if (_blockTemplate == null) return;

            for (int i = 0; i < _programmes.Count; i++)
            {
                var p = _programmes[i];
                double leftPercent = TimelineBandMetrics.PercentFor(_windowStart, _windowSpan, p.Start);
                double rightPercent = TimelineBandMetrics.PercentFor(_windowStart, _windowSpan, p.End);
                if (rightPercent <= 0d || leftPercent >= 1d) continue;

                double left = TimelineBandMetrics.ClampPercent(leftPercent) * width;
                double right = TimelineBandMetrics.ClampPercent(rightPercent) * width;
                double blockWidth = Math.Max(TimelineBandMetrics.MinimumBlockWidth, right - left);

                var presenter = new ContentPresenter
                {
                    Content = p,
                    ContentTemplate = _blockTemplate,
                    Width = blockWidth,
                    Height = BlockHeight
                };
                Canvas.SetLeft(presenter, left);
                Canvas.SetTop(presenter, (height - BlockHeight) / 2);
                TrackCanvas.Children.Add(presenter);
            }
        }

        /// <summary>Repositions the playhead, the current programme frame/fill and the live edge.</summary>
        void UpdateMarkers()
        {
            double width = TrackCanvas.ActualWidth;
            double height = TrackCanvas.ActualHeight;
            if (width <= 0 || height <= 0 || _windowSpan.Ticks <= 0) return;

            var windowEnd = _windowStart + _windowSpan;
            TxtWindowRange.Text = TimelineBandMetrics.FormatWindowRange(_windowStart, _windowSpan);
            TxtProgrammeName.Text = _programmeName;

            // Playhead: follows the drag preview while seeking, the playback cursor otherwise.
            var probe = _previewTime ?? _cursor;
            double playPercent = TimelineBandMetrics.PercentFor(_windowStart, _windowSpan, probe);
            bool playVisible = probe >= _windowStart && probe <= windowEnd;
            double playX = TimelineBandMetrics.ClampPercent(playPercent) * width;
            Playhead.Visibility = playVisible ? Visibility.Visible : Visibility.Collapsed;
            PlayheadHandle.Visibility = Playhead.Visibility;
            Playhead.Height = height;
            Canvas.SetLeft(Playhead, Math.Round(playX) - 1);
            Canvas.SetTop(Playhead, 0);
            PlayheadHandle.Height = 6;
            Canvas.SetLeft(PlayheadHandle, Math.Round(playX) - 3.5);
            Canvas.SetTop(PlayheadHandle, 0);

            // Current programme: an accent frame plus a progress fill up to the playhead.
            var current = _currentProgramme;
            if (current == null || current.End <= current.Start)
            {
                CurrentFrame.Visibility = Visibility.Collapsed;
                ProgressFill.Visibility = Visibility.Collapsed;
            }
            else
            {
                double blockStart = TimelineBandMetrics.PercentFor(_windowStart, _windowSpan, current.Start);
                double blockEnd = TimelineBandMetrics.PercentFor(_windowStart, _windowSpan, current.End);
                double left = TimelineBandMetrics.ClampPercent(blockStart) * width;
                double right = TimelineBandMetrics.ClampPercent(blockEnd) * width;
                bool visible = blockEnd > 0d && blockStart < 1d && right > left;

                if (!visible)
                {
                    CurrentFrame.Visibility = Visibility.Collapsed;
                    ProgressFill.Visibility = Visibility.Collapsed;
                }
                else
                {
                    double frameWidth = Math.Max(2d, right - left);
                    CurrentFrame.Width = frameWidth;
                    CurrentFrame.Height = height;
                    Canvas.SetLeft(CurrentFrame, left);
                    Canvas.SetTop(CurrentFrame, 0);
                    CurrentFrame.Visibility = Visibility.Visible;

                    double fillTo = TimelineBandMetrics.ClampPercent(Math.Max(blockStart, Math.Min(blockEnd, playPercent)));
                    double fillWidth = fillTo * width - left;
                    if (fillWidth >= 1d)
                    {
                        ProgressFill.Width = fillWidth;
                        ProgressFill.Height = Math.Max(1d, height - 4);
                        Canvas.SetLeft(ProgressFill, left + 1);
                        Canvas.SetTop(ProgressFill, 2);
                        ProgressFill.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        ProgressFill.Visibility = Visibility.Collapsed;
                    }
                }
            }

            // Live edge: where "now" sits inside the window.
            var now = DateTime.Now;
            if (now >= _windowStart && now <= windowEnd)
            {
                double nowX = TimelineBandMetrics.ClampPercent(TimelineBandMetrics.PercentFor(_windowStart, _windowSpan, now)) * width;
                LiveEdge.Height = height;
                Canvas.SetLeft(LiveEdge, Math.Round(nowX) - 1);
                Canvas.SetTop(LiveEdge, 0);
                LiveEdge.Visibility = Visibility.Visible;
            }
            else
            {
                LiveEdge.Visibility = Visibility.Collapsed;
            }
        }

        void ApplyCompactMode(bool compact)
        {
            bool changed = _compactMode != compact;
            _compactMode = compact;
            try
            {
                RowBand.Height = compact ? new GridLength(0) : GridLength.Auto;
                if (compact)
                {
                    Band.Visibility = Visibility.Collapsed;
                    SliderSeek.Visibility = Visibility.Visible;
                }
                else
                {
                    Band.Visibility = Visibility.Visible;
                    SliderSeek.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
            if (changed) RaiseLayoutChanged();
        }

        void HideBand()
        {
            _meterHasProgrammeWindow = false;
            _currentProgramme = null;
            _programmes.Clear();
            _programmeCache.Clear();
            _scheduleSignature = string.Empty;
            _bandKey = string.Empty;
            DropManualWindow("band hidden");
            try { RulerCanvas.Children.Clear(); } catch { }
            try { TrackCanvas.Children.Clear(); } catch { }
            try { SliderSeek.Minimum = 0; } catch { }
        }

        void RaiseLayoutChanged()
        {
            try { LayoutChanged?.Invoke(); } catch { }
        }

        /// <summary>Scopes the meter to the currently playing programme while keeping Value in the units SeekEnd reports.</summary>
        void ApplyMeterWindow(OverlayTimelineState state)
        {
            _currentProgramme = null;
            int index = TimelineBandMetrics.FindProgrammeIndex(_programmes, state.Cursor);
            if (index < 0)
            {
                for (int i = 0; i < _programmes.Count; i++)
                {
                    if (_programmes[i].IsCurrent) { index = i; break; }
                }
            }
            if (index >= 0) _currentProgramme = _programmes[index];

            var current = _currentProgramme;
            // The name is shown whatever the meter anchors to: live playback is deliberately unanchored (the host
            // still wants the plain 0..duration meter), but the band's caption must name what is on right now.
            if (current != null && !string.IsNullOrEmpty(current.Name)) _programmeName = current.Name;
            if (current == null || current.End <= current.Start || !state.ProgrammeAnchored)
            {
                _meterHasProgrammeWindow = false;
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
                // Plain playback / replay: the programme window becomes the meter's own 0..duration range.
                min = 0;
                max = (current.End - current.Start).TotalSeconds;
                value = (state.Cursor - current.Start).TotalSeconds;
            }
            if (max <= min)
            {
                _meterHasProgrammeWindow = false;
                return;
            }

            _meterWindowStart = current.Start;
            _meterHasProgrammeWindow = true;
            SliderSeek.Minimum = min;
            SliderSeek.Maximum = max;
            if (!_seeking) SliderSeek.Value = Math.Max(min, Math.Min(max, value));
        }

        /// <summary>Wall-clock seconds for the meter, in the units <see cref="SeekEnd"/> is documented with.</summary>
        double MeterSecondsFor(DateTime wall)
        {
            if (_timeshiftOn && _tsMin != default(DateTime)) return Math.Max(0, (wall - _tsMin).TotalSeconds);
            if (_currentProgramme != null) return Math.Max(0, (wall - _currentProgramme.Start).TotalSeconds);
            return Math.Max(0, (wall - _meterWindowStart).TotalSeconds);
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

        // ---------------------------------------------------------------------
        // Band interaction
        // ---------------------------------------------------------------------

        /// <summary>True when a drag on the band can actually seek (timeshift or programme-relative replay).</summary>
        bool SeekEnabled => _timeshiftOn || _meterHasProgrammeWindow;

        void MarkManualWindow(DateTime anchor)
        {
            _manualWindow = true;
            _manualAnchor = anchor;
        }

        /// <summary>Leaves the browsing window and lets the band follow the playback cursor again.</summary>
        void DropManualWindow(string reason)
        {
            if (!_manualWindow) return;
            _manualWindow = false;
            LibmpvIptvClient.Diagnostics.Logger.Info($"[Timeline] browse window dropped: {reason}");
        }

        void BtnPrevSegment_Click(object sender, RoutedEventArgs e) => PageSegment(-1);

        void BtnNextSegment_Click(object sender, RoutedEventArgs e) => PageSegment(1);

        /// <summary>
        /// Moves the visible window to the previous/next programme without touching the playback position;
        /// clicking a segment is still the only way to seek.
        /// </summary>
        void PageSegment(int direction)
        {
            try
            {
                var anchor = _manualWindow ? _manualAnchor : _cursor;
                var target = TimelineBandMetrics.PageAnchor(_programmes, anchor, _windowSpan, direction);
                LibmpvIptvClient.Diagnostics.Logger.Info($"[Timeline] page {direction} anchor={anchor:HH:mm} -> {target:HH:mm}");
                MarkManualWindow(target);
                Render();
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Debug($"[Timeline] segment page failed: {ex.Message}");
            }
        }

        void TrackCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

        void TrackCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_compactMode) return;
            double width = TrackCanvas.ActualWidth;
            if (width <= 0) return;

            var p = e.GetPosition(TrackCanvas);
            _pointerDown = true;
            _dragMoved = false;
            _seekStarted = false;
            _dragStartX = p.X;
            _dragStartAnchor = _manualWindow ? _manualAnchor : _cursor;
            _dragStartPercent = TimelineBandMetrics.ClampPercent(p.X / width);

            int index = TimelineBandMetrics.FindProgrammeIndexAtPercent(_programmes, _windowStart, _windowSpan, _dragStartPercent);
            _dragIsSeek = index >= 0 && SeekEnabled;
            _previewTime = _dragIsSeek
                ? TimelineBandMetrics.TimeAtPercent(_windowStart, _windowSpan, _dragStartPercent)
                : (DateTime?) null;

            try { TrackCanvas.CaptureMouse(); } catch { }
            e.Handled = true;
        }

        void TrackCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (!_pointerDown)
                {
                    UpdateTrackTooltip(e);
                    return;
                }

                double width = TrackCanvas.ActualWidth;
                if (width <= 0) return;
                var p = e.GetPosition(TrackCanvas);
                double dx = p.X - _dragStartX;
                if (!_dragMoved && Math.Abs(dx) < TimelineBandMetrics.DragThresholdPx) return;
                _dragMoved = true;

                if (_dragIsSeek)
                {
                    if (!_seekStarted)
                    {
                        _seekStarted = true;
                        SeekStart?.Invoke();
                    }
                    var target = TimelineBandMetrics.TimeAtPercent(_windowStart, _windowSpan,
                                                                   TimelineBandMetrics.ClampPercent(p.X / width));
                    var now = DateTime.Now;
                    if (target > now) target = now; // never seek into the future
                    _previewTime = target;
                    UpdateMarkers();
                }
                else
                {
                    // Panning the window: the pointer moves the timeline, not the playback position.
                    double minutes = -dx / width * _windowSpan.TotalMinutes;
                    MarkManualWindow(_dragStartAnchor + TimeSpan.FromMinutes(minutes));
                    Render();
                }
            }
            catch { }
        }

        void TrackCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (!_pointerDown) return;
                _pointerDown = false;
                try { TrackCanvas.ReleaseMouseCapture(); } catch { }

                if (_dragIsSeek)
                {
                    if (_seekStarted && _previewTime.HasValue)
                    {
                        var target = _previewTime.Value;
                        _previewTime = null;
                        SeekValue = MeterSecondsFor(target);
                        UpdateMarkers();
                        SeekEnd?.Invoke(SeekValue);
                    }
                    else
                    {
                        // A tap: select the programme instead of seeking into it.
                        _previewTime = null;
                        UpdateMarkers();
                        int index = TimelineBandMetrics.FindProgrammeIndexAtPercent(_programmes, _windowStart, _windowSpan, _dragStartPercent);
                        if (index >= 0)
                        {
                            var programme = _programmes[index];
                            if (programme.CanPlay) ProgrammeSelected?.Invoke(programme.Start);
                        }
                    }
                }
                else if (_dragMoved)
                {
                    // A pan drag moved the window; the pointer release has nothing left to commit.
                }
            }
            catch { }
            finally
            {
                _dragMoved = false;
                _dragIsSeek = false;
                _seekStarted = false;
                _previewTime = null;
                e.Handled = true;
            }
        }

        void TrackCanvas_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try { TrackCanvas.ClearValue(ToolTipProperty); } catch { }
        }

        void UpdateTrackTooltip(System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                double width = TrackCanvas.ActualWidth;
                if (width <= 0) return;
                var p = e.GetPosition(TrackCanvas);
                var time = TimelineBandMetrics.TimeAtPercent(_windowStart, _windowSpan,
                                                             TimelineBandMetrics.ClampPercent(p.X / width));
                int index = TimelineBandMetrics.FindProgrammeIndex(_programmes, time);
                if (index >= 0 && !string.IsNullOrEmpty(_programmes[index].Name))
                {
                    TrackCanvas.ToolTip = _programmes[index].Name + "  " + time.ToString("HH:mm");
                }
                else
                {
                    TrackCanvas.ToolTip = time.ToString("HH:mm");
                }
            }
            catch { }
        }

        /// <summary>
        /// Wheel over the band pans the visible window; anywhere else keeps the host's old volume-wheel
        /// behaviour. Returns true when the wheel was consumed by the band.
        /// </summary>
        public bool HandleWheel(System.Windows.Input.MouseWheelEventArgs e)
        {
            try
            {
                if (_compactMode || Band.Visibility != Visibility.Visible) return false;
                var p = e.GetPosition(TrackCanvas);
                if (p.X < 0 || p.Y < 0 || p.X > TrackCanvas.ActualWidth || p.Y > TrackCanvas.ActualHeight) return false;

                var anchor = _manualWindow ? _manualAnchor : _cursor;
                var delta = TimeSpan.FromMinutes(e.Delta > 0 ? -WheelPanMinutes : WheelPanMinutes);
                MarkManualWindow(anchor + delta);
                Render();
                e.Handled = true;
                return true;
            }
            catch { return false; }
        }

        void OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // The bubbling preview event stops here; the host's own handler runs first (tunneling) for the overlay.
            try { HandleWheel(e); } catch { }
        }

        // ---- plain meter interaction (compact fallback) ----

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
    /// One programme segment of the EPG timeline. The hosts build these snapshots and the control keeps
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

        /// <summary>Precomputed caption: "HH:mm", or "MM-DD HH:mm" when the day changes.</summary>
        public string TimeText
        {
            get => _timeText;
            set { if (_timeText != value) { _timeText = value; OnPropertyChanged(nameof(TimeText)); } }
        }

        /// <summary>Tooltip of a segment: the programme name plus its full range.</summary>
        public string TooltipText
        {
            get
            {
                var range = TimelineStripMetrics.FormatClock(Start, true) + " - " + TimelineStripMetrics.FormatClock(End, true);
                return string.IsNullOrEmpty(_name) ? range : _name + "  " + range;
            }
        }

        /// <summary>False marks the segment as unavailable for timeshift (dashed, muted, not clickable).</summary>
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

        /// <summary>Duration proportional width in DIPs; kept for hosts that still lay segments out in a strip.</summary>
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

    /// <summary>Snapshot pushed from the shell each playback tick to feed the timeline band.</summary>
    public sealed class OverlayTimelineState
    {
        public IReadOnlyList<TimelineProgramme> Programmes { get; set; } = Array.Empty<TimelineProgramme>();

        /// <summary>Wall-clock cursor that the playhead and the current-segment highlight follow.</summary>
        public DateTime Cursor { get; set; }

        /// <summary>True when the meter may be anchored to the current programme's start/end instead of mpv's duration.</summary>
        public bool ProgrammeAnchored { get; set; }

        /// <summary>Band content identity (channel + programme starts); any change rebuilds the segments.</summary>
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
