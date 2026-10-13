using System;

namespace LibmpvIptvClient.Models
{
    public class EpgProgram
    {
        public string Title { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Description { get; set; } = "";
        
        public bool IsPlaying { get; set; } // New property
        public bool IsBooked { get; set; } // Already scheduled reminder

        public string Status 
        { 
            get 
            {
                var now = DateTime.Now;
                if (now >= Start && now < End) return LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Live", "直播");
                if (now >= End)
                {
                    // Only what the configured replay span still covers is replayable: the operator
                    // does not serve anything older, so such a programme is reported as expired
                    // instead of offering a replay that would end a few seconds after it starts.
                    return LibmpvIptvClient.Services.ArchiveWindow.IsReplayable(End)
                        ? LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Playback", "回放")
                        : LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Expired", "已过期");
                }
                return LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Status_Scheduled", "预约");
            }
        }
    }
}
