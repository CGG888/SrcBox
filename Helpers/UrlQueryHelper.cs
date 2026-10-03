namespace LibmpvIptvClient.Helpers
{
    /// <summary>Query string helpers for playback urls.</summary>
    public static class UrlQueryHelper
    {
        /// <summary>
        /// Appends extra query parameters to a url, choosing "?" or "&amp;" as needed. Blank input is a
        /// no-op and a leading "?"/"&amp;" in <paramref name="extraQuery"/> is tolerated, so the value can
        /// be pasted straight from a documentation page.
        /// </summary>
        public static string AppendQuery(string? url, string? extraQuery)
        {
            if (string.IsNullOrWhiteSpace(url)) return url ?? "";

            var extra = (extraQuery ?? "").Trim();
            while (extra.Length > 0 && (extra[0] == '?' || extra[0] == '&')) extra = extra.Substring(1).TrimStart();
            if (extra.Length == 0) return url!;

            return url!.Contains("?") ? url + "&" + extra : url + "?" + extra;
        }
    }
}
