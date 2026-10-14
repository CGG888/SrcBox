using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
    /// "10月13号", "晚上八点" — with the English and Russian equivalents. Without a day word the window is
    /// today. A spoken clock time is a hint, not a range: the window opens <see cref="DefaultTimeHintMinutes"/>
    /// before it and closes <see cref="ProgrammeSpanMinutes"/> after it, because "20点的新闻联播" is what a
    /// programme that started at 19:00 is called.
    /// </summary>
    public static class VoiceTimeParser
    {
        /// <summary>Where the last few hours of the previous day end.</summary>
        private const int NightEndHour = 6;

        /// <summary>Minutes before a spoken clock time whose programme still counts as a match.</summary>
        public const int DefaultTimeHintMinutes = 60;

        /// <summary>How long after the spoken start time a programme may still be the one that was meant.</summary>
        private const int ProgrammeSpanMinutes = 90;

        /// <summary>What "刚才" roughly covers when nothing more precise was said.</summary>
        private static readonly TimeSpan JustNowSpan = TimeSpan.FromHours(2);

        private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>"2026年10月13日" / "10月13号" — the year is optional.</summary>
        private static readonly Regex CalendarDate = new(
            @"(?<y>[0-9]{4})?\s*年?\s*(?<m>[0-9]{1,2})\s*月\s*(?<d>[0-9]{1,2})\s*[日号]?", Options);

        /// <summary>"13号" — a day of the current, or of the previous, month.</summary>
        private static readonly Regex CalendarDayOnly = new(@"(?<d>[0-9]{1,2})\s*[日号]", Options);

        /// <summary>"8点", "8:30", "20时", "8 h" — a clock time together with its unit.</summary>
        private static readonly Regex Clock = new(@"(?<h>[0-9]{1,2})\s*(?<m>[0-9]{2})?\s*(点|時|时|:|：|h\b|час)", Options);

        /// <summary>The clock plus the half hour that may follow it, for taking the time out of a title.</summary>
        private static readonly Regex ClockForStripping = new(
            @"(?<h>[0-9]{1,2})\s*(?<m>[0-9]{2})?\s*(点|時|时|:|：|h\b|час)\s*半?", Options);

        /// <summary>
        /// A lone 十 in front of a unit is the number ten ("十点" is 10:00). The shared converter leaves it
        /// alone because it cannot tell a number from a name that is literally called "十".
        /// </summary>
        private static readonly Regex LoneTen = new(@"(?<![0-9零一二三四五六七八九十百千万两])十(?=\s*(月|点|時|时|:|：|h|час))", Options);

        /// <summary>A number left over once every unit was removed ("八点三十分" keeps the 30).</summary>
        private static readonly Regex LooseNumber = new(@"(?<![0-9])[0-9]{1,4}(?![0-9])", Options);

        public static VoiceTimeWindow Parse(string? text, string? lang, DateTime now) =>
            Parse(text, lang, now, DefaultTimeHintMinutes, true);

        public static VoiceTimeWindow Parse(string? text, string? lang, DateTime now, int timeHintMinutes,
            bool parseCalendarDates = true)
        {
            var normalized = VoiceText.Normalize(text);
            var compact = VoiceText.Compact(normalized);

            var calendar = parseCalendarDates ? ResolveCalendarDate(compact, now) : null;
            var (day, label) = calendar ?? ResolveDay(compact, now);
            var (fromHour, toHour, partLabel) = ResolvePart(compact);
            var (hour, minute) = ResolveClock(compact);

            if (hour >= 0)
            {
                // An explicit clock time means the user remembers when it started, not the exact range.
                // "晚上8点" is 20:00, so the part of the day has to be folded in before the window is built.
                hour = To24Hour(hour, fromHour);
                var start = day.Date.AddHours(hour).AddMinutes(minute);
                var end = start.AddMinutes(ProgrammeSpanMinutes);
                start = start.AddMinutes(-Math.Clamp(timeHintMinutes, 0, 240));
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

        /// <summary>
        /// Removes every time expression the parser understands, so what is left of "回看今天晚上的八点
        /// 新闻联播" is the programme name. The title filter must not be fed the words the window already
        /// consumed; that is what used to leave "20点 新闻联" behind and sink the search.
        /// </summary>
        public static string StripTimeHints(string? text)
        {
            var normalized = VoiceText.Normalize(text);
            if (normalized.Length == 0) return "";

            var value = VoiceText.Compact(normalized);
            value = ReplaceAll(value, TimeWords.JustNow);
            value = ReplaceAll(value, TimeWords.Day);
            value = ReplaceAll(value, TimeWords.ThisWeek);
            value = ReplaceAll(value, TimeWords.LastWeek);
            value = ReplaceAll(value, TimeWords.Parts);
            foreach (var weekday in TimeWords.Weekdays) value = ReplaceAll(value, weekday.Words);
            value = value.Replace("上星期", " ", StringComparison.Ordinal).Replace("下星期", " ", StringComparison.Ordinal);

            value = NormalizeLoneTen(VoiceText.HanToArabicDigits(value));
            value = CalendarDate.Replace(value, " ");
            value = CalendarDayOnly.Replace(value, " ");
            value = ClockForStripping.Replace(value, " ");
            value = LooseNumber.Replace(value, " ");
            return value.Trim();
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

        /// <summary>
        /// A spoken calendar date, which is what "回看10月13号的新闻联播" needs before anything else: no word
        /// list can carry every date. A date without a year that is still in the future means last year, and
        /// a bare day beyond today means last month.
        /// </summary>
        private static (DateTime Date, string Label)? ResolveCalendarDate(string compact, DateTime now)
        {
            var text = NormalizeLoneTen(VoiceText.HanToArabicDigits(compact));

            var match = CalendarDate.Match(text);
            if (match.Success
                && int.TryParse(match.Groups["m"].Value, out var month) && month is >= 1 and <= 12
                && int.TryParse(match.Groups["d"].Value, out var day) && day >= 1)
            {
                var explicitYear = match.Groups["y"].Success
                    && int.TryParse(match.Groups["y"].Value, out var spokenYear) && spokenYear is >= 1970 and <= 2999;
                var year = explicitYear ? int.Parse(match.Groups["y"].Value) : now.Year;
                if (day <= DateTime.DaysInMonth(year, month))
                {
                    var date = new DateTime(year, month, day);
                    if (explicitYear) return (date, $"{date.Year}年{month}月{day}日");
                    if (date.Date > now.Date) date = date.AddYears(-1);
                    return (date, $"{month}月{day}日");
                }
            }

            var dayOnly = CalendarDayOnly.Match(text);
            if (dayOnly.Success && int.TryParse(dayOnly.Groups["d"].Value, out var dayNumber) && dayNumber is >= 1 and <= 31
                && dayNumber <= DateTime.DaysInMonth(now.Year, now.Month))
            {
                var date = new DateTime(now.Year, now.Month, dayNumber);
                if (date.Date > now.Date)
                {
                    var previous = date.AddMonths(-1);
                    if (dayNumber <= DateTime.DaysInMonth(previous.Year, previous.Month)) date = previous;
                }
                return (date, $"{date.Month}月{date.Day}日");
            }

            return null;
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
            foreach (var weekday in TimeWords.Weekdays)
                foreach (var word in weekday.Words)
                    if (compact.Contains(VoiceText.Compact(word), StringComparison.Ordinal)) return weekday.Day;
            return -1;
        }

        private static string WeekdayName(int day) => day switch
        {
            1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", 6 => "六", _ => "日"
        };

        // ---------------------------------------------------------------- time of day

        private static (int From, int To, string? Label) ResolvePart(string compact)
        {
            // The table is ordered, so "凌晨" is asked before "早上" and "今天晚上" before "晚上".
            foreach (var part in TimeWords.PartsOfDay)
                if (ContainsAny(compact, part.Words))
                    return (part.From, part.To, string.IsNullOrEmpty(part.Label) ? null : part.Label);
            return (-1, -1, null);
        }

        /// <summary>
        /// Folds a spoken hour into the 24 hour clock using the part of the day that was said: "晚上8点" is
        /// 20:00, "早上8点" stays 08:00. Without a part word the hour is taken as spoken, and 凌晨12点 is
        /// midnight while 中午12点 is noon.
        /// </summary>
        private static int To24Hour(int hour, int fromHour)
        {
            if (fromHour < 0) return hour;
            if (fromHour >= 12 && hour >= 1 && hour <= 11) return hour + 12;
            if (fromHour == 0 && hour == 12) return 0;
            return hour;
        }

        private static (int Hour, int Minute) ResolveClock(string compact)
        {
            var text = NormalizeLoneTen(VoiceText.HanToArabicDigits(compact));
            var match = Clock.Match(text);
            if (!match.Success) return (-1, 0);
            if (!int.TryParse(match.Groups["h"].Value, out var hour)) return (-1, 0);
            if (hour > 23) return (-1, 0);

            var minute = match.Groups["m"].Success && int.TryParse(match.Groups["m"].Value, out var parsed) ? parsed : 0;
            if (!match.Groups["m"].Success)
            {
                // "八点半": the half hour follows the unit the match stopped on.
                var after = match.Index + match.Length;
                if (after < text.Length && text[after] == '半') minute = 30;
            }
            return (hour, Math.Clamp(minute, 0, 59));
        }

        // ---------------------------------------------------------------- helpers

        private static string NormalizeLoneTen(string text) => text.Length == 0 ? text : LoneTen.Replace(text, "10");

        private static string ReplaceAll(string value, IReadOnlyList<string> words)
        {
            foreach (var word in words)
            {
                var needle = VoiceText.Compact(word);
                if (needle.Length > 0) value = value.Replace(needle, " ", StringComparison.Ordinal);
            }
            return value;
        }

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
