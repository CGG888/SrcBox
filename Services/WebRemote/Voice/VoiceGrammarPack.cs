using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>One phrasing of the table, in the shape the built-in grammar is written in.</summary>
    public sealed class VoiceGrammarPhrase
    {
        public string Name { get; set; } = "";
        public string Verb { get; set; } = "";
        public string Pattern { get; set; } = "";

        /// <summary>Group holding a number the router must read (volume, seek, step).</summary>
        public string NumberGroup { get; set; } = "";

        /// <summary>Group holding a relative volume change such as "大点".</summary>
        public string RelativeGroup { get; set; } = "";

        /// <summary>Fixed argument for a phrasing that carries none ("next", "-1", "first").</summary>
        public string Arg { get; set; } = "";
    }

    /// <summary>Every spelling of one weekday.</summary>
    public sealed class VoiceGrammarWeekday
    {
        /// <summary>"星期一", "周一", "monday"; matched as substrings of the compacted sentence.</summary>
        public List<string> Words { get; set; } = new();

        /// <summary>The <see cref="DayOfWeek"/> value: Sunday is 0 and Saturday is 6.</summary>
        public int Day { get; set; }
    }

    /// <summary>Words that mean one part of the day, and the hours they cover.</summary>
    public sealed class VoiceGrammarPart
    {
        public List<string> Words { get; set; } = new();

        /// <summary>First hour of the part, used to fold "晚上8点" onto the 24 hour clock.</summary>
        public int From { get; set; } = -1;

        /// <summary>Last hour of the part; 24 means midnight.</summary>
        public int To { get; set; } = -1;

        /// <summary>What the reply calls this part ("晚上").</summary>
        public string Label { get; set; } = "";
    }

    /// <summary>The four word lists the time parser reads a sentence with.</summary>
    public sealed class VoiceGrammarTimeTable
    {
        public List<string> Day { get; set; } = new();
        public List<string> ThisWeek { get; set; } = new();
        public List<string> LastWeek { get; set; } = new();
        public List<string> JustNow { get; set; } = new();
        public List<VoiceGrammarPart> Parts { get; set; } = new();
        public List<VoiceGrammarWeekday> Weekdays { get; set; } = new();
    }

    /// <summary>
    /// The vocabulary the router matches with, as a file. The app ships one version of it inside the
    /// binary; a pack in %LOCALAPPDATA%\SrcBox\voice replaces the sections it carries, so the words can be
    /// tuned, translated and shipped on their own without a new build. A section the pack leaves out keeps
    /// the built-in one, which is what makes a small delta pack possible.
    /// </summary>
    public sealed class VoiceGrammarPack
    {
        public const int CurrentSchema = 1;

        /// <summary>Largest pack we accept; the whole vocabulary is a few kilobytes.</summary>
        public const int MaxBytes = 512 * 1024;

        /// <summary>Largest number of entries in any one list.</summary>
        public const int MaxEntries = 500;

        public int Schema { get; set; } = CurrentSchema;
        public string Lang { get; set; } = "";
        public string Version { get; set; } = "";
        public string Updated { get; set; } = "";
        public string Notes { get; set; } = "";

        /// <summary>The phrasing table, in precedence order: the first match decides the verb.</summary>
        public List<VoiceGrammarPhrase> Phrases { get; set; } = new();

        /// <summary>Scaffolding trimmed off a channel argument ("换到", "台").</summary>
        public List<string> Prepositions { get; set; } = new();

        /// <summary>Words a channel name may not absorb.</summary>
        public List<string> NoiseWords { get; set; } = new();

        /// <summary>Day, week and time-of-day words; weekdays included.</summary>
        public VoiceGrammarTimeTable? TimeWords { get; set; }

        /// <summary>Words the offline recogniser should be biased towards, when it can be.</summary>
        public List<string> Hotwords { get; set; } = new();

        internal static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,

            // Written out as it reads: a pack is meant to be opened and edited by hand.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>Reads a pack, or explains why it cannot be read.</summary>
        public static VoiceGrammarPack? Parse(string? json, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(json)) { error = "empty"; return null; }
            if (json.Length > MaxBytes) { error = "too large"; return null; }
            try
            {
                var pack = JsonSerializer.Deserialize<VoiceGrammarPack>(json, Json);
                if (pack == null) { error = "empty"; return null; }
                return pack;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        /// <summary>Everything that would stop this pack from being used; empty means it is fine.</summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            if (Schema != CurrentSchema) problems.Add($"schema {Schema} is not {CurrentSchema}");
            if (string.IsNullOrWhiteSpace(Lang)) problems.Add("lang is empty");
            if (string.IsNullOrWhiteSpace(Version)) problems.Add("version is empty");
            if (Phrases.Count > MaxEntries) problems.Add($"more than {MaxEntries} phrases");
            if (Prepositions.Count > MaxEntries) problems.Add($"more than {MaxEntries} prepositions");
            if (NoiseWords.Count > MaxEntries) problems.Add($"more than {MaxEntries} noise words");
            if (Hotwords.Count > MaxEntries) problems.Add($"more than {MaxEntries} hotwords");

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var phrase in Phrases)
            {
                var where = string.IsNullOrWhiteSpace(phrase.Name) ? phrase.Pattern : phrase.Name;
                if (string.IsNullOrWhiteSpace(phrase.Name)) problems.Add("a phrase has no name");
                else if (!names.Add(phrase.Name)) problems.Add($"duplicate phrase {phrase.Name}");
                if (!Enum.TryParse<VoiceVerb>(phrase.Verb, true, out var verb) || verb == VoiceVerb.Unknown)
                    problems.Add($"{where}: unknown verb {phrase.Verb}");
                if (string.IsNullOrWhiteSpace(phrase.Pattern)) { problems.Add($"{where}: no pattern"); continue; }
                try
                {
                    // A pack must not be able to pin the voice thread on a catastrophic expression, so the
                    // same timeout the router matches with is applied here.
                    _ = new Regex(phrase.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        VoiceGrammar.RegexTimeout);
                }
                catch (ArgumentException ex)
                {
                    problems.Add($"{where}: {ex.Message}");
                }
            }

            var time = TimeWords;
            if (time != null)
            {
                foreach (var weekday in time.Weekdays)
                {
                    if (weekday.Day is < 0 or > 6) problems.Add($"weekday {weekday.Day} is not a DayOfWeek");
                    if (weekday.Words.Count == 0) problems.Add("a weekday has no words");
                }

                foreach (var part in time.Parts)
                {
                    if (part.Words.Count == 0) problems.Add("a part of the day has no words");
                    if (part.From is < 0 or > 24 || part.To is < 0 or > 24 || part.To < part.From)
                        problems.Add($"part {part.Label} has the hours {part.From}-{part.To}");
                }
            }

            return problems;
        }

        /// <summary>Everything except the words: what the settings page needs to describe a pack.</summary>
        internal VoiceGrammarPack Summary() => new()
        {
            Schema = Schema,
            Lang = Lang,
            Version = Version,
            Updated = Updated,
            Notes = Notes,
            Hotwords = Hotwords
        };
    }

    /// <summary>What <c>current.json</c> holds: which pack file is installed and what it must hash to.</summary>
    internal sealed class VoiceGrammarPointer
    {
        public int Schema { get; set; } = VoiceGrammarPack.CurrentSchema;
        public string Lang { get; set; } = "";
        public string Version { get; set; } = "";
        public string File { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Bytes { get; set; }
        public string Installed { get; set; } = "";

        internal static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }
}
