using System;
using System.ComponentModel;

namespace LibmpvIptvClient.Models
{
    public enum RecordingType
    {
        Front,
        Back,
        Realtime
    }

    public enum ScheduledRecordingStatus
    {
        Waiting,
        Recording,
        Completed,
        Failed,
        Cancelled,   // 取消（录制前取消）
        Stopped      // 停止（录制中停止）
    }

    public class ScheduledRecordingInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _id = Guid.NewGuid().ToString("N");
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }
        public string ReminderId { get; set; } = "";
        public string ChannelId { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string ChannelLogo { get; set; } = "";
        public string ProgramTitle { get; set; } = "";
        public RecordingType Type { get; set; } = RecordingType.Front;
        public DateTime ScheduledStart { get; set; }
        public DateTime ScheduledEnd { get; set; }
        public int ScheduledDurationMin { get; set; }
        public DateTime? ActualStartTime { get; set; }
        public DateTime? ActualEndTime { get; set; }
        public int? ActualDurationMin { get; set; }
        public string? FilePath { get; set; } = "";
        private long _sizeBytes;
        public long SizeBytes
        {
            get => _sizeBytes;
            set { _sizeBytes = value; OnPropertyChanged(nameof(SizeBytes)); OnPropertyChanged(nameof(SizeLabel)); }
        }
        private string _sizeLabel = "";
        public string SizeLabel
        {
            get => _sizeLabel;
            set { _sizeLabel = value; OnPropertyChanged(nameof(SizeLabel)); }
        }
        private ScheduledRecordingStatus _status = ScheduledRecordingStatus.Waiting;
        public ScheduledRecordingStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusLabel)); }
        }
        private string _statusLabel = "";
        public string StatusLabel
        {
            get => _statusLabel;
            set { _statusLabel = value; OnPropertyChanged(nameof(StatusLabel)); }
        }
        public string? ErrorMessage { get; set; }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string TypeLabel => Type switch
        {
            RecordingType.Front => Helpers.ResxLocalizer.Get("Recording_Front", "前台"),
            RecordingType.Back => Helpers.ResxLocalizer.Get("Recording_Back", "后台"),
            RecordingType.Realtime => Helpers.ResxLocalizer.Get("Recording_Realtime", "实时"),
            _ => ""
        };

        public string DurationLabel => ActualDurationMin.HasValue
            ? $"{ActualDurationMin.Value} {Helpers.ResxLocalizer.Get("Recording_Min", "分钟")}"
            : $"{ScheduledDurationMin} {Helpers.ResxLocalizer.Get("Recording_Min", "分钟")}";
    }
}
