using System;

namespace LibmpvIptvClient.Helpers
{
    /// <summary>
    /// Maps the UI volume onto the gain mpv should apply.
    ///
    /// mpv's own "volume" property already means unity at 100 (below that it follows a cubic curve), so the
    /// player could never be louder than the source level: the volume slider tops out at 100 and the
    /// "最大音量" (volume-max) slider used to be a dead knob. That value is now the ceiling reached when the
    /// slider sits at 100, expressed as extra gain above unity and capped at +12 dB to keep clipping sane.
    /// </summary>
    public static class VolumeMath
    {
        /// <summary>Hard cap for the boost taken from the volume ceiling (400% = +12.04 dB).</summary>
        public const double MaxHeadroomDb = 12.0;

        /// <summary>Extra gain, in dB, that the configured volume ceiling (percent) unlocks.</summary>
        public static double CeilingGainDb(double volumeMaxPercent)
        {
            var percent = Math.Max(100.0, Math.Min(1000.0, volumeMaxPercent));
            var db = 20.0 * Math.Log10(percent / 100.0);
            return Math.Max(0.0, Math.Min(MaxHeadroomDb, db));
        }

        /// <summary>Total "volume-gain" for the current settings, clamped to what mpv accepts (-200..60).</summary>
        public static double GainDb(double userGainDb, double volumeMaxPercent)
        {
            var gain = Math.Max(-200.0, Math.Min(60.0, userGainDb)) + CeilingGainDb(volumeMaxPercent);
            return Math.Max(-200.0, Math.Min(60.0, gain));
        }
    }
}
