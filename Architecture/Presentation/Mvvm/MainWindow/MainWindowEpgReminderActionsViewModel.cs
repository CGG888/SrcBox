using System;
using System.Collections.Generic;
using LibmpvIptvClient;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

public sealed class MainWindowEpgReminderActionsViewModel : ViewModelBase
{
    public bool CanCreateReminder(EpgProgram program, DateTime now)
    {
        return program.Start > now;
    }

    public ScheduledReminder BuildReminder(Channel channel, EpgProgram program, int preAlertSeconds, string action, string? playMode, string? recordMode, int? recordDurationMin)
    {
        return new ScheduledReminder
        {
            Id = Guid.NewGuid().ToString("N"),
            ChannelId = channel.TvgId ?? channel.Id ?? "",
            ChannelName = channel.Name ?? "",
            ChannelLogo = channel.Logo ?? "",
            StartAtUtc = program.Start.ToUniversalTime(),
            EndTimeUtc = program.End.ToUniversalTime(),
            PreAlertSeconds = preAlertSeconds,
            Action = action,
            PlayMode = playMode,
            RecordMode = recordMode,
            RecordDurationMin = recordDurationMin,
            Enabled = true,
            Note = program.Title ?? ""
        };
    }

    public void SaveReminder(ScheduledReminder reminder)
    {
        var list = AppSettings.Current.ScheduledReminders ?? new List<ScheduledReminder>();
        var dupKey = (reminder.ChannelId + "|" + reminder.StartAtUtc.ToString("o")).ToLowerInvariant();
        list.RemoveAll(x => (x.ChannelId + "|" + x.StartAtUtc.ToString("o")).ToLowerInvariant() == dupKey);
        list.Add(reminder);
        AppSettings.Current.ScheduledReminders = list;
        AppSettings.Current.Save();
        try { ReminderService.Instance.Start(); } catch { }
    }

    public void NotifyReminder(ScheduledReminder reminder, string? fallbackLogo)
    {
        try
        {
            ToastService.ShowReminder(reminder.ChannelId, reminder.ChannelName, reminder.Note, reminder.StartAtUtc.ToLocalTime(), reminder.ChannelLogo, false);
        }
        catch
        {
            try { ToastService.ShowReminder(reminder.ChannelName ?? "", reminder.ChannelName ?? "", reminder.Note ?? "", reminder.StartAtUtc.ToLocalTime(), fallbackLogo, false); } catch { }
        }
    }
}
