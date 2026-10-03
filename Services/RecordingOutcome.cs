using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Maps a background recorder completion onto a scheduled-recording status.
    /// A user requested stop reaches the manager as success=false with the error "Cancelled", which used to
    /// be stored as a failure and even raised a failure styled toast.
    /// </summary>
    public static class RecordingOutcome
    {
        public static bool IsCancellation(string? errorMessage)
            => !string.IsNullOrEmpty(errorMessage)
               && errorMessage.Contains("cancel", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>True for the statuses that mean "the user stopped it", not "it failed".</summary>
        public static bool IsUserStopped(ScheduledRecordingStatus status)
            => status is ScheduledRecordingStatus.Cancelled or ScheduledRecordingStatus.Stopped;

        public static ScheduledRecordingStatus Resolve(bool success, string? errorMessage, ScheduledRecordingStatus current)
        {
            if (success) return ScheduledRecordingStatus.Completed;
            // StopRecording already recorded the stop before cancelling, so keep that status instead of
            // overwriting it with the "Cancelled" the recorder reports.
            if (IsUserStopped(current)) return current;
            if (IsCancellation(errorMessage)) return ScheduledRecordingStatus.Cancelled;
            return ScheduledRecordingStatus.Failed;
        }
    }
}
