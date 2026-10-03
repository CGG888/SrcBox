namespace LibmpvIptvClient.Models
{
    /// <summary>One embedded subtitle track reported by the player (issue #35).</summary>
    public sealed class SubtitleTrackInfo
    {
        public SubtitleTrackInfo(int id, string? title, string? language, string? codec, bool selected, bool external)
        {
            Id = id;
            Title = title ?? "";
            Language = language ?? "";
            Codec = codec ?? "";
            Selected = selected;
            External = external;
        }

        /// <summary>mpv track id, used as the value of the "sid" property.</summary>
        public int Id { get; }
        public string Title { get; }
        public string Language { get; }
        public string Codec { get; }
        public bool Selected { get; }
        public bool External { get; }
    }
}
