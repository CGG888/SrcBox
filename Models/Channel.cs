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
        public Source? Tag { get; set; }
        public System.Collections.Generic.List<Source> Sources { get; set; } = new System.Collections.Generic.List<Source>();
        public int DisplayIndex { get; set; }
        public int GlobalIndex { get; set; }
        
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
