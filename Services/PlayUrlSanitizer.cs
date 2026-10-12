using System;
using System.Linq;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Normalizes a playlist url into the exact string the player is asked to open: the "$quality"
    /// suffix used by the rtp2httpd / udpxy playlists is dropped and a non ascii path is
    /// percent-encoded. Replay and timeshift build their archive request on top of the live url, so
    /// they have to see the same value playback uses instead of the raw playlist entry.
    /// </summary>
    internal static class PlayUrlSanitizer
    {
        internal static string? Clean(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            var s = input.Trim();

            var idx = s.IndexOf('$');
            if (idx > 0) s = s.Substring(0, idx);
            s = s.Trim().TrimEnd(',');

            try
            {
                if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !s.Contains("%"))
                {
                    var hasNonAscii = s.Any(c => c > 127);
                    if (hasNonAscii) return new Uri(s).AbsoluteUri;
                }
            }
            catch { }

            return s;
        }
    }
}
