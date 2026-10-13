using System;
using System.Collections.Generic;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>A resolved "when" for a catch-up question.</summary>
    public sealed class VoiceTimeWindow
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        /// <summary>Short human label used in the spoken answer ("今天" / "昨天晚上").</summary>
        public string Label { get; set; } = "";

        /// <summary>Hour filter, -1 when the whole day is meant.</summary>
        public int FromHour { get; set; } = -1;

        /// <summary>Hour filter, -1 when the whole day is meant.</summary>
        public int ToHour { get; set; } = -1;
    }

    /// <summary>
    /// Turns the time words of a sentence into a [Start, End) window: "今天", "昨晚", "刚才", "上周五",
    /// "20点" — with the English and Russian equivalents. Without a day word the window is today.
    /// </summary>
    public static class VoiceTimeParser
    {
        /// <summary>Where the last few hours of the previous day end.</summary>
        private const int NightEndHour = 6;

        /// <summary>What "刚才" roughly covers when nothing more precise was said.</summary>
        private static readonly TimeSpan JustNowSpan = TimeSpan.FromHours(2);

        public static VoiceTimeWindow Parse(string? text, string? lang, DateTime now)
        {
            var normalized = VoiceText.Normalize(text);
            var compact = VoiceText.Compact(normalized);

            var (day, label) = ResolveDay(compact, now);
            var (fromHour, toHour, partLabel) = ResolvePart(compact);
            var (hour, minute) = ResolveClock(compact);

            if (hour >= 0)
            {
                // An explicit clock time means the user remembers when it started, not the exact range.
                var start = day.Date.AddHours(hour).AddMinutes(minute);
                var end = start.AddMinutes(90);
                return new VoiceTimeWindow
                {
                    Start = start,
                    End = end,
                    Label = $"{label}{(partLabel ?? "")} {hour:00}:{minute:00}".Trim()
                };
            }

            if (partLabel != null && fromHour >= 0)
            {
                var start = day.Date.AddHours(fromHour);
                var end = toHour >= 24 ? day.Date.AddDays(1) : day.Date.AddHours(toHour);
                // "今天早上" asked at 22:00 must not look for programmes tonight.
                if (end > now && start < now) end = now.AddMinutes(30);
                return new VoiceTimeWindow { Start = start, End = end, Label = $"{label}{partLabel}", FromHour = fromHour, ToHour = toHour };
            }

            if (compact.Contains("刚才", StringComparison.Ordinal) || compact.Contains("刚刚", StringComparison.Ordinal)
                || compact.Contains("just now", StringComparison.Ordinal) || compact.Contains("только что", StringComparison.Ordinal))
            {
                return new VoiceTimeWindow
                {
                    Start = now - JustNowSpan,
                    End = now.AddMinutes(30),
                    Label = Localize(lang, "刚才", "just now", "только что")
                };
            }

            return new VoiceTimeWindow
            {
                Start = day.Date,
                End = day.Date.AddDays(1),
                Label = label
            };
        }

        // ---------------------------------------------------------------- day

        private enum DayMarker { Today = 0, Yesterday, DayBefore, Tomorrow, DayAfter }

        private static bool MentionsDay(string compact)
        {
            foreach (var word in TimeWords.Day)
                if (compact.Contains(VoiceText.Compact(word), StringComparison.Ordinal)) return true;
            foreach (var word in TimeWords.ThisWeek)
                if (compact.Contains(VoiceText.Compact(word), StringComparison.Ordinal)) return true;
            foreach (var word in TimeWords.LastWeek)
                if (compact.Contains(VoiceText.Compact(word), StringComparison.Ordinal)) return true;
            return false;
        }

        private static (DateTime Date, string Label) ResolveDay(string compact, DateTime now)
        {
            var today = now.Date;

            if (ContainsAny(compact, TimeWords.JustNow)) return (today, Localize(null, "刚才", "just now", "только что"));
            if (compact.Contains("前天", StringComparison.Ordinal) || compact.Contains("позавчера", StringComparison.Ordinal)
                || compact.Contains("day before yesterday", StringComparison.Ordinal))
                return (today.AddDays(-2), Localize(null, "前天", "the day before yesterday", "позавчера"));
            if (compact.Contains("昨天", StringComparison.Ordinal) || compact.Contains("昨日", StringComparison.Ordinal)
                || compact.Contains("yesterday", StringComparison.Ordinal) || compact.Contains("вчера", StringComparison.Ordinal))
                return (today.AddDays(-1), Localize(null, "昨天", "yesterday", "вчера"));
            if (compact.Contains("前晚", StringComparison.Ordinal))
                return (today.AddDays(-1), Localize(null, "前晚", "the night before last", "позавчера вечером"));
            if (compact.Contains("明天", StringComparison.Ordinal) || compact.Contains("tomorrow", StringComparison.Ordinal)
                || compact.Contains("завтра", StringComparison.Ordinal))
                return (today.AddDays(1), Localize(null, "明天", "tomorrow", "завтра"));
            if (compact.Contains("后天", StringComparison.Ordinal))
                return (today.AddDays(2), Localize(null, "后天", "in two days", "послезавтра"));

            // A weekday name ("上周五", "last friday", "в прошлую пятницу").
            var weekday = ResolveWeekday(compact);
            if (weekday >= 0)
            {
                var lastWeek = ContainsAny(compact, TimeWords.LastWeek) || compact.Contains("上星期", StringComparison.Ordinal);
                var target = today.AddDays(-(((int)today.DayOfWeek - weekday + 7) % 7));
                if (lastWeek || target == today) target = target.AddDays(-7);
                return (target, $"{Localize(null, "周", "", "")}{WeekdayName(weekday)}");
            }

            if (ContainsAny(compact, TimeWords.ThisWeek))
            {
                var offset = ((int)today.DayOfWeek + 6) % 7; // Monday is 0
                return (today.AddDays(-offset), Localize(null, "本周", "this week", "на этой неделе"));
            }
            if (ContainsAny(compact, TimeWords.LastWeek))
                return (today.AddDays(-7), Localize(null, "上周", "last week", "на прошлой неделе"));

            return (today, Localize(null, "今天", "today", "сегодня"));
        }

        /// <summary>DayOfWeek value (0=Sunday) of the first weekday name in the sentence, else -1.</summary>
        private static int ResolveWeekday(string compact)
        {
            var table = new (string[] Words, int Day)[]
            {
                (new[] { "星期一", "周一", "礼拜一", "monday", "понедельник" }, 1),
                (new[] { "星期二", "周二", "礼拜二", "tuesday", "вторник" }, 2),
                (new[] { "星期三", "周三", "礼拜三", "wednesday", "среда", "среду" }, 3),
                (new[] { "星期四", "周四", "礼拜四", "thursday", "четверг" }, 4),
                (new[] { "星期五", "周五", "礼拜五", "friday", "пятница", "пятницу" }, 5),
                (new[] { "星期六", "周六", "礼拜六", "saturday", "суббота", "субботу" }, 6),
                (new[] { "星期日", "星期天", "周日", "周天", "礼拜天", "sunday", "воскресенье" }, 0)
            };
            foreach (var (words, day) in table)
                foreach (var word in words)
                    if (compact.Contains(VoiceText.Compact(word), StringComparison.Ordinal)) return day;
            return -1;
        }

        private static string WeekdayName(int day) => day switch
        {
            1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", 6 => "六", _ => "日"
        };

        // ---------------------------------------------------------------- time of day

        private static (int From, int To, string? Label) ResolvePart(string compact)
        {
            if (compact.Contains("凌晨", StringComparison.Ordinal)) return (0, NightEndHour, "凌晨");
            if (HasWord(compact, "早上", "早晨", "上午", "morning", "утр")) return (NightEndHour, 12, "上午");
            if (HasWord(compact, "中午", "noon", "полдень")) return (11, 14, "中午");
            if (HasWord(compact, "下午", "afternoon", "днём", "днем")) return (12, 18, "下午");
            if (HasWord(compact, "今晚", "今天晚上", "tonight", "сегодня вечером")) return (18, 24, "晚上");
            if (HasWord(compact, "昨晚", "昨天晚上", "last night", "вчера вечером")) return (18, 24, "晚上");
            if (HasWord(compact, "晚上", "夜里", "夜间", "evening", "night", "вечер")) return (18, 24, "晚上");
            return (-1, -1, null);
        }

        private static (int Hour, int Minute) ResolveClock(string compact)
        {
            var match = System.Text.RegularExpressions.Regex.Match(compact, @"(?<h>[0-9]{1,2})\s*(?<m>[0-9]{2})?\s*(点|時|时|:|：|h\b|час)");
            if (!match.Success) return (-1, 0);
            if (!int.TryParse(match.Groups["h"].Value, out var hour)) return (-1, 0);
            var minute = match.Groups["m"].Success && int.TryParse(match.Groups["m"].Value, out var parsed) ? parsed : 0;
            if (hour > 23) return (-1, 0);
            return (hour, Math.Clamp(minute, 0, 59));
        }

        // ---------------------------------------------------------------- helpers

        private static bool ContainsAny(string compact, IReadOnlyList<string> words)
        {
            foreach (var word in words)
                if (compact.Contains(VoiceText.Compact(word), StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool HasWord(string compact, params string[] words)
        {
            foreach (var word in words)
                if (compact.Contains(VoiceText.Compact(word), StringComparison.Ordinal)) return true;
            return false;
        }

        private static string Localize(string? lang, string zh, string en, string ru)
        {
            var l = (lang ?? "zh").ToLowerInvariant();
            if (l.StartsWith("en")) return en;
            if (l.StartsWith("ru")) return ru;
            return zh;
        }
    }
}
