using Button = System.Windows.Controls.Button;

namespace LibmpvIptvClient
{
    /// <summary>
    /// What a host bottom bar needs from whichever progress meter is on screen: the plain
    /// <see cref="StandardSeekBar"/> or the EPG <see cref="TimeshiftTimeline"/>. Keeping this surface tiny is the
    /// point - the two controls share these few members and nothing else, so a layout change inside one of them
    /// cannot reach the other.
    /// </summary>
    public interface ISeekMeter
    {
        /// <summary>Height the host has to reserve for this meter.</summary>
        double RequiredHeight { get; }

        /// <summary>The meter's mute button.</summary>
        Button MuteButton { get; }

        /// <summary>The meter's volume slider.</summary>
        Controls.VolumeSlider VolumeSlider { get; }

        /// <summary>Plain progress: <paramref name="current"/> of <paramref name="duration"/> seconds.</summary>
        void SetTime(double current, double duration);

        /// <summary>Sets the volume slider without raising the host's volume-changed event.</summary>
        void SetVolume(double volume);

        /// <summary>Paints the mute state.</summary>
        void SetMuted(bool muted);
    }
}
