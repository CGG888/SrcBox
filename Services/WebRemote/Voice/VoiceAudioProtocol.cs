using System;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>
    /// Pure decode/validate helpers for the "voiceAudio" websocket message: the page records one utterance,
    /// base64 encodes the PCM and the server turns that back into a clip. Staying free of websocket, player
    /// and WPF types is what lets one upload be tested without a phone.
    /// </summary>
    public static class VoiceAudioProtocol
    {
        /// <summary>Hard cap on one utterance; anything longer is truncated, never rejected.</summary>
        public const int MaxSeconds = 15;

        /// <summary>Rate the page records at, and the one used when the client reports an implausible rate.</summary>
        public const int DefaultSampleRateHz = 16000;

        /// <summary>Language assumed when the client sends none; matches VoiceCommandContext's default.</summary>
        public const string DefaultLang = "zh-CN";

        // A browser records at 8 kHz at the lowest and 192 kHz at the highest; a value outside that band
        // cannot come from a real recording, so it is replaced instead of trusted to compute Seconds.
        private const int MinSampleRateHz = 4000;
        private const int MaxSampleRateHz = 192000;

        /// <summary>
        /// Parses the upload payload and validates it. Returns null and a short reason when the payload
        /// carries no audio at all or cannot be decoded; a clip longer than <see cref="MaxSeconds"/> is
        /// truncated to that cap without an error, because a long hold is a client bug, not bad input.
        /// </summary>
        public static VoiceAudioClip? TryDecode(string? base64, int sampleRateHz, string? lang, out string error)
        {
            error = "";

            if (string.IsNullOrWhiteSpace(base64))
            {
                error = "empty audio";
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                error = "invalid base64 audio";
                return null;
            }

            // One sample needs two bytes; a shorter body is a truncated upload, not a silent recording.
            if (bytes.Length < 2)
            {
                error = "empty audio";
                return null;
            }

            if (sampleRateHz < MinSampleRateHz || sampleRateHz > MaxSampleRateHz)
                sampleRateHz = DefaultSampleRateHz;

            var sampleCount = Math.Min(bytes.Length / 2, sampleRateHz * MaxSeconds);
            var samples = new short[sampleCount];
            // 16 bit little endian is what both a browser and this process produce, so the bytes need no swap.
            Buffer.BlockCopy(bytes, 0, samples, 0, sampleCount * 2);

            return new VoiceAudioClip
            {
                Samples = samples,
                SampleRateHz = sampleRateHz,
                Lang = string.IsNullOrWhiteSpace(lang) ? DefaultLang : lang!.Trim()
            };
        }
    }
}
