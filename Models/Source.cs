using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LibmpvIptvClient.Models
{
    public enum StreamProtocol
    {
        HLS,
        DASH,
        RTSP,
        RTP,
        SRT,
        HTTP,
        FILE
    }
    public enum TransportHint
    {
        Auto,
        Tcp,
        Udp,
        UdpMulticast,
        Http
    }
    public class SourceQuality
    {
        public int Height { get; set; }
        public int Bitrate { get; set; }
        public string Codec { get; set; } = "";
        public double Fps { get; set; }
    }
    public class SourceFcc
    {
        public bool Supported { get; set; }
        public string? BurstTemplate { get; set; }
    }

    /// <summary>
    /// Represents a single stream source for a channel.
    /// Supports health monitoring via INotifyPropertyChanged.
    /// </summary>
    public class Source : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = ""; // 存储源的描述名称（如"组播高清"）
        public string ChannelId { get; set; } = "";
        public string Url { get; set; } = "";
        public StreamProtocol Protocol { get; set; }
        public TransportHint Transport { get; set; } = TransportHint.Auto;
        public SourceQuality Quality { get; set; } = new SourceQuality();
        public string Region { get; set; } = "";
        public int Priority { get; set; }
        public SourceFcc? Fcc { get; set; }

        #region Health Status (writable, notify on change)

        private bool _isReachable;
        public bool IsReachable
        {
            get => _isReachable;
            set { if (_isReachable != value) { _isReachable = value; OnPropertyChanged(); OnHealthChanged(); } }
        }

        private int _latencyMs;
        public int LatencyMs
        {
            get => _latencyMs;
            set { if (_latencyMs != value) { _latencyMs = value; OnPropertyChanged(); } }
        }

        private System.DateTime? _lastChecked;
        public System.DateTime? LastChecked
        {
            get => _lastChecked;
            set { if (_lastChecked != value) { _lastChecked = value; OnPropertyChanged(); OnHealthChanged(); } }
        }

        private int _failureCount;
        public int FailureCount
        {
            get => _failureCount;
            set { if (_failureCount != value) { _failureCount = value; OnPropertyChanged(); OnHealthChanged(); } }
        }

        /// <summary>
        /// Computed: healthy if checked, reachable, and not persistently failing.
        /// Returns false for sources that have never been probed (LastChecked == null).
        /// </summary>
        public bool IsHealthy => LastChecked.HasValue && IsReachable && FailureCount < 3;

        /// <summary>
        /// Callback invoked when health-related properties (IsReachable, LastChecked,
        /// FailureCount) change. SourceHealthService sets this to notify the parent Channel.
        /// </summary>
        public System.Action? OnHealthChanged { get; set; }

        /// <summary>
        /// Returns true if this source is HTTP or HTTPS (can be probed via HTTP HEAD).
        /// </summary>
        public bool IsHttpSource => !string.IsNullOrWhiteSpace(Url)
            && (Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            // Also notify IsHealthy when underlying properties change
            if (propertyName == nameof(IsReachable) || propertyName == nameof(FailureCount))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHealthy)));
            }
        }

        #endregion
    }
}
