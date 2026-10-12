using System;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Builds the catch-up / timeshift playback url for a channel. Extracted from the main window
    /// view model so the url rules are unit tested instead of being buried in the UI layer.
    /// Priority: the per-channel catchup-source, then the global replay template, then the global
    /// timeshift template, then the rtp2httpd playseek request when the channel carries no template
    /// at all. A parameter-only value ("?playseek=...") and a channel flagged catchup="append" are
    /// appended to the channel live url instead of replacing it, which is what PLTV/playseek portals
    /// and the Kodi style playlists expect.
    /// </summary>
    internal static class ArchiveUrlBuilder
    {
        /// <summary>
        /// Url of the channel live stream: the selected source first, then the first source. The value
        /// is cleaned the same way playback cleans it, so an archive query is appended to the url the
        /// player really opens and never to the raw playlist entry.
        /// </summary>
        internal static string GetLiveUrl(Channel? ch)
        {
            if (ch == null) return "";
            if (ch.Tag is Source tagged && !string.IsNullOrWhiteSpace(tagged.Url)) return PlayUrlSanitizer.Clean(tagged.Url) ?? "";
            if (ch.Sources != null && ch.Sources.Count > 0 && !string.IsNullOrWhiteSpace(ch.Sources[0].Url))
                return PlayUrlSanitizer.Clean(ch.Sources[0].Url) ?? "";
            return "";
        }

        /// <summary>True for values that carry no host and therefore have to be appended to the live url.</summary>
        internal static bool IsParameterOnly(string? fmt)
        {
            if (string.IsNullOrWhiteSpace(fmt)) return false;
            var trimmed = fmt.Trim();
            return trimmed.StartsWith("?", StringComparison.Ordinal) || trimmed.StartsWith("&", StringComparison.Ordinal);
        }

        /// <summary>True when the playlist marks the channel with catchup="append".</summary>
        internal static bool IsAppendMode(Channel? ch)
            => ch != null && string.Equals((ch.Catchup ?? "").Trim(), "append", StringComparison.OrdinalIgnoreCase);

        /// <summary>Default port of an rtp2httpd instance.</summary>
        private const int Rtp2httpdDefaultPort = 5140;

        /// <summary>
        /// True for the live urls an rtp2httpd instance serves: its default port and its "/rtsp/"
        /// proxy path. The udpxy style "/rtp/" and "/udp/" paths are deliberately excluded, because
        /// those servers ignore an unknown query and would silently play the live stream instead of
        /// the requested archive window.
        /// </summary>
        internal static bool LooksLikeRtp2httpd(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            var s = url!.Trim();
            if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;

            if (s.IndexOf("/rtsp/", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            try { return new Uri(s).Port == Rtp2httpdDefaultPort; }
            catch { return false; }
        }

        /// <summary>Appends an archive query to the live url, using '&amp;' when the live url already has a query.</summary>
        internal static string AppendToLiveUrl(string liveUrl, string archiveQuery)
        {
            var separator = liveUrl.Contains("?") ? "&" : "?";
            return liveUrl + separator + archiveQuery.TrimStart('?', '&');
        }

        /// <summary>
        /// Expands one replay/timeshift value. Parameter-only values, and values without a scheme
        /// when <paramref name="appendOnly"/> is set, are joined to the channel live url; everything
        /// else is returned as is with the {id} placeholder replaced. Null means no usable url.
        /// </summary>
        internal static string? BuildFromFormat(Channel? ch, string? fmt, bool appendOnly = false)
        {
            if (ch == null || string.IsNullOrWhiteSpace(fmt)) return null;

            var value = fmt!.Trim().Replace("{id}", ch.Id ?? ch.Name);
            if (IsParameterOnly(value) || (appendOnly && !value.Contains("://", StringComparison.Ordinal)))
            {
                var liveUrl = GetLiveUrl(ch);
                if (string.IsNullOrEmpty(liveUrl)) return null;
                return AppendToLiveUrl(liveUrl, value);
            }

            return value;
        }

        /// <summary>
        /// Resolves the archive url handed to the player: the per-channel catchup-source first, then
        /// the global replay template, then the global timeshift template, and finally the rtp2httpd
        /// playseek request for a channel that carries no template of its own.
        /// </summary>
        internal static string? ResolveArchiveUrl(Channel? ch, string? replayFormat, string? timeshiftFormat, bool rtp2httpdTimeZoneMarker = false)
        {
            if (ch == null) return null;

            if (!string.IsNullOrEmpty(ch.CatchupSource))
            {
                var fromChannel = BuildFromFormat(ch, ch.CatchupSource, IsAppendMode(ch));
                if (!string.IsNullOrEmpty(fromChannel)) return fromChannel;
            }

            var fromReplay = BuildFromFormat(ch, replayFormat);
            if (!string.IsNullOrEmpty(fromReplay)) return fromReplay;

            var fromTimeshift = BuildFromFormat(ch, timeshiftFormat);
            if (!string.IsNullOrEmpty(fromTimeshift)) return fromTimeshift;

            // rtp2httpd answers an archive request on the live url with "playseek=START-END", so a
            // channel that carries no template at all is still replayable on such a url. Naive times
            // are read as UTC unless the request carries the "TZ/UTC+N" user agent marker, therefore
            // the time zone aware request can use local times and the plain one has to use UTC.
            var liveUrl = GetLiveUrl(ch);
            if (!LooksLikeRtp2httpd(liveUrl)) return null;

            var playseek = rtp2httpdTimeZoneMarker
                ? "playseek={start}-{end}"
                : "playseek={utc:yyyyMMddHHmmss}-{utcend:yyyyMMddHHmmss}";
            return AppendToLiveUrl(liveUrl, playseek);
        }
    }
}
