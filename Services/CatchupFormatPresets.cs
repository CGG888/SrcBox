using System.Collections.Generic;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// One ready made catchup/timeshift query string the settings page offers next to the url format box.
    /// The templates are all parameter-only, so they are appended to the channel live url (HTTP unicast and
    /// RTSP unicast alike) and the user can still edit the box afterwards.
    /// </summary>
    public sealed class CatchupFormatPreset
    {
        public CatchupFormatPreset(string id, string template)
        {
            Id = id;
            Template = template;
        }

        /// <summary>Stable id. It names the localization key, so an existing id must never be renamed.</summary>
        public string Id { get; }

        /// <summary>Parameter-only template handed to <see cref="UrlPlaceholderExpander"/>.</summary>
        public string Template { get; }

        public string LabelKey => "UI_Catchup_Preset_" + Id;
    }

    /// <summary>
    /// The format catalogue collected from the catchup documentation of the players this app talks to
    /// (Kodi pvr.iptvsimple / APTV / TV Live) plus the two operator shapes reported in issue #6 - a ZTE
    /// style RTSP unicast portal (重庆联通) and a PLTV style playseek portal (广西联通). It is the fastest
    /// way to answer "which parameter does my provider want" without hand editing the m3u: pick the shape,
    /// let it fill the global replay/timeshift template, then test in the player.
    /// </summary>
    public static class CatchupFormatPresets
    {
        public static IReadOnlyList<CatchupFormatPreset> All { get; } = new List<CatchupFormatPreset>
        {
            // playseek is the de-facto standard of the Chinese unicast portals (ZTE/HW boxes and
            // rtp2httpd style relays): "playseek=<start>-<end>".
            new CatchupFormatPreset("playseek_local", "?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}"),
            new CatchupFormatPreset("playseek_utc", "?playseek=${utc:yyyyMMddHHmmss}-${utcend:yyyyMMddHHmmss}"),
            new CatchupFormatPreset("playseek_unix", "?playseek=${timestamp}-${end_timestamp}"),

            // Kodi's own vocabulary: a single "when from" timestamp.
            new CatchupFormatPreset("utc_only", "?time={utc}"),

            // starttime/endtime portals: the UTC + "T" shape is the Huawei/魔百和 one, the plain one the
            // local time variant.
            new CatchupFormatPreset("starttime_utc_t", "?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}"),
            new CatchupFormatPreset("starttime_local", "?starttime=${(b)yyyyMMddHHmmss}&endtime=${(e)yyyyMMddHHmmss}"),

            // Unix seconds, with the window or with a duration.
            new CatchupFormatPreset("start_end_unix", "?start=${timestamp}&end=${end_timestamp}"),
            new CatchupFormatPreset("start_duration", "?start=${timestamp}&duration=${duration}")
        };
    }
}
