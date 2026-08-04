using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LibmpvIptvClient.Services
{
    public static class EpgMatcher
    {
        private static readonly string[] JunkWords = new[]
        {
            "HD", "SD", "FHD", "HEVC", "H.264", "H.265", "4K", "1080P", "720P", "50FPS", "60FPS",
            "[高清]", "(高清)", "高清", "标清", "超清", "测试", "试验", "IPV6", "IPV4", "OTT",
            "LIVE", "STREAM", "CHANNEL"
        };

        private static readonly string[] CctvTypeSuffixes = new[]
        {
            "综合", "综合频道", "中文国际", "中文国际频道",
            "财经", "综艺", "体育", "电影", "纪录", "科教", "戏曲",
            "社会与法", "社会与法频道", "新闻", "少儿", "音乐",
            "军事农业", "国防军事", "农业农村", "电视剧",
            "体育赛事", "奥林匹克", "4K综艺", "4K"
        };

        private static readonly string[] CctvPrefixes = new[]
        {
            "CCTV", "CCTV-", "CCTV", "中央台", "中央电视台"
        };

        public static string? Match(string sourceName, IEnumerable<string> epgNames)
        {
            if (string.IsNullOrWhiteSpace(sourceName)) return null;

            var cleanSource = CleanName(sourceName);

            foreach (var epgName in epgNames)
            {
                var cleanEpg = CleanName(epgName);
                if (cleanSource == cleanEpg) return epgName;
            }

            foreach (var epgName in epgNames)
            {
                if (IsSmartMatch(sourceName, epgName)) return epgName;
            }

            return null;
        }

        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var s = name.ToUpperInvariant();

            foreach (var junk in JunkWords)
            {
                s = s.Replace(junk, "");
            }

            s = Regex.Replace(s, @"\([^\)]*\)|\[[^\]]*\]|【[^】]*】", "");

            s = Regex.Replace(s, @"[\s_\.\:\+\-]", "");

            s = s.Replace("一", "1").Replace("二", "2").Replace("三", "3")
                 .Replace("四", "4").Replace("五", "5").Replace("六", "6")
                 .Replace("七", "7").Replace("八", "8").Replace("九", "9")
                 .Replace("十", "10").Replace("0", "0");

            return s;
        }

        private static bool IsSmartMatch(string src, string dst)
        {
            var cleanSrc = CleanName(src);
            var cleanDst = CleanName(dst);

            var numSrc = ExtractNumbers(cleanSrc);
            var numDst = ExtractNumbers(cleanDst);

            if (numSrc.Count > 0 || numDst.Count > 0)
            {
                if (!numSrc.SequenceEqual(numDst)) return false;
            }

            var textSrc = Regex.Replace(cleanSrc, @"\d", "");
            var textDst = Regex.Replace(cleanDst, @"\d", "");

            if (textSrc == textDst) return true;

            if (IsStationAlias(textSrc, textDst)) return true;

            if (IsCctvTypeMatch(textSrc, textDst)) return true;

            return false;
        }

        private static bool IsCctvTypeMatch(string textSrc, string textDst)
        {
            if (!IsCctvName(textSrc) || !IsCctvName(textDst)) return false;

            foreach (var suffix in CctvTypeSuffixes)
            {
                var normalizedSrc = textSrc.Replace(suffix, "");
                var normalizedDst = textDst.Replace(suffix, "");

                if (normalizedSrc == normalizedDst && normalizedSrc.StartsWith("CCTV"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCctvName(string name)
        {
            return name.StartsWith("CCTV") && name.Length > 4;
        }

        private static List<int> ExtractNumbers(string s)
        {
            var list = new List<int>();
            var matches = Regex.Matches(s, @"\d+");
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Value, out var v)) list.Add(v);
            }
            return list;
        }

        private static bool IsStationAlias(string a, string b)
        {
            var suffixes = new[] { "卫视", "电视台", "台", "频道", "TV" };
            foreach (var suf in suffixes)
            {
                a = a.Replace(suf, "");
                b = b.Replace(suf, "");
            }
            return a == b && a.Length > 1;
        }
    }
}
