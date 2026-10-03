using System;
using System.Collections.Generic;
using System.Globalization;
using LibmpvIptvClient.Architecture.Application.Player;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// Reads and selects the subtitle tracks embedded in the current stream. mpv publishes the track list
    /// as a node array, but its property layer also resolves the indexed form ("track-list/2/lang"), so
    /// this stays free of mpv_node marshalling and works through the existing IPlayerEngine surface.
    /// </summary>
    public static class SubtitleTrackService
    {
        /// <summary>Upper bound, so a broken property source cannot spin forever.</summary>
        public const int MaxTracks = 64;

        public static List<SubtitleTrackInfo> Read(IPlayerEngine? engine)
        {
            var tracks = new List<SubtitleTrackInfo>();
            if (engine == null) return tracks;

            long count;
            try { count = engine.GetPropertyLong("track-list/count") ?? 0; }
            catch { return tracks; }

            for (var i = 0L; i < count && i < MaxTracks; i++)
            {
                try
                {
                    if (!string.Equals(engine.GetPropertyString($"track-list/{i}/type"), "sub", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var id = engine.GetPropertyLong($"track-list/{i}/id");
                    if (id == null) continue;

                    tracks.Add(new SubtitleTrackInfo(
                        (int)id.Value,
                        engine.GetPropertyString($"track-list/{i}/title"),
                        engine.GetPropertyString($"track-list/{i}/lang"),
                        engine.GetPropertyString($"track-list/{i}/codec"),
                        engine.GetPropertyBool($"track-list/{i}/selected") ?? false,
                        engine.GetPropertyBool($"track-list/{i}/external") ?? false));
                }
                catch { }
            }

            return tracks;
        }

        /// <summary>Selects an embedded subtitle track; a null id turns subtitles off ("no").</summary>
        public static void Select(IPlayerEngine? engine, int? trackId)
        {
            if (engine == null) return;
            try
            {
                engine.SetPropertyString("sid", trackId.HasValue
                    ? trackId.Value.ToString(CultureInfo.InvariantCulture)
                    : "no");
            }
            catch { }
        }

        /// <summary>Menu label for a track: title, language and codec, skipping the empty parts.</summary>
        public static string Describe(SubtitleTrackInfo? track)
        {
            if (track == null) return "";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(track.Title)) parts.Add(track.Title.Trim());
            if (!string.IsNullOrWhiteSpace(track.Language)) parts.Add(track.Language.Trim());
            if (!string.IsNullOrWhiteSpace(track.Codec)) parts.Add(track.Codec.Trim());
            if (parts.Count == 0) parts.Add("#" + track.Id.ToString(CultureInfo.InvariantCulture));

            return string.Join(" · ", parts);
        }
    }
}
