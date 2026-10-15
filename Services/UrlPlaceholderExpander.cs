using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Expands the catchup/timeshift time placeholders used by rtp2httpd, Kodi (pvr.iptvsimple, so also
    /// APTV / TV Live / IPTV-org playlists) and the Chinese IPTV players: <c>${(b)yyyyMMddHHmmss}</c>,
    /// <c>${utc:YmdHMS}</c>, <c>{utc}</c>, <c>{utcend}</c>, <c>{lutc}</c>, <c>{Y}{m}{d}{H}{M}{S}</c>,
    /// <c>${timestamp}</c>, <c>{duration}</c>/<c>{duration:X}</c>, <c>{offset:X}</c> and the fixed
    /// <c>{start}</c>/<c>{end}</c> pair.
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

        // Kodi accepts a format argument on its own aliases too: "${end:YmdHM}" is "{utcend:YmdHM}".
        static readonly Regex s_kodiStartPlaceholderRegex =
            new Regex(@"\$?\{start:(.*?)\}", RegexOptions.Compiled);

        static readonly Regex s_kodiEndPlaceholderRegex =
            new Regex(@"\$?\{end:(.*?)\}", RegexOptions.Compiled);

        // Kodi scales both values: {duration:60} is the window length in minutes, {offset:60} the
        // distance from the window start to "now" in minutes. Leaving them unexpanded sent the bare
        // "{duration:60}" to the provider, which then rejected the archive request.
        static readonly Regex s_kodiDurationRegex =
            new Regex(@"\{duration:(\d+)\}", RegexOptions.Compiled);

        static readonly Regex s_kodiOffsetRegex =
            new Regex(@"\{offset:(\d+)\}", RegexOptions.Compiled);

        /// <summary>Characters a Kodi format string may carry: its letters plus common separators.</summary>
        const string KodiFormatChars = "YmdHMS:./- _+";

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
            url = s_kodiDurationRegex.Replace(url, m => Scaled(DurationSeconds(start, end), m.Groups[1].Value));
            url = s_kodiOffsetRegex.Replace(url, m => Scaled((long)(DateTime.Now - start).TotalSeconds, m.Groups[1].Value));

            // 1b. Kodi's plain Unix-second tokens: {utc}/{utcend} are the archive window, ${start}/${end}
            //     are Kodi's own aliases of them, and {lutc}/${now} mean "now". SrcBox keeps its documented
            //     meaning of ${timestamp} (the start of the window) instead of Kodi's alias for "now", so
            //     only {lutc}/${now} carry the current instant here.
            var nowUnix = UnixUtc(DateTime.UtcNow);
            url = url.Replace("${utcend}", UnixUtc(end)).Replace("{utcend}", UnixUtc(end));
            url = url.Replace("${utc}", UnixUtc(start)).Replace("{utc}", UnixUtc(start));
            url = url.Replace("${start}", UnixUtc(start)).Replace("${end}", UnixUtc(end));
            url = url.Replace("${lutc}", nowUnix).Replace("{lutc}", nowUnix);
            url = url.Replace("${now}", nowUnix).Replace("{now}", nowUnix);

            // 2. {utc:...} / {utcend:...} plus Kodi's ${start:...} / ${end:...} aliases of them
            url = s_utcPlaceholderRegex.Replace(url, m => FormatUtcPlaceholder(m.Groups[1].Value, start.ToUniversalTime()));
            url = s_utcendPlaceholderRegex.Replace(url, m => FormatUtcPlaceholder(m.Groups[1].Value, end.ToUniversalTime()));
            url = s_kodiStartPlaceholderRegex.Replace(url, m => FormatUtcPlaceholder(m.Groups[1].Value, start.ToUniversalTime()));
            url = s_kodiEndPlaceholderRegex.Replace(url, m => FormatUtcPlaceholder(m.Groups[1].Value, end.ToUniversalTime()));

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

            // 3b. Kodi component tokens of the window start ({Y}-{m}-{d}T{H}:{M}:{S}).
            url = url.Replace("{Y}", start.Year.ToString("D4", CultureInfo.InvariantCulture))
                     .Replace("{m}", start.Month.ToString("D2", CultureInfo.InvariantCulture))
                     .Replace("{d}", start.Day.ToString("D2", CultureInfo.InvariantCulture))
                     .Replace("{H}", start.Hour.ToString("D2", CultureInfo.InvariantCulture))
                     .Replace("{M}", start.Minute.ToString("D2", CultureInfo.InvariantCulture))
                     .Replace("{S}", start.Second.ToString("D2", CultureInfo.InvariantCulture));

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

        static string ExpandMacroName(string fmt)
        {
            // Kodi spells its format strings with the component letters only (`{utc:YmdHM}`,
            // `${end:Ymd-H-M}`), so such a value is translated component by component. Everything else
            // is a .NET format string and is passed through untouched, which keeps the long documented
            // form `${utc:yyyyMMddHHmmss}` working.
            if (IsKodiFormat(fmt)) return TranslateKodiFormat(fmt);

            return fmt switch
            {
                "YmdHMS" => "yyyyMMddHHmmss",
                "Ymd" => "yyyyMMdd",
                "HMS" => "HHmmss",
                _ => fmt
            };
        }

        /// <summary>True for a Kodi style format: only its letters and separators, and at least one letter.</summary>
        static bool IsKodiFormat(string fmt)
        {
            if (string.IsNullOrEmpty(fmt)) return false;

            var hasLetter = false;
            foreach (var c in fmt)
            {
                if (KodiFormatChars.IndexOf(c) < 0) return false;
                if (c is 'Y' or 'm' or 'd' or 'H' or 'M' or 'S') hasLetter = true;
            }
            return hasLetter;
        }

        /// <summary>
        /// Kodi's letters are not .NET's: Kodi's 'm' is the month and 'M' the minute (the opposite of a
        /// .NET custom format string), and 'Y' carries all four year digits.
        /// </summary>
        static string TranslateKodiFormat(string fmt)
        {
            var sb = new StringBuilder(fmt.Length + 8);
            foreach (var c in fmt)
            {
                sb.Append(c switch
                {
                    'Y' => "yyyy",
                    'm' => "MM",
                    'd' => "dd",
                    'H' => "HH",
                    'M' => "mm",
                    'S' => "ss",
                    _ => c.ToString()
                });
            }
            return sb.ToString();
        }

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

        /// <summary>Unix seconds of an instant read as UTC (what Kodi's {utc}/{utcend}/${start}/${end} mean).</summary>
        static string UnixUtc(DateTime dt) =>
            new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        /// <summary>Kodi's "{duration:X}" / "{offset:X}": the value in units of X seconds (minimum 1).</summary>
        static string Scaled(long value, string divisor)
        {
            if (!int.TryParse(divisor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var div) || div < 1)
                div = 1;
            return (value / div).ToString(CultureInfo.InvariantCulture);
        }

        static string Duration(DateTime start, DateTime end) =>
            DurationSeconds(start, end).ToString(CultureInfo.InvariantCulture);

        static long DurationSeconds(DateTime start, DateTime end) => (long)(end - start).TotalSeconds;
    }
}
