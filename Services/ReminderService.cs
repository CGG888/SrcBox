using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    public class ScheduledRecordingTriggerArgs : EventArgs
    {
        public string ReminderId { get; }
        public string Action { get; }
        public string ChannelId { get; }
        public string ChannelName { get; }
        public string? ChannelLogo { get; }
        public string ProgramTitle { get; }
        public DateTime ScheduledStart { get; }
        public DateTime ScheduledEnd { get; }
        public string? Url { get; }
        public string? RecordMode { get; }
        public int? RecordDurationMin { get; }

        public ScheduledRecordingTriggerArgs(string reminderId, string action, string channelId, string channelName, string? channelLogo,
            string programTitle, DateTime scheduledStart, DateTime scheduledEnd, string? url, string? recordMode, int? recordDurationMin)
        {
            ReminderId = reminderId;
            Action = action;
            ChannelId = channelId;
            ChannelName = channelName;
            ChannelLogo = channelLogo;
            ProgramTitle = programTitle;
            ScheduledStart = scheduledStart;
            ScheduledEnd = scheduledEnd;
            Url = url;
            RecordMode = recordMode;
            RecordDurationMin = recordDurationMin;
        }
    }

    public class ReminderService : IDisposable
    {
        private static readonly Lazy<ReminderService> _lazy = new Lazy<ReminderService>(() => new ReminderService());
        public static ReminderService Instance => _lazy.Value;
        private readonly System.Timers.Timer _timer = new System.Timers.Timer(1000) { AutoReset = false };
        private const int GraceSeconds = 120;
        private List<ScheduledReminder> _list = new List<ScheduledReminder>();

        public event EventHandler<ScheduledRecordingTriggerArgs>? RecordingTriggered;

        private ReminderService()
        {
            _timer.Elapsed += (_, __) => Tick();
        }

        public void Start()
        {
            try
            {
                _list = AppSettings.Current.ScheduledReminders ?? new List<ScheduledReminder>();
                try
                {
                    LibmpvIptvClient.Diagnostics.Logger.Debug($"[Reminder] start loaded={_list.Count} enabled={_list.Count(x=>x.Enabled && !x.Completed)}");
                }
                catch { }
            }
            catch { _list = new List<ScheduledReminder>(); }
            // 先处理已经到点或刚过点（宽限内）的预约，然后再安排下一次
            ProcessDue(includeGrace: true);
            ScheduleNext();
        }

        public void Import(IEnumerable<ScheduledReminder> items)
        {
            if (items == null) return;
            var map = _list.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
            {
                if (it == null || string.IsNullOrWhiteSpace(it.Id)) continue;
                map[it.Id] = it;
            }
            _list = map.Values.ToList();
            AppSettings.Current.ScheduledReminders = _list;
            AppSettings.Current.Save();
            ScheduleNext();
        }

        void ScheduleNext()
        {
            try
            {
                var now = DateTime.UtcNow;
                var next = _list.Where(x => x.Enabled && !x.Completed)
                    .Select(x =>
                    {
                        bool isPlay = string.Equals(x.Action, "play", StringComparison.OrdinalIgnoreCase);
                        bool isRecord = IsRecordAction(x.Action);
                        var dueAt = isPlay
                            ? (x.PreAlertSeconds > 0 ? x.StartAtUtc.AddSeconds(-x.PreAlertSeconds) : x.StartAtUtc)
                            : (isRecord ? x.StartAtUtc : x.StartAtUtc.AddSeconds(-x.PreAlertSeconds));
                        return new { Item = x, Due = dueAt };
                    })
                    .Where(p => p.Due > now)
                    .OrderBy(p => p.Due)
                    .FirstOrDefault();
                if (next == null) { _timer.Stop(); return; }
                var due = next.Due;
                var ms = Math.Max(500, (int)(due - now).TotalMilliseconds);
                _timer.Interval = ms;
                _timer.Start();
                try
                {
                    var act = next.Item?.Action ?? "";
                    LibmpvIptvClient.Diagnostics.Logger.Debug($"[Reminder] next={due.ToLocalTime():yyyy-MM-dd HH:mm:ss} action={act} count={_list.Count(r=>r.Enabled && !r.Completed)}");
                }
                catch { }
            }
            catch { }
        }

        private static bool IsRecordAction(string? action)
        {
            if (string.IsNullOrEmpty(action)) return false;
            return action.StartsWith("record", StringComparison.OrdinalIgnoreCase);
        }

        private void FireRecordingTrigger(ScheduledReminder r, string action, DateTime local, string? logoLocal)
        {
            try
            {
                LibmpvIptvClient.Services.ToastService.ShowRecordingAppointment(
                    r.ChannelId ?? "", r.ChannelName ?? "", r.Note ?? "", local, logoLocal,
                    action == "record_front" ? "front" : "back",
                    r.PlayMode ?? "default", 0);
            }
            catch { }
            r.Completed = true;
            RecordingTriggered?.Invoke(this, new ScheduledRecordingTriggerArgs(
                r.Id, action, r.ChannelId ?? "", r.ChannelName ?? "", logoLocal,
                r.Note ?? "", r.StartAtUtc.ToLocalTime(), r.StartAtUtc.ToLocalTime().AddMinutes(r.RecordDurationMin ?? 60),
                null, r.RecordMode, r.RecordDurationMin));
            try { LibmpvIptvClient.Diagnostics.Logger.Debug($"[Reminder] fired recording id={r.Id} ch={r.ChannelName} action={action}"); } catch { }
        }

        void Tick()
        {
            try
            {
                ProcessDue(includeGrace: false);
            }
            catch { }
            ScheduleNext();
        }
        void ProcessDue(bool includeGrace)
        {
            var now = DateTime.UtcNow;
            int ok = 0, miss = 0;
            foreach (var r in _list.Where(x => x.Enabled && !x.Completed).OrderBy(x => x.StartAtUtc))
            {
                bool doPlay = string.Equals(r.Action, "play", StringComparison.OrdinalIgnoreCase);
                bool doRecordFront = string.Equals(r.Action, "record_front", StringComparison.OrdinalIgnoreCase);
                bool doRecordBack = string.Equals(r.Action, "record_back", StringComparison.OrdinalIgnoreCase);
                bool isRecord = doRecordFront || doRecordBack;

                var preAt = r.StartAtUtc.AddSeconds(-r.PreAlertSeconds);

                if ((doPlay || isRecord) && r.PreAlertSeconds > 0 && now >= preAt && now < r.StartAtUtc)
                {
                    try
                    {
                        var local = r.StartAtUtc.ToLocalTime();
                        string? logoLocal = null;
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(r.ChannelLogo))
                            {
                                if (System.IO.File.Exists(r.ChannelLogo)) logoLocal = r.ChannelLogo;
                                else logoLocal = LogoCacheService.Instance.GetLogoPathAsync(r.ChannelName ?? "", r.ChannelLogo).GetAwaiter().GetResult();
                            }
                        }
                        catch { }

                        if (doPlay)
                        {
                            try
                            {
                                LibmpvIptvClient.Services.ToastService.ShowPlayAppointment(
                                    r.ChannelId ?? "", r.ChannelName ?? "", r.Note ?? "", local, logoLocal, r.PlayMode ?? "default", r.PreAlertSeconds);
                            }
                            catch { }
                            r.Completed = true; ok++;
                            try { LibmpvIptvClient.Diagnostics.Logger.Debug($"[Reminder] pre-alert scheduled autoplay id={r.Id} ch={r.ChannelName} at={preAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"); } catch { }
                        }
                        else if (isRecord)
                        {
                            FireRecordingTrigger(r, doRecordFront ? "record_front" : "record_back", local, logoLocal);
                        }
                    }
                    catch { }
                    continue;
                }

                var triggerAt = doPlay || isRecord ? r.StartAtUtc : preAt;
                if (triggerAt <= now)
                {
                    var delta = (now - triggerAt).TotalSeconds;
                    if (!includeGrace && delta > 5) continue;
                    if (includeGrace && delta > GraceSeconds) { r.Completed = true; miss++; try { LibmpvIptvClient.Diagnostics.Logger.Debug($"[Reminder] missed id={r.Id} ch={r.ChannelName} action={r.Action} due={triggerAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} delta={delta:F1}s"); } catch { } continue; }
                    try
                    {
                        var local = r.StartAtUtc.ToLocalTime();
                        string? logoLocal = null;
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(r.ChannelLogo))
                            {
                                if (System.IO.File.Exists(r.ChannelLogo)) logoLocal = r.ChannelLogo;
                                else logoLocal = LogoCacheService.Instance.GetLogoPathAsync(r.ChannelName ?? "", r.ChannelLogo).GetAwaiter().GetResult();
                            }
                        }
                        catch { }

                        if (doPlay)
                        {
                            try
                            {
                                LibmpvIptvClient.Services.ToastService.ShowPlayAppointment(r.ChannelId ?? "", r.ChannelName ?? "", r.Note ?? "", local, logoLocal, r.PlayMode ?? "default");
                            }
                            catch { NotificationService.Instance.ShowWithLogo(r.ChannelName ?? "", r.Note ?? "", local, logoLocal, 8000); }
                        }
                        else if (doRecordFront)
                        {
                            FireRecordingTrigger(r, "record_front", local, logoLocal);
                        }
                        else if (doRecordBack)
                        {
                            FireRecordingTrigger(r, "record_back", local, logoLocal);
                        }
                        else
                        {
                            LibmpvIptvClient.Services.ToastService.ShowReminder(r.ChannelId ?? "", r.ChannelName ?? "", r.Note ?? "", local, logoLocal, true);
                        }
                        r.Completed = true; ok++;
                        try
                        {
                            LibmpvIptvClient.Diagnostics.Logger.Debug($"[Reminder] fired id={r.Id} ch={r.ChannelName} action={r.Action} local={local:yyyy-MM-dd HH:mm:ss}");
                        }
                        catch { }
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        LibmpvIptvClient.Diagnostics.Logger.Debug($"[Reminder] pending id={r.Id} action={r.Action} due={triggerAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                    }
                    catch { }
                }
            }
            try
            {
                if (ok + miss > 0)
                {
                    LibmpvIptvClient.Diagnostics.Logger.Debug($"[Reminder] summary ok={ok} missed={miss}");
                }
            }
            catch { }
            AppSettings.Current.ScheduledReminders = _list;
            AppSettings.Current.Save();
        }

        public void Dispose()
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
        }
    }
}
