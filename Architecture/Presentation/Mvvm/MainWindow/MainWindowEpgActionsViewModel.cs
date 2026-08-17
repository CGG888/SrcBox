using System;
using System.Collections.Generic;
using System.Linq;
using LibmpvIptvClient;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;

public sealed record EpgDateNavigationState(string Label, bool CanPrev, bool CanNext, DateTime CurrentDate);

public sealed record EpgDataSet(
    IReadOnlyList<DateTime> AvailableDates,
    DateTime CurrentDate,
    IReadOnlyList<EpgProgram> Items);

public sealed record EpgFilterResult(
    IReadOnlyList<EpgProgram> Items,
    DateTime CurrentDate);

public sealed class MainWindowEpgActionsViewModel : ViewModelBase
{
    public EpgDataSet BuildEpgDataSet(IReadOnlyList<EpgProgram> programs, DateTime today)
    {
        var availableDates = programs.Select(p => p.Start.Date).Distinct().OrderBy(d => d).ToList();
        var currentDate = today;
        if (availableDates.Count > 0)
        {
            if (availableDates.Contains(today))
            {
                currentDate = today;
            }
            else
            {
                currentDate = availableDates[0];
            }
        }

        var filtered = programs.Where(p => p.Start.Date == currentDate).OrderBy(p => p.Start).ToList();
        return new EpgDataSet(availableDates, currentDate, filtered);
    }

    public EpgFilterResult FilterProgramsForDate(IReadOnlyList<EpgProgram> programs, DateTime currentDate)
    {
        var filtered = programs.Where(p => p.Start.Date == currentDate).OrderBy(p => p.Start).ToList();
        return new EpgFilterResult(filtered, currentDate);
    }

    public DateTime ResolveDateForFocus(DateTime focusTime)
    {
        return focusTime.Date;
    }

    public DateTime? MoveToPrevDate(IReadOnlyList<DateTime> availableDates, DateTime currentDate)
    {
        var idx = IndexOfDate(availableDates, currentDate);
        if (idx > 0)
        {
            return availableDates[idx - 1];
        }
        return null;
    }

    public DateTime? MoveToNextDate(IReadOnlyList<DateTime> availableDates, DateTime currentDate)
    {
        var idx = IndexOfDate(availableDates, currentDate);
        if (idx >= 0 && idx < availableDates.Count - 1)
        {
            return availableDates[idx + 1];
        }
        return null;
    }

    public EpgDateNavigationState BuildDateNavigationState(IReadOnlyList<DateTime> availableDates, DateTime currentDate, DateTime today)
    {
        if (availableDates.Count == 0)
        {
            return new EpgDateNavigationState(
                LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_NoData", "无数据"),
                false,
                false,
                currentDate);
        }

        var idx = IndexOfDate(availableDates, currentDate);
        if (idx < 0)
        {
            currentDate = availableDates[0];
            idx = 0;
        }

        var label = currentDate.ToString("MM-dd");
        if (currentDate == today)
        {
            label = LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Today", "今天");
        }
        else if (currentDate == today.AddDays(1))
        {
            label = LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Tomorrow", "明天");
        }
        else if (currentDate == today.AddDays(-1))
        {
            label = LibmpvIptvClient.Helpers.ResxLocalizer.Get("EPG_Yesterday", "昨天");
        }

        return new EpgDateNavigationState(
            label,
            idx > 0,
            idx < availableDates.Count - 1,
            currentDate);
    }

    public EpgProgram? ResolveScrollTargetForToday(IReadOnlyList<EpgProgram> programs, DateTime now)
    {
        return programs.FirstOrDefault(p => p.Start <= now && p.End > now);
    }

    public void ApplyProgramFlags(
        IReadOnlyList<EpgProgram> programs,
        string channelKey,
        IReadOnlyList<ScheduledReminder>? reminders,
        EpgProgram? currentPlayingProgram,
        DateTime now,
        DateTime? playbackTime)
    {
        foreach (var p in programs)
        {
            p.IsPlaying = false;
            p.IsBooked = false;
        }

        if (reminders != null && reminders.Count > 0 && !string.IsNullOrEmpty(channelKey))
        {
            foreach (var p in programs)
            {
                p.IsBooked = reminders.Any(r =>
                    r.Enabled && !r.Completed &&
                    string.Equals(r.ChannelId ?? "", channelKey ?? "", StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs((r.StartAtUtc - p.Start.ToUniversalTime()).TotalSeconds) <= 60);
            }
        }

        EpgProgram? target = null;
        if (currentPlayingProgram != null)
        {
            target = programs.FirstOrDefault(p => p.Start == currentPlayingProgram.Start && p.End == currentPlayingProgram.End)
                     ?? programs.FirstOrDefault(p => string.Equals(p.Title ?? "", currentPlayingProgram.Title ?? "", StringComparison.OrdinalIgnoreCase)
                                                     && p.Start == currentPlayingProgram.Start);
        }

        if (target == null)
        {
            var point = playbackTime ?? now;
            target = programs.FirstOrDefault(p => p.Start <= point && p.End > point);
        }

        if (target != null)
        {
            target.IsPlaying = true;
        }
    }

    public int SyncChannelCurrentProgramTitles(
        IReadOnlyList<Channel> channels,
        Func<Channel, EpgProgram?> resolveCurrentProgram)
    {
        if (channels == null || resolveCurrentProgram == null)
        {
            return 0;
        }

        var changed = 0;
        var total = 0;
        foreach (var ch in channels)
        {
            total++;
            if (ch == null)
            {
                continue;
            }

            try
            {
                var prog = resolveCurrentProgram(ch);
                if (prog != null)
                {
                    if (ch.CurrentProgramTitle != prog.Title)
                    {
                        ch.CurrentProgramTitle = prog.Title;
                        changed++;
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(ch.CurrentProgramTitle))
                    {
                        ch.CurrentProgramTitle = "";
                        changed++;
                    }
                }
            }
            catch
            {
                if (!string.IsNullOrEmpty(ch.CurrentProgramTitle))
                {
                    ch.CurrentProgramTitle = "";
                    changed++;
                }
            }
        }
        LibmpvIptvClient.Diagnostics.Logger.Debug($"[EPG] SyncChannelCurrentProgramTitles: processed {total} channels, changed {changed}");

        return changed;
    }

    private static int IndexOfDate(IReadOnlyList<DateTime> dates, DateTime target)
    {
        for (var i = 0; i < dates.Count; i++)
        {
            if (dates[i] == target)
            {
                return i;
            }
        }
        return -1;
    }
}
