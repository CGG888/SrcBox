using System;

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
        Cancelled
    }

    public class ScheduledRecordingInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
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
        public string? FilePath { get; set; }
        public long SizeBytes { get; set; }
        public string SizeLabel { get; set; } = "";
        public ScheduledRecordingStatus Status { get; set; } = ScheduledRecordingStatus.Waiting;
        public string StatusLabel { get; set; } = "";
        public string? ErrorMessage { get; set; }

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
