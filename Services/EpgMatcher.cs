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
            "综合", "综合频道", "中文国际", "中文国际频道", "中文国际",
            "财经", "综艺", "体育", "电影", "纪录", "科教", "戏曲",
            "社会与法", "社会与法频道", "新闻", "少儿", "音乐",
            "军事农业", "国防军事", "农业农村", "电视剧",
            "体育赛事", "奥林匹克", "4K综艺", "4K",
            "美洲", "欧洲", "亚洲",
            "兵器科技", "第一剧场", "电视指南", "风云剧场", "风云音乐", "风云足球",
            "高尔夫网球", "怀旧剧场", "女性时尚", "世界地理", "卫生健康",
            "央视台球", "央视文化精品", "央视国学", "央视资讯",
            "文化精品", "发现之旅", "中学生", "纪录国际", "全球资讯榜",
            "欢乐大本营", "欢乐综艺", "家庭健康", "健康之路", "中国音乐",
            "早期教育", "职业教育", "电视批判", "学术导视", "旅游天地",
            "走进科学", "自然之谜", "科技博览", "人文地图", "大家谈",
            "文化视界", "科技教育", "法治在线", "天天快乐", "生活广角",
            "生活空间", "健康之星", "夕阳红", "老年之家", "父母大人",
            "儿童影院", "动画王国", "童话剧场", "儿童故事", "智慧树"
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
            if (!IsCctvRelatedName(textSrc) || !IsCctvRelatedName(textDst)) return false;

            var srcPrefix = ExtractCctvPrefix(textSrc);
            var dstPrefix = ExtractCctvPrefix(textDst);

            if (srcPrefix != dstPrefix && !(srcPrefix == "CCTV" && dstPrefix == "CETV") &&
                !(srcPrefix == "CETV" && dstPrefix == "CCTV"))
            {
                return false;
            }

            foreach (var suffix in CctvTypeSuffixes)
            {
                var normalizedSrc = textSrc.Replace(suffix, "");
                var normalizedDst = textDst.Replace(suffix, "");

                if (normalizedSrc == normalizedDst &&
                    (normalizedSrc.StartsWith("CCTV") || normalizedSrc.StartsWith("CETV") || normalizedSrc.StartsWith("CGTN")))
                {
                    return true;
                }
            }

            var numSrc = ExtractNumbers(textSrc);
            var numDst = ExtractNumbers(textDst);
            if (numSrc.Count > 0 && numDst.Count > 0 && numSrc.SequenceEqual(numDst))
            {
                var prefixMatch = srcPrefix == dstPrefix ||
                                 (srcPrefix == "CCTV" && dstPrefix == "CETV") ||
                                 (srcPrefix == "CETV" && dstPrefix == "CCTV");
                if (prefixMatch) return true;
            }

            return false;
        }

        private static bool IsCctvRelatedName(string name)
        {
            return name.StartsWith("CCTV") || name.StartsWith("CETV") || name.StartsWith("CGTN") || name.StartsWith("CHTV");
        }

        private static string ExtractCctvPrefix(string name)
        {
            if (name.StartsWith("CCTV")) return "CCTV";
            if (name.StartsWith("CETV")) return "CETV";
            if (name.StartsWith("CGTN")) return "CGTN";
            if (name.StartsWith("CHTV")) return "CHTV";
            return "";
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
