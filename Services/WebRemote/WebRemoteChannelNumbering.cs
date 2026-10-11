using System;
using System.Collections.Generic;

namespace LibmpvIptvClient.Services.WebRemote
{
    /// <summary>
    /// Builds the channel numbers used by the web remote page and by the local channel-number overlay.
    /// Numbers follow the playlist group order and ignore group boundaries; the favourites section is
    /// numbered after the playlist groups, so a favourite reuses the number it already received and the
    /// numbering never shifts when the favourites change.
    /// </summary>
    public static class WebRemoteChannelNumbering
    {
        /// <summary>
        /// Numbers the supplied id groups in order (1-based) and returns the id → number map.
        /// Empty ids and repeated ids are ignored so a channel keeps the number it got first.
        /// </summary>
        public static Dictionary<string, int> BuildNumberMap(IEnumerable<IEnumerable<string?>>? groups)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            if (groups == null) return map;

            var nextNumber = 0;
            foreach (var group in groups)
            {
                if (group == null) continue;
                foreach (var id in group)
                {
                    if (string.IsNullOrEmpty(id) || map.ContainsKey(id)) continue;
                    map[id] = ++nextNumber;
                }
            }

            return map;
        }
    }
}
