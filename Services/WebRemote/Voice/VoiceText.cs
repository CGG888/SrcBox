using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>
    /// Text preparation for matching. Everything downstream (aliases, fuzzy scoring, time parsing) works on
    /// the normalised form, so this is the single place that knows about full-width characters, Chinese
    /// numerals and the punctuation phones like to insert.
    /// </summary>
    public static class VoiceText
    {
        private static readonly char[] Separators = { ' ', '　', ',', '，', '。', '.', '!', '！', '?', '？', '、', ';', '；', ':', '：', '"', '\'', '“', '”', '‘', '’', '(', ')', '（', '）', '[', ']', '【', '】', '-', '—', '~', '～', '|', '/', '\\' };
        private static readonly char[] Trimmable = { ' ', '的', '了', '吧', '呀', '啊', '呢', '哦', '嘛', '我', '你', '请', '帮', '要', '想', '给' };

        /// <summary>Lower-cases, folds full-width ASCII, strips punctuation, collapses spaces.</summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var builder = new StringBuilder(text.Length);
            var lastWasSpace = false;
            foreach (var raw in text.Trim())
            {
                var c = raw;
                if (c >= '０' && c <= '９') c = (char)(c - '０' + '0');
                else if (c >= 'Ａ' && c <= 'Ｚ') c = (char)(c - 'Ａ' + 'a');
                else if (c >= 'ａ' && c <= 'ｚ') c = (char)(c - 'ａ' + 'a');
                else if (c == '　') c = ' ';

                if (Array.IndexOf(Separators, c) >= 0)
                {
                    if (!lastWasSpace && builder.Length > 0) { builder.Append(' '); lastWasSpace = true; }
                    continue;
                }

                if (c == ' ')
                {
                    if (!lastWasSpace && builder.Length > 0) { builder.Append(' '); lastWasSpace = true; }
                    continue;
                }

                if (char.IsUpper(c)) c = char.ToLowerInvariant(c);
                builder.Append(c);
                lastWasSpace = false;
            }

            var collapsed = builder.ToString().Trim();
            // A trailing particle is noise for matching but meaningful for nothing else.
            while (collapsed.Length > 1 && Array.IndexOf(Trimmable, collapsed[^1]) >= 0)
                collapsed = collapsed[..^1].TrimEnd();
            return collapsed;
        }

        /// <summary>Names are compared without spaces or punctuation so "CCTV 5" and "cctv5" match.</summary>
        public static string Compact(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
                if (!char.IsWhiteSpace(c) && Array.IndexOf(Separators, c) < 0)
                    builder.Append(char.ToLowerInvariant(c));
            return builder.ToString();
        }

        /// <summary>
        /// Converts Chinese numerals to Arabic digits ("二十一台" -> "21台"). A numeral run only converts when
        /// it both starts and ends with a numeral character and contains a structural one (十/百/千/万/零 or
        /// 两), so a channel literally named "二十四小时" is left alone and still matches by name.
        /// </summary>
        public static string HanToArabicDigits(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder(text.Length);
            var i = 0;
            while (i < text.Length)
            {
                if (!IsHanNumeral(text[i])) { builder.Append(text[i]); i++; continue; }

                var j = i;
                while (j < text.Length && IsHanNumeral(text[j])) j++;
                var run = text[i..j];
                if (ShouldConvert(run) && TryParseHanNumber(run, out var value))
                    builder.Append(value.ToString(CultureInfo.InvariantCulture));
                else
                    builder.Append(run);
                i = j;
            }
            return builder.ToString();
        }

        private static bool IsHanNumeral(char c) => "零一二三四五六七八九十百千万两壹贰叁肆伍陆柒捌玖拾佰仟".IndexOf(c) >= 0;

        private static bool ShouldConvert(string run)
        {
            if (run.Length < 1) return false;
            if (!IsHanNumeral(run[0]) || !IsHanNumeral(run[^1])) return false;
            if (run.Length == 1) return "零一二三四五六七八九两".IndexOf(run[0]) >= 0;
            foreach (var c in run)
                if ("十百千万零两".IndexOf(c) >= 0) return true;
            return false;
        }

        /// <summary>Chinese numeral to int, up to 万. Returns false for anything that is not a plain number.</summary>
        public static bool TryParseHanNumber(string run, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(run)) return false;

            var total = 0;   // everything already closed off by a 万
            var section = 0; // the section being built, below 万
            var number = 0;  // digits that have not been given a unit yet
            var seen = false;

            foreach (var c in run)
            {
                if (TryDigit(c, out var digit))
                {
                    seen = true;
                    // 零 is a placeholder ("一千零五"); digits in a row read as one number ("二三" -> 23).
                    if (digit != 0) number = number == 0 ? digit : number * 10 + digit;
                    continue;
                }

                var unit = UnitOf(c);
                if (unit == 0) return false;
                seen = true;

                if (unit == 10000)
                {
                    total = (total + section + (number == 0 ? 1 : number)) * 10000;
                    section = 0;
                    number = 0;
                    continue;
                }

                // 十三 => 13: a unit with nothing in front of it counts as one.
                section += (number == 0 ? 1 : number) * unit;
                number = 0;
            }

            value = total + section + number;
            return seen;
        }

        private const string SimpleDigits = "零一二三四五六七八九";
        private const string FormalDigits = "零壹贰叁肆伍陆柒捌玖";

        private static bool TryDigit(char c, out int digit)
        {
            const string simple = SimpleDigits;
            const string formal = FormalDigits;
            var index = simple.IndexOf(c);
            if (index >= 0) { digit = index; return true; }
            index = formal.IndexOf(c);
            if (index >= 0) { digit = index; return true; }
            if (c == '两') { digit = 2; return true; }
            digit = 0;
            return false;
        }

        private static int UnitOf(char c) => c switch
        {
            '十' or '拾' => 10,
            '百' or '佰' => 100,
            '千' or '仟' => 1000,
            '万' => 10000,
            _ => 0
        };

        /// <summary>
        /// The first spoken number together with its raw text. Arabic digits win; otherwise a Chinese
        /// numeral run is used, which is what makes "声音大点" style commands work without a number.
        /// </summary>
        public static bool TrySpokenNumber(string? text, out int value, out string raw)
        {
            value = 0;
            raw = "";
            if (string.IsNullOrEmpty(text)) return false;

            for (var i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i])) continue;
                var j = i;
                while (j < text.Length && char.IsDigit(text[j])) j++;
                raw = text[i..j];
                return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
            }

            for (var i = 0; i < text.Length; i++)
            {
                if (!IsHanNumeral(text[i])) continue;
                var j = i;
                while (j < text.Length && IsHanNumeral(text[j])) j++;
                var run = text[i..j];
                if (!ShouldConvert(run)) continue;
                if (!TryParseHanNumber(run, out value)) continue;
                raw = run;
                return true;
            }
            return false;
        }

        public static int? FirstNumber(string? text) =>
            TrySpokenNumber(text, out var value, out _) ? value : null;

        /// <summary>Arabic value of a text that is only a number/numeral, else null.</summary>
        public static int? OnlyNumber(string? text)
        {
            var compact = Normalize(text).Replace(" ", "");
            if (compact.Length == 0) return null;
            if (compact.All(char.IsDigit))
                return int.TryParse(compact, NumberStyles.None, CultureInfo.InvariantCulture, out var direct) ? direct : null;
            if (TryParseHanNumber(compact, out var han)) return han;
            return null;
        }

        /// <summary>Keeps the caller's list in order while removing blanks and duplicates for display.</summary>
        public static IReadOnlyList<string> Distinct(IEnumerable<string>? values)
        {
            var result = new List<string>();
            if (values == null) return result;
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (result.Contains(value)) continue;
                result.Add(value);
            }
            return result;
        }
    }
}
