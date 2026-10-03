using System;
using System.Collections.Generic;
using System.Linq;

namespace LibmpvIptvClient.Helpers
{
    /// <summary>
    /// Channel-name normalisation for the "{name}" logo template. Logo repositories are generated from
    /// cleaned names (quality tags, spaces and edge punctuation removed, "CCTV-" -> "CCTV",
    /// "PLUS" -> "+"), so the raw channel name usually 404s while the cleaned one exists.
    /// </summary>
    public static class LogoNameCleaner
    {
        // Tokens that logo repositories strip (mirrors the CCSH/IPTV generator's removal list).
        static readonly string[] NoiseTokens =
        {
            "（HD）", "(HD)", "[HD]", "HD", "超清", "高清", "标清", "4K", "UHD",
            "1080P", "1080p", "720P", "720p", "480P", "480p",
            "[BD]", "[VGA]", "[SD]", "(1080p)", "(720p)", "(480p)", "🎞️"
        };

        public static string Clean(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name.Trim();

            foreach (var token in NoiseTokens)
            {
                s = s.Replace(token, "", StringComparison.OrdinalIgnoreCase);
            }

            s = s.Replace("-PLUS", "+", StringComparison.OrdinalIgnoreCase)
                 .Replace("PLUS", "+", StringComparison.OrdinalIgnoreCase)
                 .Replace("CCTV-", "CCTV", StringComparison.OrdinalIgnoreCase);

            s = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
            return s.Trim('-', '_', '·', '|', '/', '\\', '.');
        }

        /// <summary>Candidate logo names in the order to try them: cleaned first, then the raw name.</summary>
        public static IReadOnlyList<string> Candidates(string? name)
        {
            var list = new List<string>();

            void add(string? value)
            {
                value = (value ?? "").Trim();
                if (value.Length == 0) return;
                if (list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) return;
                list.Add(value);
            }

            add(Clean(name));
            add(name);
            return list;
        }
    }
}
