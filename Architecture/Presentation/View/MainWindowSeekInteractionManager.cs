using System;
using System.Windows;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

namespace LibmpvIptvClient.Architecture.Presentation.View
{
    /// <summary>
    /// Windowed-bar seek wiring. The windowed bottom bar hosts the same <see cref="TimeshiftTimeline"/> control
    /// as the fullscreen overlay, so this manager listens to the control's own seek events instead of reading a
    /// bare <c>Slider</c>: the meter already reports its value in meter units (seconds from <c>Minimum</c>), which
    /// is exactly what the timeshift / replay conversions below need.
    /// </summary>
    public class MainWindowSeekInteractionManager
    {
        private readonly MainWindow _window;
        private readonly MainShellViewModel _shell;

        public MainWindowSeekInteractionManager(MainWindow window, MainShellViewModel shell)
        {
            _window = window;
            _shell = shell;
        }

        /// <summary>The user grabbed the meter: lock the tick-driven position updates until the release.</summary>
        public void Timeline_SeekStart()
        {
            _shell.IsSeeking = true;
        }

        /// <summary>
        /// The user released the meter. <paramref name="seconds"/> is in the meter's own units (seconds
        /// from <c>Minimum</c>); the conversion back to a wall clock time lives in
        /// <see cref="MainWindowChannelPlaybackActionsViewModel.SeekToMeterValue"/> and is shared with the
        /// fullscreen overlay bar.
        /// </summary>
        public void Timeline_SeekEnd(double seconds)
        {
            if (_window.PlayerInterop == null) return;
            _shell.IsSeeking = false;
            var v = Math.Max(0, seconds);
            _shell.SeekValue = v;
            _shell.ChannelPlaybackActions.SeekToMeterValue(v);
        }
    }
}
