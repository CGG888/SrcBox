using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Services
{
    public class ReminderService : IDisposable
    {
        private static readonly Lazy<ReminderService> _lazy = new Lazy<ReminderService>(() => new ReminderService());
        public static ReminderService Instance => _lazy.Value;
        private readonly System.Timers.Timer _timer = new System.Timers.Timer(1000) { AutoReset = false };
        private const int GraceSeconds = 120;
        private List<ScheduledReminder> _list = new List<ScheduledReminder>();

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
                        var dueAt = isPlay
                            ? (x.PreAlertSeconds > 0 ? x.StartAtUtc.AddSeconds(-x.PreAlertSeconds) : x.StartAtUtc)
                            : x.StartAtUtc.AddSeconds(-x.PreAlertSeconds);
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
                var preAt = r.StartAtUtc.AddSeconds(-r.PreAlertSeconds);
                if (doPlay && r.PreAlertSeconds > 0 && now >= preAt && now < r.StartAtUtc)
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
                        try 
                        { 
                            LibmpvIptvClient.Services.ToastService.ShowPlayAppointment(
                                r.ChannelId ?? "", r.ChannelName ?? "", r.Note ?? "", local, logoLocal, r.PlayMode ?? "default", r.PreAlertSeconds);
                        } 
                        catch { }
                        // 预提醒即进入倒计时与自动播放流程，标记完成防止开始时间再次触发
                        r.Completed = true; ok++;
                        try { LibmpvIptvClient.Diagnostics.Logger.Debug($"[Reminder] pre-alert scheduled autoplay id={r.Id} ch={r.ChannelName} at={preAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"); } catch { }
                    }
                    catch { }
                    continue;
                }
                var triggerAt = doPlay ? r.StartAtUtc : preAt;
                if (triggerAt <= now)
                {
                    var delta = (now - triggerAt).TotalSeconds;
                    // 允许一定抖动（调度/挂起/线程切换导致的轻微延迟）
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
                        try
                        {
                            if (doPlay)
                            {
                                LibmpvIptvClient.Services.ToastService.ShowPlayAppointment(r.ChannelId ?? "", r.ChannelName ?? "", r.Note ?? "", local, logoLocal, r.PlayMode ?? "default");
                            }
                            else
                            {
                                LibmpvIptvClient.Services.ToastService.ShowReminder(r.ChannelId ?? "", r.ChannelName ?? "", r.Note ?? "", local, logoLocal, true);
                            }
                        }
                        catch { NotificationService.Instance.ShowWithLogo(r.ChannelName ?? "", r.Note ?? "", local, logoLocal, 8000); }
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
