using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>How a user-supplied phrase is matched.</summary>
    public enum RuleKind
    {
        /// <summary>The whole sentence must equal the phrase.</summary>
        Exact = 0,

        /// <summary>The sentence must contain the phrase.</summary>
        Contains = 1,

        /// <summary>The phrase is a regular expression.</summary>
        Regex = 2,

        /// <summary>Alias of <see cref="Contains"/>, kept because the settings JSON uses the word "macro".</summary>
        Macro = 3
    }

    /// <summary>One user rule from the settings file.</summary>
    public sealed class VoiceRule
    {
        public string Kind { get; set; } = "contains";
        public List<string> Phrases { get; set; } = new();
        public string Action { get; set; } = "";
        public string Target { get; set; } = "";

        public RuleKind ResolveKind() => (Kind ?? "").Trim().ToLowerInvariant() switch
        {
            "exact" => RuleKind.Exact,
            "regex" or "re" => RuleKind.Regex,
            "macro" => RuleKind.Macro,
            _ => RuleKind.Contains
        };
    }

    /// <summary>
    /// The built-in phrasings, in all four languages the remote ships. Order is precedence: the first
    /// matching pattern decides the verb, so anything that must not be swallowed by a broader pattern
    /// ("返回" before "回看", "取消静音" before "静音") has to be listed above it.
    /// </summary>
    internal static class VoiceAliases
    {
        private sealed class Pattern
        {
            public Regex Matcher { get; init; } = null!;
            public VoiceVerb Verb { get; init; }
            public string Name { get; init; } = "";

            /// <summary>Group name holding a number the router must read (volume, seek, step).</summary>
            public string NumberGroup { get; init; } = "";

            /// <summary>Group name holding a relative volume change such as "大点".</summary>
            public string RelativeGroup { get; init; } = "";

            /// <summary>
            /// Fixed argument the router needs when the sentence itself carries none: a step direction
            /// ("next"/"prev" walk the list, "first"/"last" jump to the ends) has no words to scrape,
            /// so the pattern name is the argument.
            /// </summary>
            public string Arg { get; init; } = "";
        }

        private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Pattern[] Patterns = BuildPatterns();

        private static readonly string[] Prepositions =
        {
            "到", "成", "为", "至", "去", "回", "换到", "切到", "切换到", "调到", "转到", "换", "切", "看",
            "换台", "换频道", "频道", "台", "的", "把", "给我", "一下", "吧",
            // Dictation hears "台" as 代/带 often enough to matter ("放到二代" for 换到2台); neither word ever
            // carries channel identity on its own, so both are scaffolding here.
            "代", "带"
        };

        // ---------------------------------------------------------------- public entry points

        /// <summary>Built-in matching. Returns the verb, the raw argument text and the rule name.</summary>
        public static bool TryBuiltIn(string normalized, out VoiceVerb verb, out string arg, out string ruleName)
        {
            verb = VoiceVerb.Unknown;
            arg = "";
            ruleName = "";
            if (string.IsNullOrEmpty(normalized)) return false;

            var compact = VoiceText.Compact(normalized);
            foreach (var pattern in Patterns)
            {
                var match = pattern.Matcher.Match(compact);
                if (!match.Success) continue;

                verb = pattern.Verb;
                ruleName = pattern.Name;
                arg = !string.IsNullOrEmpty(pattern.Arg) ? pattern.Arg : ExtractArg(compact, pattern, match);
                return true;
            }
            return false;
        }

        /// <summary>User rule matching. <paramref name="target"/> is the captured alias when the rule has one.</summary>
        public static bool TryRule(VoiceRule rule, string normalized, out string target)
        {
            target = "";
            if (rule == null || string.IsNullOrEmpty(normalized)) return false;
            var compact = VoiceText.Compact(normalized);
            var kind = rule.ResolveKind();

            if (rule.Phrases is { Count: > 0 })
            {
                foreach (var phrase in rule.Phrases)
                {
                    if (string.IsNullOrWhiteSpace(phrase)) continue;
                    var needle = VoiceText.Compact(VoiceText.Normalize(phrase));
                    if (needle.Length == 0) continue;

                    switch (kind)
                    {
                        case RuleKind.Exact when compact == needle:
                            target = rule.Target ?? "";
                            return true;
                        case RuleKind.Regex:
                            try
                            {
                                var regex = new Regex(phrase, Options);
                                var match = regex.Match(normalized);
                                if (!match.Success) continue;
                                target = match.Groups.Count > 1 ? match.Groups[1].Value : (rule.Target ?? "");
                                return true;
                            }
                            catch (ArgumentException) { continue; }
                        default:
                            var index = compact.IndexOf(needle, StringComparison.Ordinal);
                            if (index < 0) continue;
                            // A regex-ish capture group in the target is not supported on purpose: user
                            // rules stay declarative, the fuzzy matcher does the guessing.
                            target = rule.Target ?? "";
                            return true;
                    }
                }
                return false;
            }

            // No phrases: treat the target itself as the phrase.
            if (string.IsNullOrWhiteSpace(rule.Target)) return false;
            var fallback = VoiceText.Compact(VoiceText.Normalize(rule.Target));
            if (fallback.Length == 0) return false;
            if (kind == RuleKind.Exact ? compact == fallback : compact.Contains(fallback, StringComparison.Ordinal))
            {
                target = rule.Target ?? "";
                return true;
            }
            return false;
        }

        /// <summary>Strips filler words from an argument so "回看今天的节目" leaves "今天的节目" semantics out.</summary>
        public static string StripFiller(string arg, IReadOnlyList<string> words)
        {
            var value = arg ?? "";
            foreach (var word in words.OrderByDescending(w => w.Length))
                value = value.Replace(word, "", StringComparison.Ordinal);
            return value.Replace(" ", "").Trim();
        }

        /// <summary>What the page shows when nothing matched, so the user learns the vocabulary.</summary>
        public static IReadOnlyList<string> HelpExamples(string lang) => (lang ?? "zh").ToLowerInvariant() switch
        {
            var l when l.StartsWith("en") => new[]
            {
                "switch to CCTV 5", "next channel", "change source", "volume 40", "what is on now",
                "replay today's programmes"
            },
            var l when l.StartsWith("ru") => new[]
            {
                "включи cctv 5", "следующий канал", "смени источник", "громкость 40", "что сейчас идёт"
            },
            var l when l.StartsWith("zh-tw") || l.StartsWith("zh-hk") => new[]
            {
                "換到一台", "切到 CCTV5", "換個源", "音量 40", "現在在播什麼", "回看今天的節目"
            },
            _ => new[]
            {
                "换到一台", "切到 CCTV5", "换个源", "音量 40", "现在在播什么", "回看今天的节目"
            }
        };

        // ---------------------------------------------------------------- argument extraction

        private static string ExtractArg(string compact, Pattern pattern, Match match)
        {
            // A captured number is the argument: "音量 40", "快进 30 秒". For the relative volume forms
            // ("大点声") the number is optional and the direction word is the whole argument.
            if (!string.IsNullOrEmpty(pattern.NumberGroup) && match.Groups[pattern.NumberGroup].Success
                && match.Groups[pattern.NumberGroup].Length > 0)
            {
                var value = match.Groups[pattern.NumberGroup].Value;
                var relative = string.IsNullOrEmpty(pattern.RelativeGroup) ? "" : match.Groups[pattern.RelativeGroup].Value;
                if (pattern.Name == "volume_step" && !string.IsNullOrWhiteSpace(relative)) value = $"{relative} {value}";
                return value.Trim();
            }

            var rest = compact.Remove(match.Index, match.Length);
            if (string.IsNullOrEmpty(pattern.NumberGroup)) rest = Trim(rest);
            return rest;
        }

        private static string Trim(string rest)
        {
            var value = rest;
            var changed = true;
            while (changed && value.Length > 0)
            {
                changed = false;
                foreach (var word in Prepositions.OrderByDescending(w => w.Length))
                {
                    if (value.StartsWith(word, StringComparison.Ordinal))
                    {
                        value = value[word.Length..];
                        changed = true;
                    }
                    else if (value.EndsWith(word, StringComparison.Ordinal))
                    {
                        value = value[..^word.Length];
                        changed = true;
                    }
                }
            }
            return value.Trim();
        }

        private static bool IsDayWord(string value)
        {
            foreach (var word in TimeWords.Day)
                if (value.Contains(word, StringComparison.Ordinal)) return true;
            return false;
        }

        // ---------------------------------------------------------------- pattern table

        private static Pattern[] BuildPatterns()
        {
            var patterns = new List<Pattern>();

            // ---- player transport -------------------------------------------------
            patterns.Add(P("exit", VoiceVerb.Exit, @"(exit|quit|закрыть|выйти|退出|退出播放|返回|关掉|关闭)"));
            patterns.Add(P("fullscreen", VoiceVerb.Fullscreen, @"(fullscreen|full screen|полный экран|全屏|全螢幕)"));
            patterns.Add(P("pause", VoiceVerb.Pause, @"(pause|hold on|пауза|暂停|停止播放|先停|暂停一下)"));
            patterns.Add(P("stop", VoiceVerb.Stop, @"(^stop$|^стоп$|停止)"));
            patterns.Add(P("play", VoiceVerb.Play, @"(^play$|^resume$|^продолжить$|^играть$|^(继续|继续播放|播放|开始播放|开始)$)"));

            // ---- mute -------------------------------------------------------------
            patterns.Add(P("unmute", VoiceVerb.ToggleMute, @"(unmute|turn on the sound|включи звук|取消静音|打开声音|打开音量|恢复声音|静音关闭)"));
            patterns.Add(P("mute", VoiceVerb.ToggleMute, @"(^mute$|mute|^выключи звук$|^静音$|^静音吧$|^关掉声音$)"));

            // ---- volume -----------------------------------------------------------
            patterns.Add(P("volume_step", VoiceVerb.VolumeStep, @"(?<num>[0-9]{1,3})?(?<rel>大点|大声点|小点|小声点|大声一点|小声一点|大一点|小一点|louder|quieter|громче|тише)(?:声|声音)?", numberGroup: "num", relativeGroup: "rel"));
            patterns.Add(P("volume_up", VoiceVerb.VolumeStep, @"(音量|声音|vol(ume)?|громкость)?(调大|加大|增大|提高|up|increase|увеличь|прибавь)(?<num>[0-9]{1,3})?", numberGroup: "num"));
            patterns.Add(P("volume_down", VoiceVerb.VolumeStep, @"(音量|声音|vol(ume)?|громкость)?(调小|减小|降低|down|decrease|уменьши|убавь)(?<num>[0-9]{1,3})?", numberGroup: "num"));
            patterns.Add(P("volume_set", VoiceVerb.VolumeSet, @"(音量|声音|vol(ume)?|громкость)(?:调(?:到|至|成|为)?|设置(?:为|成)?|设为|设成|到|至|为|is|to|=|：|:)?(?<num>[0-9]{1,3})", numberGroup: "num"));

            // ---- seek -------------------------------------------------------------
            patterns.Add(P("seek_back", VoiceVerb.Seek, @"(后退|回退|倒退|rewind|назад)(?<num>[0-9]{1,4})?(秒|s|sec|seconds|сек)?", numberGroup: "num"));
            patterns.Add(P("seek_forward", VoiceVerb.Seek, @"(快进|前进|前移|skip|вперёд|вперед)(?<num>[0-9]{1,4})?(秒|s|sec|seconds|сек)?", numberGroup: "num"));

            // ---- channel step -----------------------------------------------------
            patterns.Add(P("next", VoiceVerb.ChannelStep, @"(下一个频道|下一频道|下一个台|下个台|下一台|下一个|下个|下一|next channel|next|频道加|следующий канал|следующий)", arg: "1"));
            patterns.Add(P("prev", VoiceVerb.ChannelStep, @"(上一个频道|上一频道|上一个台|上个台|上一台|上一个|上个|上一|prev(channel)?|previous|previous channel|频道减|предыдущий канал|предыдущий)", arg: "-1"));
            patterns.Add(P("first", VoiceVerb.ChannelStep, @"(第一个频道|第一个台|首个频道|第一台|first channel|первый канал)", arg: "first"));
            patterns.Add(P("last", VoiceVerb.ChannelStep, @"(最后一个频道|最后一个台|末频道|last channel|последний канал)", arg: "last"));

            // ---- stream variant of the current channel ----------------------------
            patterns.Add(P("stream_next", VoiceVerb.SwitchStream, @"(换下一(个)?源|换下一个(流|线路)|下一路|next source|next stream|следующий источник)", arg: "next"));
            patterns.Add(P("stream_prev", VoiceVerb.SwitchStream, @"(换上一(个)?源|上一路|prev(ious)? source|предыдущий источник)", arg: "prev"));
            patterns.Add(P("stream", VoiceVerb.SwitchStream, @"(换(个|一个)?源|切换源|换源|换个线路|换线路|换流|换个流|换地址|change (the )?source|switch (the )?source|смени источник|сменить источник)"));

            // ---- channel data source (playlist) ----------------------------------
            patterns.Add(P("playlist", VoiceVerb.LoadPlaylist, @"(?<num>[0-9]{1,2})?\s*(播放列表|频道列表|节目源|数据源|频道数据源|playlist|плейлист)"));

            // ---- EPG --------------------------------------------------------------
            // These sit above "channel_switch" on purpose: "回看昨天的节目" would otherwise be read as
            // "switch to a channel called 回看".
            patterns.Add(P("epg_now", VoiceVerb.EpgNow, @"(现在(在)?播(什么|啥)?|现在(在)?播放|正在播放|正在播(什么|啥)|当前(节目|播出)|在播什么|what'?s on( now)?|what is on|current programme|что (сейчас )?идёт|что сейчас)"));
            patterns.Add(P("epg_replay", VoiceVerb.EpgReplay, @"(回看|回放|重播|看回看|replay|watch back|посмотреть|повтор)"));

            // ---- channel switch (broadest, must stay last) -----------------------
            // "放到/翻到/拨到" are the verbs dictation keeps producing for 换到; they carry the same channel
            // argument, and without them the sentence only reaches the fuzzy matcher as noise.
            patterns.Add(P("channel_switch", VoiceVerb.SwitchChannel, @"(换到|切到|切换到|调到|转到|放到|放进|翻到|翻|拨到|拨|换台|换频道|换|切|转|看|播放|switch to|change to|turn to|go to|put on|watch|включи|переключи(сь)? на|поставь)"));

            return patterns.ToArray();
        }

        private static Pattern P(string name, VoiceVerb verb, string pattern,
            string numberGroup = "", string relativeGroup = "", string arg = "") => new()
        {
            Name = name,
            Verb = verb,
            Matcher = new Regex(pattern, Options),
            NumberGroup = numberGroup,
            RelativeGroup = relativeGroup,
            Arg = arg
        };
    }

    /// <summary>Vocabulary the time parser and the alias table agree on.</summary>
    internal static class TimeWords
    {
        public static readonly string[] Day =
        {
            "今天", "今日", "本日", "当天", "今天早上", "今天晚上", "昨天", "昨日", "前天", "明天", "后天",
            "today", "yesterday", "tomorrow", "day before yesterday",
            "сегодня", "вчера", "позавчера", "завтра"
        };

        public static readonly string[] ThisWeek =
        {
            "本周", "这周", "這個星期", "这个星期", "本星期", "this week", "эта неделя", "на этой неделе"
        };

        public static readonly string[] LastWeek =
        {
            "上周", "上個星期", "上个星期", "上星期", "last week", "на прошлой неделе"
        };

        public static readonly string[] JustNow =
        {
            "刚才", "刚刚", "方才", "just now", "a moment ago", "только что"
        };

        /// <summary>Time-of-day words; part of the scaffolding a replay question is stripped of.</summary>
        public static readonly string[] Parts =
        {
            "凌晨", "早上", "早晨", "上午", "中午", "下午", "晚上", "今晚", "昨晚", "昨天晚上",
            "夜里", "夜间", "morning", "noon", "afternoon", "evening", "tonight", "last night",
            "утром", "вечером", "днём", "днем", "ночью"
        };
    }
}
