using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Expands the catchup/timeshift time placeholders used by rtp2httpd, Kodi and the Chinese IPTV
    /// players: <c>${(b)yyyyMMddHHmmss}</c>, <c>${utc:YmdHMS}</c>, <c>{utcend:...}</c>,
    /// <c>${timestamp}</c>, <c>{duration}</c> and the fixed <c>{start}</c>/<c>{end}</c> pair.
    /// </summary>
    public static class UrlPlaceholderExpander
    {
        static readonly Regex s_rtp2httpdMacroRegex =
            new Regex(@"\$\{\((b|e)\)(.*?)\}", RegexOptions.Compiled);

        // rtp2httpd's own macro syntax carries a leading '$' (`${utc:yyyyMMddHHmmss}`). It used to be
        // left in the url, producing "playseek=$20260319125300-..." and breaking the upstream request.
        static readonly Regex s_utcPlaceholderRegex =
            new Regex(@"\$?\{utc:(.*?)\}", RegexOptions.Compiled);

        static readonly Regex s_utcendPlaceholderRegex =
            new Regex(@"\$?\{utcend:(.*?)\}", RegexOptions.Compiled);

        public static string Expand(string url, DateTime start, DateTime end, bool appendEpgTime = false)
        {
            if (string.IsNullOrEmpty(url)) return url ?? "";

            // 0. Decode only the escapes that hide placeholders (e.g. $%7B(b)yyyyMMdd%7CUTC%7D).
            // Decoding the whole url would corrupt the path/query (e.g. %2B -> +, %E5%.. -> CJK).
            url = DecodePlaceholderEscapes(url);

            // 1. Unix timestamp & duration (rtp2httpd macros)
            url = url.Replace("${timestamp}", Unix(start)).Replace("{timestamp}", Unix(start));
            url = url.Replace("${end_timestamp}", Unix(end)).Replace("{end_timestamp}", Unix(end));
            url = url.Replace("${duration}", Duration(start, end)).Replace("{duration}", Duration(start, end));

            // 2. {utc:...} / {utcend:...}
            url = s_utcPlaceholderRegex.Replace(url, m => FormatUtcPlaceholder(m.Groups[1].Value, start.ToUniversalTime()));
            url = s_utcendPlaceholderRegex.Replace(url, m => FormatUtcPlaceholder(m.Groups[1].Value, end.ToUniversalTime()));

            // 3. ${(b)...} / ${(e)...}
            url = s_rtp2httpdMacroRegex.Replace(url, m =>
            {
                var fmt = ExpandMacroName(m.Groups[2].Value);
                var dt = m.Groups[1].Value == "b" ? start : end;

                // Both the "${(b)yyyyMMdd|UTC}" and the "${(b)yyyyMMdd:utc}" spellings mean UTC.
                if (fmt.EndsWith("|UTC", StringComparison.OrdinalIgnoreCase))
                {
                    dt = dt.ToUniversalTime();
                    fmt = fmt.Substring(0, fmt.Length - 4);
                }
                else if (fmt.EndsWith(":utc", StringComparison.OrdinalIgnoreCase))
                {
                    dt = dt.ToUniversalTime();
                    fmt = fmt.Substring(0, fmt.Length - 4);
                }

                if (IsUnixFormat(fmt))
                    return new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

                return Format(dt, fmt, m.Value);
            });

            // 4. Fixed local time placeholders
            url = url.Replace("{start}", start.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            url = url.Replace("{end}", end.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));

            // 5. Optional epg_time correlation parameter (minute level)
            if (appendEpgTime)
            {
                try
                {
                    var minTs = start.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
                    var sep = url.Contains('?') ? "&" : "?";
                    url = url + sep + "epg_time=" + Uri.EscapeDataString(minTs);
                }
                catch { }
            }

            return url;
        }

        /// <summary>
        /// Decodes just the escapes that can hide a placeholder, so the path and query keep their
        /// original percent-encoding (a decoded CJK path was previously re-encoded as GBK by the
        /// mpv marshalling layer and reached the upstream as garbage).
        /// </summary>
        internal static string DecodePlaceholderEscapes(string url)
        {
            if (url.IndexOf('%') < 0) return url;
            return url
                .Replace("%7B", "{").Replace("%7b", "{")
                .Replace("%7D", "}").Replace("%7d", "}")
                .Replace("%24", "$");
        }

        static string FormatUtcPlaceholder(string fmt, DateTime dt)
            => Format(dt, ExpandMacroName(fmt), "{utc:" + fmt + "}");

        static string ExpandMacroName(string fmt) => fmt switch
        {
            "YmdHMS" => "yyyyMMddHHmmss",
            "Ymd" => "yyyyMMdd",
            "HMS" => "HHmmss",
            _ => fmt
        };

        static bool IsUnixFormat(string fmt) =>
            fmt.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
            fmt.Equals("unix", StringComparison.OrdinalIgnoreCase) ||
            fmt.Equals("epoch", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Only .NET date/time specifiers are accepted. 't' is deliberately excluded: it is the
        /// AM/PM designator, so the old code turned "yyyyMMddHHmmss:utc" into "...:u下c" on a
        /// Chinese Windows and the upstream could not parse the time.
        /// </summary>
        static bool IsSafeDateFormat(string fmt)
        {
            foreach (var c in fmt)
            {
                if ("yMdHhmsfFz:./- ".IndexOf(c) < 0) return false;
            }
            return true;
        }

        static string Format(DateTime dt, string fmt, string fallback)
        {
            if (string.IsNullOrEmpty(fmt) || !IsSafeDateFormat(fmt)) return fallback;
            try { return dt.ToString(fmt, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        static string Unix(DateTime dt) =>
            new DateTimeOffset(dt).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        static string Duration(DateTime start, DateTime end) =>
            ((long)(end - start).TotalSeconds).ToString(CultureInfo.InvariantCulture);
    }
}
