namespace LibmpvIptvClient.Architecture.Application.Player
{
    public interface IPlayerEngine
    {
        void Play(string url);
        void Stop();
        void Pause(bool paused);
        void SeekAbsolute(double seconds);
        void SeekRelative(double seconds);
        void SetVolume(double volume);
        void SetMute(bool muted);
        void SetSpeed(double speed);
        void SetAspectRatio(string ratio);

        /// <summary>
        /// 应用反交错设置（deinterlace + field-parity + vf）
        /// </summary>
        void SetDeinterlace(string mode, string? fieldParity = null, string? algorithm = null);
        double? GetTimePos();
        double? GetDuration();
        void EnsureReadyForLoad();
        bool IsEofReached();
        void LoadWithPrefetch(string url, System.Collections.Generic.IEnumerable<string> nextUrls);
        void SetPropertyString(string name, string value);
        void SetRecordingMode(bool recording);
        string? GetPropertyString(string name);
        double? GetPropertyDouble(string name);
        long? GetPropertyLong(string name);
        bool? GetPropertyBool(string name);
    }
}