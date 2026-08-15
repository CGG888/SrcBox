using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LibmpvIptvClient.Models
{
    public class Channel : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Group { get; set; } = "";
        private string _logo = "";
        public string Logo
        {
            get => _logo;
            set { if (_logo != value) { _logo = value; OnPropertyChanged(); } }
        }
        public string TvgId { get; set; } = "";
        public string TvgName { get; set; } = "";
        public string Catchup { get; set; } = "";
        public string CatchupSource { get; set; } = "";
        
        private bool _favorite;
        public bool Favorite
        {
            get => _favorite;
            set { if (_favorite != value) { _favorite = value; OnPropertyChanged(); } }
        }
        private bool _playing;
        public bool Playing
        {
            get => _playing;
            set { if (_playing != value) { _playing = value; OnPropertyChanged(); } }
        }
        private string _currentProgramTitle = "";
        public string CurrentProgramTitle
        {
            get => _currentProgramTitle;
            set { if (_currentProgramTitle != value) { _currentProgramTitle = value; OnPropertyChanged(); } }
        }
        public bool HasCatchup => !string.IsNullOrEmpty(Catchup) || !string.IsNullOrEmpty(CatchupSource); // Helper for UI
        public bool HasTimeshift => !string.IsNullOrEmpty(CatchupSource) 
            || (LibmpvIptvClient.AppSettings.Current?.Timeshift?.Enabled == true 
                && !string.IsNullOrEmpty(LibmpvIptvClient.AppSettings.Current?.Timeshift?.UrlFormat));
        private Source? _tag;
        public Source? Tag
        {
            get => _tag;
            set { if (_tag != value) { _tag = value; OnPropertyChanged(); } }
        }
        public System.Collections.Generic.List<Source> Sources { get; set; } = new System.Collections.Generic.List<Source>();
        public int DisplayIndex { get; set; }
        public int GlobalIndex { get; set; }

        /// <summary>
        /// Dummy stamp property. When Source.IsReachable/LastChecked/FailureCount changes,
        /// SourceHealthService raises this via NotifyHealthChanged() → Channel fires
        /// PropertyChanged("SourceHealthStamp") → Ellipse binding re-evaluates.
        /// </summary>
        private int _sourceHealthStamp;
        public int SourceHealthStamp
        {
            get => _sourceHealthStamp;
            private set { if (_sourceHealthStamp != value) { _sourceHealthStamp = value; OnPropertyChanged(); OnPropertyChanged(nameof(HealthColorBrush)); } }
        }

        /// <summary>
        /// Computed health color brush. Gray=single/untested, Green=tag healthy,
        /// Yellow=tag unhealthy with fallback, Red=all unreachable.
        /// </summary>
        public System.Windows.Media.SolidColorBrush HealthColorBrush
        {
            get
            {
                var sources = Sources;
                if (sources == null || sources.Count == 0) return GrayBrush;

                var tag = Tag ?? sources.FirstOrDefault();
                if (tag == null) return GrayBrush;
                if (!tag.LastChecked.HasValue) return GrayBrush;

                var hasHealthyFallback = sources.Count > 1 && sources.Any(s => s != tag && s.IsHealthy && s.IsHttpSource);
                var result = tag.IsHealthy ? GreenBrush : (hasHealthyFallback ? YellowBrush : RedBrush);
                Diagnostics.Logger.Info($"[HealthColor] {Name} tag.LastChecked={tag.LastChecked} tagHealthy={tag.IsHealthy} returning {(tag.IsHealthy ? "Green" : hasHealthyFallback ? "Yellow" : "Red")}");
                return result;
            }
        }

        private static readonly System.Windows.Media.SolidColorBrush GreenBrush  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x35, 0xC7, 0x59));
        private static readonly System.Windows.Media.SolidColorBrush YellowBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD6, 0x0A));
        private static readonly System.Windows.Media.SolidColorBrush RedBrush    = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x3B, 0x30));
        private static readonly System.Windows.Media.SolidColorBrush GrayBrush  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8E, 0x8E, 0x93));

        static Channel()
        {
            GreenBrush.Freeze();
            YellowBrush.Freeze();
            RedBrush.Freeze();
            GrayBrush.Freeze();
        }

        /// <summary>
        /// Called by SourceHealthService when a source's health status changes,
        /// so Ellipse bindings refresh.
        /// </summary>
        public void NotifySourceHealthChanged()
        {
            checked { _sourceHealthStamp++; }
            Diagnostics.Logger.Info($"[Channel] NotifySourceHealthChanged for {Name} stamp={_sourceHealthStamp}");
            OnPropertyChanged(nameof(SourceHealthStamp));
            OnPropertyChanged(nameof(HealthColorBrush));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Reset()
        {
            Id = "";
            Name = "";
            Group = "";
            _logo = "";
            TvgId = "";
            TvgName = "";
            Catchup = "";
            CatchupSource = "";
            _favorite = false;
            _playing = false;
            _currentProgramTitle = "";
            Tag = null;
            Sources.Clear();
            DisplayIndex = 0;
            GlobalIndex = 0;
        }
    }
}
