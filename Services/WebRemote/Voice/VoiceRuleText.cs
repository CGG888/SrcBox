using System;
using System.Collections.Generic;
using System.Linq;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>
    /// Reads and writes the one-line-per-rule syntax the settings field uses, so custom phrases can be
    /// typed without hand-editing JSON:
    ///
    ///   <code>
    ///   看新闻=channel:CCTV-13
    ///   回看新闻=replay:CCTV-13
    ///   !新闻频道|新闻台=channel:CCTV-13
    ///   ~第(.+)频道=channel:$1
    ///   </code>
    ///
    /// A leading '!' means exact matching, '~' means a regular expression and '*' the contains form the
    /// default already uses. Phrases on one line are separated by '|', and everything after the first
    /// colon is the target, so a URL target stays intact.
    /// </summary>
    public static class VoiceRuleText
    {
        public static List<VoiceRule> Parse(string? text)
        {
            var rules = new List<VoiceRule>();
            if (string.IsNullOrWhiteSpace(text)) return rules;

            foreach (var rawLine in text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;

                var split = line.IndexOf('=');
                if (split <= 0 || split == line.Length - 1) continue;

                var left = line.Substring(0, split).Trim();
                var right = line.Substring(split + 1).Trim();
                if (left.Length == 0 || right.Length == 0) continue;

                var kind = "contains";
                if (left.StartsWith("!")) { kind = "exact"; left = left.Substring(1).Trim(); }
                else if (left.StartsWith("~")) { kind = "regex"; left = left.Substring(1).Trim(); }
                else if (left.StartsWith("*")) { kind = "macro"; left = left.Substring(1).Trim(); }

                var phrases = left.Split('|').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                if (phrases.Count == 0) continue;

                // "replay:CCTV-13" splits into action and target; a bare "pause" has no target. The action
                // is always plain letters, which keeps "selectSource:http://host/list.m3u" in one piece.
                var action = right;
                var target = "";
                var colon = right.IndexOf(':');
                if (colon > 0 && right.Substring(0, colon).All(c => char.IsLetter(c) || c == '_'))
                {
                    action = right.Substring(0, colon).Trim();
                    target = right.Substring(colon + 1).Trim();
                }

                if (action.Length == 0) continue;
                rules.Add(new VoiceRule { Kind = kind, Phrases = phrases, Action = action, Target = target });
            }

            return rules;
        }

        public static string Format(IEnumerable<VoiceRule>? rules)
        {
            if (rules == null) return "";

            var lines = new List<string>();
            foreach (var rule in rules)
            {
                if (rule?.Phrases == null || rule.Phrases.Count == 0) continue;
                if (string.IsNullOrWhiteSpace(rule.Action)) continue;

                var prefix = rule.ResolveKind() switch
                {
                    RuleKind.Exact => "!",
                    RuleKind.Regex => "~",
                    RuleKind.Macro => "*",
                    _ => ""
                };
                var phrases = string.Join("|", rule.Phrases.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
                if (phrases.Length == 0) continue;

                var right = string.IsNullOrWhiteSpace(rule.Target) ? rule.Action : $"{rule.Action}:{rule.Target}";
                lines.Add($"{prefix}{phrases}={right}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
