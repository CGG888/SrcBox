namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>
    /// One recorded utterance uploaded from the phone as raw 16 bit little endian PCM. The desktop side
    /// only carries the samples: whichever speech engine sits behind the callback reads them and returns
    /// a sentence, so the remote never needs an online recogniser of its own.
    /// </summary>
    public sealed class VoiceAudioClip
    {
        /// <summary>Mono samples; empty when the client uploaded nothing usable.</summary>
        public short[] Samples { get; init; } = System.Array.Empty<short>();

        public int SampleRateHz { get; init; } = 16000;

        /// <summary>Language the sentence is expected to be in, e.g. zh-CN; the router localises with it.</summary>
        public string Lang { get; init; } = "zh-CN";

        /// <summary>Length of the clip, derived from the sample count so it can never disagree with it.</summary>
        public double Seconds => SampleRateHz > 0 ? (double)Samples.Length / SampleRateHz : 0;
    }
}
