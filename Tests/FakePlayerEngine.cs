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
        public void SetPropertyString(string name, string value) { }
        public void SetRecordingMode(bool recording) { }
        public string? GetPropertyString(string name) => null;
        public double? GetPropertyDouble(string name) => null;
        public long? GetPropertyLong(string name) => null;
        public bool? GetPropertyBool(string name) => null;
    }
}
