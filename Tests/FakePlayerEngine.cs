using System.Collections.Generic;
using LibmpvIptvClient.Architecture.Application.Player;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Records what the app hands to the player instead of decoding anything, so view-model level
    /// behaviour (which url gets requested, when a seek happens) can be asserted in-process.
    /// </summary>
    public sealed class FakePlayerEngine : IPlayerEngine
    {
        public List<string> Played { get; } = new List<string>();
        public List<double> RelativeSeeks { get; } = new List<double>();
        public List<double> AbsoluteSeeks { get; } = new List<double>();
        public double TimePos { get; set; }
        public double? Duration { get; set; }

        /// <summary>Property values the fake player reports (name -> string/long/double/bool).</summary>
        public Dictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        /// <summary>Property writes the code under test performed.</summary>
        public Dictionary<string, string> SetProperties { get; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public void Play(string url) => Played.Add(url);
        public void Stop() { }
        public void Pause(bool paused) { }
        public void SeekAbsolute(double seconds) => AbsoluteSeeks.Add(seconds);
        public void SeekRelative(double seconds) => RelativeSeeks.Add(seconds);
        public void SetVolume(double volume) { }
        public void SetMute(bool muted) { }
        public void SetSpeed(double speed) { }
        public void SetAspectRatio(string ratio) { }
        public void SetDeinterlace(string mode, string? fieldParity = null, string? algorithm = null) { }
        public double? GetTimePos() => TimePos;
        public double? GetDuration() => Duration;
        public void EnsureReadyForLoad() { }
        public bool IsEofReached() => false;
        public void LoadWithPrefetch(string url, IEnumerable<string> nextUrls) => Played.Add(url);
        public bool SwitchToPrefetchedNext(string url) => false;
        public void AnchorPrefetch(string? nextUrl) { }
        public void SetPropertyString(string name, string value) => SetProperties[name] = value;
        public void SetRecordingMode(bool recording) { }
        public string? GetPropertyString(string name)
            => Properties.TryGetValue(name, out var v) ? v as string : null;
        public double? GetPropertyDouble(string name)
            => Properties.TryGetValue(name, out var v) && v is double d ? d : null;
        public long? GetPropertyLong(string name)
            => Properties.TryGetValue(name, out var v) ? v switch { long l => l, int i => i, _ => null } : null;
        public bool? GetPropertyBool(string name)
            => Properties.TryGetValue(name, out var v) && v is bool b ? b : null;
    }
}
