using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>One phrasing: a regular expression plus the verb it means and the groups to read.</summary>
    internal sealed class VoicePattern
    {
        public Regex Matcher { get; init; } = null!;
        public VoiceVerb Verb { get; init; }
        public string Name { get; init; } = "";

        /// <summary>Group name holding a number the router must read (volume, seek, step).</summary>
        public string NumberGroup { get; init; } = "";

        /// <summary>Group name holding a relative volume change such as "大点".</summary>
        public string RelativeGroup { get; init; } = "";

        /// <summary>Fixed argument for a phrasing that carries none ("next", "-1", "first").</summary>
        public string Arg { get; init; } = "";
    }

    /// <summary>
    /// The vocabulary one sentence is read with. The built-in tables live here; a grammar pack installed in
    /// <see cref="DefaultDirectory"/> replaces the sections it carries, so the words can be tuned and shipped
    /// on their own. <see cref="Current"/> notices a new or changed file by itself, and falls back to the
    /// built-in tables whenever a pack cannot be read.
    /// </summary>
    public sealed class VoiceGrammar
    {
        /// <summary>A pack must not be able to hang the voice thread with a catastrophic expression.</summary>
        public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

        /// <summary>The version of the tables compiled into the binary.</summary>
        public const string BuiltInVersion = "1";

        private static readonly object Gate = new();
        private static VoiceGrammar? _current;
        private static string _key = "";
        private static string _error = "";
        private static string _source = "";
        private static string _version = "";
        private static string _lang = "zh-CN";
        private static VoiceGrammarPack? _pack;

        private static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SrcBox", "voice");

        /// <summary>Where an installed or hand-dropped pack is looked for. Tests point this at a scratch directory.</summary>
        internal static string? DirectoryOverride { get; set; }

        /// <summary>Where an installed or hand-dropped pack is looked for.</summary>
        public static string DefaultDirectory =>
            string.IsNullOrWhiteSpace(DirectoryOverride) ? DefaultPath : DirectoryOverride!;

        /// <summary>
        /// Built on first use rather than in a field initializer: the tables below are declared later in the
        /// file, and a field initializer would run before them.
        /// </summary>
        private static readonly Lazy<VoiceGrammar> BuiltIn = new(BuildBuiltIn);

        /// <summary>The built-in tables, always valid.</summary>
        public static VoiceGrammar Default => BuiltIn.Value;

        /// <summary>The tables in force: the built-in ones, or those with an installed pack on top.</summary>
        public static VoiceGrammar Current
        {
            get
            {
                Refresh();
                lock (Gate) return _current ?? Default;
            }
        }

        // ---------------------------------------------------------------- tables

        internal IReadOnlyList<VoicePattern> Patterns { get; private init; } = Array.Empty<VoicePattern>();
        internal IReadOnlyList<string> Prepositions { get; private init; } = Array.Empty<string>();
        public IReadOnlyList<string> NoiseWords { get; private init; } = Array.Empty<string>();
        internal IReadOnlyList<string> Day { get; private init; } = Array.Empty<string>();
        internal IReadOnlyList<string> ThisWeek { get; private init; } = Array.Empty<string>();
        internal IReadOnlyList<string> LastWeek { get; private init; } = Array.Empty<string>();
        internal IReadOnlyList<string> JustNow { get; private init; } = Array.Empty<string>();

        /// <summary>The parts of the day, in the order they are tested; the first hit wins.</summary>
        internal IReadOnlyList<VoiceGrammarPart> Parts { get; private init; } = Array.Empty<VoiceGrammarPart>();

        /// <summary>Every part-of-day word, flat, for stripping a sentence down to its title.</summary>
        internal IReadOnlyList<string> PartWords { get; private init; } = Array.Empty<string>();

        internal IReadOnlyList<VoiceGrammarWeekday> Weekdays { get; private init; } = Array.Empty<VoiceGrammarWeekday>();

        /// <summary>Words the offline recogniser should be biased towards, when it can be.</summary>
        public IReadOnlyList<string> Hotwords { get; private init; } = Array.Empty<string>();

        public string Lang { get; private init; } = "zh-CN";
        public string Version { get; private init; } = BuiltInVersion;

        /// <summary>File the tables came from; empty means the built-in ones.</summary>
        public string Source { get; private init; } = "";

        public bool IsBuiltIn => Source.Length == 0;

        // ---------------------------------------------------------------- installed pack state

        /// <summary>Version of the pack in force, or the built-in version when there is none.</summary>
        public static string InstalledVersion
        {
            get
            {
                Refresh();
                lock (Gate) return _version.Length > 0 ? _version : BuiltInVersion;
            }
        }

        public static string InstalledLang
        {
            get
            {
                Refresh();
                lock (Gate) return _lang;
            }
        }

        public static string InstalledSource
        {
            get
            {
                Refresh();
                lock (Gate) return _source;
            }
        }

        /// <summary>Why the pack on disk was ignored; empty when everything loaded.</summary>
        public static string LastError
        {
            get
            {
                Refresh();
                lock (Gate) return _error;
            }
        }

        /// <summary>What the installed pack says about itself, for the settings page.</summary>
        public static VoiceGrammarPack? InstalledPack
        {
            get
            {
                Refresh();
                lock (Gate) return _pack?.Summary();
            }
        }

        /// <summary>Forgets the cached pack, so the next read picks up an install or a removal.</summary>
        public static void Reload()
        {
            lock (Gate)
            {
                _current = null;
                _key = "";
                _pack = null;
                _error = "";
                _source = "";
                _version = "";
            }
        }

        // ---------------------------------------------------------------- loading

        private static void Refresh()
        {
            lock (Gate)
            {
                var dir = DefaultDirectory;
                var pointer = Path.Combine(dir, "current.json");

                // A pack dropped in by hand works too: the pointer is only needed for a version and a hash.
                var dropped = Path.Combine(dir, "pack.json");
                var stamp = File.Exists(pointer) ? File.GetLastWriteTimeUtc(pointer).Ticks : 0;
                var dropStamp = File.Exists(dropped) ? File.GetLastWriteTimeUtc(dropped).Ticks : 0;
                var key = $"{dir}|{stamp}|{dropStamp}";
                if (_current != null && key == _key) return;

                _key = key;
                _error = "";
                _pack = null;
                _source = "";
                _version = "";

                var path = stamp != 0 ? pointer : (dropStamp != 0 ? dropped : "");
                if (path.Length == 0)
                {
                    _lang = "zh-CN";
                    _current = Default;
                    return;
                }

                var loaded = Load(dir, path, out var error);
                if (loaded == null)
                {
                    _error = error;
                    _current = Default;
                    return;
                }

                _pack = loaded;
                _source = path;
                _version = loaded.Version;
                _lang = string.IsNullOrWhiteSpace(loaded.Lang) ? "zh-CN" : loaded.Lang;
                _current = Merge(Default, loaded, path);
            }
        }

        private static VoiceGrammarPack? Load(string dir, string path, out string error)
        {
            error = "";
            try
            {
                var text = File.ReadAllText(path);
                var fromPointer = path.EndsWith("current.json", StringComparison.OrdinalIgnoreCase);
                if (fromPointer)
                {
                    var pointer = JsonSerializer.Deserialize<VoiceGrammarPointer>(text, VoiceGrammarPointer.Json);
                    if (pointer == null || string.IsNullOrWhiteSpace(pointer.File))
                    {
                        error = "the installed pack names no file";
                        return null;
                    }

                    var root = Path.GetFullPath(dir);
                    var file = Path.GetFullPath(Path.Combine(root, pointer.File));
                    if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "the installed pack points outside its directory";
                        return null;
                    }
                    if (!File.Exists(file))
                    {
                        error = "the installed pack file is missing";
                        return null;
                    }
                    if (!string.IsNullOrWhiteSpace(pointer.Sha256))
                    {
                        var hash = VoiceModelStore.HashFile(file, null, CancellationToken.None);
                        if (!hash.Equals(pointer.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            error = "the installed pack does not match its checksum";
                            return null;
                        }
                    }
                    text = File.ReadAllText(file);
                }

                var pack = VoiceGrammarPack.Parse(text, out var parseError);
                if (pack == null)
                {
                    error = $"the pack cannot be read: {parseError}";
                    return null;
                }

                var problems = pack.Validate();
                if (problems.Count > 0)
                {
                    error = "the pack is not usable: " + string.Join("; ", problems);
                    return null;
                }
                return pack;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        internal static VoiceGrammar Merge(VoiceGrammar basis, VoiceGrammarPack pack, string source)
        {
            var time = pack.TimeWords;
            var parts = time?.Parts is { Count: > 0 } ? time.Parts : basis.Parts;
            return new VoiceGrammar
            {
                Patterns = pack.Phrases.Count > 0 ? Compile(pack.Phrases) : basis.Patterns,
                Prepositions = pack.Prepositions.Count > 0 ? pack.Prepositions : basis.Prepositions,
                NoiseWords = pack.NoiseWords.Count > 0 ? pack.NoiseWords : basis.NoiseWords,
                Day = time?.Day is { Count: > 0 } ? time.Day : basis.Day,
                ThisWeek = time?.ThisWeek is { Count: > 0 } ? time.ThisWeek : basis.ThisWeek,
                LastWeek = time?.LastWeek is { Count: > 0 } ? time.LastWeek : basis.LastWeek,
                JustNow = time?.JustNow is { Count: > 0 } ? time.JustNow : basis.JustNow,
                Parts = parts,
                PartWords = parts.SelectMany(p => p.Words).ToArray(),
                Weekdays = time?.Weekdays is { Count: > 0 } ? time.Weekdays : basis.Weekdays,
                Hotwords = pack.Hotwords,
                Lang = string.IsNullOrWhiteSpace(pack.Lang) ? basis.Lang : pack.Lang,
                Version = pack.Version,
                Source = source
            };
        }

        private static IReadOnlyList<VoicePattern> Compile(IEnumerable<VoiceGrammarPhrase> phrases)
        {
            var patterns = new List<VoicePattern>();
            foreach (var phrase in phrases)
            {
                if (!Enum.TryParse<VoiceVerb>(phrase.Verb, true, out var verb) || verb == VoiceVerb.Unknown) continue;
                if (string.IsNullOrWhiteSpace(phrase.Pattern)) continue;
                Regex matcher;
                try
                {
                    matcher = new Regex(phrase.Pattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                patterns.Add(new VoicePattern
                {
                    Matcher = matcher,
                    Verb = verb,
                    Name = phrase.Name,
                    NumberGroup = phrase.NumberGroup,
                    RelativeGroup = phrase.RelativeGroup,
                    Arg = phrase.Arg
                });
            }
            return patterns;
        }

        /// <summary>
        /// The tables in force written out as a pack. The copy published under voice/ in the repository is
        /// generated from this, and the test suite compares the two, so a word added here has to be published
        /// as well.
        /// </summary>
        internal VoiceGrammarPack AsPack() => new()
        {
            Schema = VoiceGrammarPack.CurrentSchema,
            Lang = Lang,
            Version = Version,
            Notes = "The vocabulary SrcBox ships with.",
            Phrases = Patterns.Select(pattern => new VoiceGrammarPhrase
            {
                Name = pattern.Name,
                Verb = pattern.Verb.ToString(),
                Pattern = pattern.Matcher.ToString(),
                NumberGroup = pattern.NumberGroup,
                RelativeGroup = pattern.RelativeGroup,
                Arg = pattern.Arg
            }).ToList(),
            Prepositions = Prepositions.ToList(),
            NoiseWords = NoiseWords.ToList(),
            TimeWords = new VoiceGrammarTimeTable
            {
                Day = Day.ToList(),
                ThisWeek = ThisWeek.ToList(),
                LastWeek = LastWeek.ToList(),
                JustNow = JustNow.ToList(),
                Parts = Parts.Select(part => new VoiceGrammarPart
                {
                    Words = part.Words.ToList(),
                    From = part.From,
                    To = part.To,
                    Label = part.Label
                }).ToList(),
                Weekdays = Weekdays.Select(weekday => new VoiceGrammarWeekday
                {
                    Words = weekday.Words.ToList(),
                    Day = weekday.Day
                }).ToList()
            },
            Hotwords = Hotwords.ToList()
        };

        // ---------------------------------------------------------------- built-in tables

        private static VoiceGrammar BuildBuiltIn() => new()
        {
            Patterns = VoiceAliases.BuildBuiltInPatterns(),
            Prepositions = VoiceAliases.BuildBuiltInPrepositions(),
            NoiseWords = BuiltInNoiseWords,
            Day = BuiltInDay,
            ThisWeek = BuiltInThisWeek,
            LastWeek = BuiltInLastWeek,
            JustNow = BuiltInJustNow,
            Parts = BuiltInParts,
            PartWords = BuiltInParts.SelectMany(p => p.Words).ToArray(),
            Weekdays = BuiltInWeekdays,
            Hotwords = Array.Empty<string>(),
            Lang = "zh-CN",
            Version = BuiltInVersion,
            Source = ""
        };

        private static readonly string[] BuiltInNoiseWords =
        {
            "回看", "回放", "重播", "今天", "今日", "昨天", "昨日", "前天", "明天", "后天", "本日", "节目",
            "有什么", "什么", "看一下", "看看", "看", "帮我", "请", "我要", "我想", "在播", "正在把",
            "现在", "正在", "播", "今晚", "昨天晚上", "晚上", "上午", "下午", "中午", "早上", "凌晨"
        };

        private static readonly string[] BuiltInDay =
        {
            "今天", "今日", "本日", "当天", "今天早上", "今天晚上", "昨天", "昨日", "前天", "明天", "后天",
            "today", "yesterday", "tomorrow", "day before yesterday",
            "сегодня", "вчера", "позавчера", "завтра"
        };

        private static readonly string[] BuiltInThisWeek =
        {
            "本周", "这周", "這個星期", "这个星期", "本星期", "this week", "эта неделя", "на этой неделе"
        };

        private static readonly string[] BuiltInLastWeek =
        {
            "上周", "上個星期", "上个星期", "上星期", "last week", "на прошлой неделе"
        };

        private static readonly string[] BuiltInJustNow =
        {
            "刚才", "刚刚", "方才", "just now", "a moment ago", "только что"
        };

        /// <summary>
        /// The parts of the day, in the order they are tested. The order is precedence: "凌晨" has to be
        /// asked before "早上", and "今天晚上" before "晚上".
        /// </summary>
        private static readonly VoiceGrammarPart[] BuiltInParts =
        {
            new() { Words = { "凌晨" }, From = 0, To = 6, Label = "凌晨" },
            new() { Words = { "早上", "早晨", "上午", "morning", "утром", "утр" }, From = 6, To = 12, Label = "上午" },
            new() { Words = { "中午", "noon", "полдень" }, From = 11, To = 14, Label = "中午" },
            new() { Words = { "下午", "afternoon", "днём", "днем" }, From = 12, To = 18, Label = "下午" },
            new() { Words = { "今晚", "今天晚上", "tonight", "сегодня вечером" }, From = 18, To = 24, Label = "晚上" },
            new() { Words = { "昨晚", "昨天晚上", "last night", "вчера вечером" }, From = 18, To = 24, Label = "晚上" },
            new() { Words = { "晚上", "夜里", "夜间", "evening", "night", "вечер", "ночью" }, From = 18, To = 24, Label = "晚上" }
        };

        private static readonly VoiceGrammarWeekday[] BuiltInWeekdays =
        {
            new() { Words = { "星期一", "周一", "礼拜一", "monday", "понедельник" }, Day = 1 },
            new() { Words = { "星期二", "周二", "礼拜二", "tuesday", "вторник" }, Day = 2 },
            new() { Words = { "星期三", "周三", "礼拜三", "wednesday", "среда", "среду" }, Day = 3 },
            new() { Words = { "星期四", "周四", "礼拜四", "thursday", "четверг" }, Day = 4 },
            new() { Words = { "星期五", "周五", "礼拜五", "friday", "пятница", "пятницу" }, Day = 5 },
            new() { Words = { "星期六", "周六", "礼拜六", "saturday", "суббота", "субботу" }, Day = 6 },
            new() { Words = { "星期日", "星期天", "周日", "周天", "礼拜天", "sunday", "воскресенье" }, Day = 0 }
        };
    }
}
