using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>
    /// Writes the sentence the page shows and reads out. Every line lives in the WebRemoteStrings
    /// dictionaries, so the answer follows the same four languages as the rest of the remote page
    /// instead of being hard coded here.
    /// </summary>
    internal static class VoiceAnswer
    {
        public static Dictionary<string, string> Strings(string lang) => WebRemoteStrings.Get(lang);

        public static string Text(Dictionary<string, string> s, string key, string fallback) =>
            s.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

        /// <summary>Formats "已切换到 {0}（{1}台）" style templates; missing keys fall back to English.</summary>
        public static string Format(Dictionary<string, string> s, string key, string fallback, params object[] args)
        {
            var template = Text(s, key, fallback);
            try { return string.Format(CultureInfo.CurrentCulture, template, args); }
            catch (FormatException) { return template; }
        }

        public static string JoinTitles(IReadOnlyList<VoiceOption> options, int max = 5)
        {
            var titles = options.Take(max).Select(o => string.IsNullOrEmpty(o.Subtitle) ? o.Title : $"{o.Title} {o.Subtitle}");
            return string.Join("、", titles);
        }

        public static string NotUnderstood(Dictionary<string, string> s, string lang) =>
            $"{Text(s, "voice_not_understood", "没听懂。")} {string.Join("、", VoiceAliases.HelpExamples(lang))}";

        /// <summary>Summary of the matched programme window, e.g. "今天共 12 个节目可以回看".</summary>
        public static string ReplaySummary(Dictionary<string, string> s, string label, int count) =>
            Format(s, "voice_replay_summary", "{0}共 {1} 个节目可以回看", label, count);

        /// <summary>Short "HH:mm" range for a candidate line.</summary>
        public static string TimeRange(DateTime start, DateTime end) =>
            $"{start:HH:mm}-{end:HH:mm}";
    }
}
