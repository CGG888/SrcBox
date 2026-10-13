using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ManySpeech.AliParaformerAsr;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>
    /// Runs the downloaded model in-process. Loading it costs seconds, so the engine is created on first
    /// use and kept warm; one short utterance then costs far less than the round trip that brought it here.
    /// </summary>
    public sealed class OfflineSpeechRecognizer : IDisposable
    {
        private static readonly object SharedGate = new object();
        private static OfflineSpeechRecognizer? _shared;
        private static string _sharedDirectory = "";
        private static readonly System.Text.RegularExpressions.Regex TranscriptTag =
            new System.Text.RegularExpressions.Regex(@"<\|[^|>]*\|>", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex TranscriptSpace =
            new System.Text.RegularExpressions.Regex(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly object _gate = new object();
        private readonly string _directory;
        private OfflineRecognizer? _recognizer;

        public OfflineSpeechRecognizer(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        /// <summary>Model directory this instance reads from.</summary>
        public string Directory => _directory;

        /// <summary>
        /// One recognizer per model directory: rebuilding the ONNX session per sentence would cost more
        /// than the sentence itself, and two sessions would double the memory for no gain.
        /// </summary>
        public static OfflineSpeechRecognizer For(string? directory)
        {
            var resolved = VoiceModelStore.Resolve(directory);
            lock (SharedGate)
            {
                if (_shared == null || !string.Equals(_sharedDirectory, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    _shared?.Dispose();
                    _shared = new OfflineSpeechRecognizer(resolved);
                    _sharedDirectory = resolved;
                }

                return _shared;
            }
        }

        /// <summary>True when the engine can run: the model files are on disk.</summary>
        public static bool IsReady(string? directory = null) => VoiceModelStore.IsInstalled(directory);

        /// <summary>
        /// Recognises one utterance. Runs on a worker thread because the first call also loads the model.
        /// </summary>
        public Task<string> TranscribeAsync(VoiceAudioClip clip, CancellationToken cancellationToken = default)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            return Task.Run(() => Transcribe(clip), cancellationToken);
        }

        /// <summary>Recognises one utterance of 16 kHz mono samples; empty text means nothing was said.</summary>
        public string Transcribe(VoiceAudioClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));

            var samples = ToFloats(clip.Samples);
            if (samples.Length == 0) return "";

            lock (_gate)
            {
                var recognizer = _recognizer ??= Create();
                var stream = recognizer.CreateOfflineStream();
                stream.AddSamples(samples);
                var results = recognizer.GetResults(new List<OfflineStream> { stream });
                return results.Count == 0 ? "" : CleanTranscript(results[0].Text);
            }
        }

        /// <summary>
        /// The SenseVoice front end prefixes every sentence with its own control tokens - language, emotion,
        /// audio event and ITN mode, as in "&lt;|yue|&gt; &lt;|NEUTRAL|&gt; &lt;|Speech|&gt; &lt;|woitn|&gt; 大家好". They are not
        /// words anybody said: the command router would match nothing and then read them back as part of the
        /// "not understood" answer, so only the plain sentence is kept.
        /// </summary>
        public static string CleanTranscript(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            return TranscriptSpace.Replace(TranscriptTag.Replace(raw, " "), " ").Trim();
        }

        private OfflineRecognizer Create()
        {
            var model = Path.Combine(_directory, VoiceModelStore.ModelFileName);
            var config = Path.Combine(_directory, VoiceModelStore.ConfigFileName);
            var mvn = Path.Combine(_directory, VoiceModelStore.MvnFileName);
            var tokens = Path.Combine(_directory, VoiceModelStore.TokensFileName);

            foreach (var path in new[] { model, config, mvn, tokens })
            {
                if (!File.Exists(path)) throw new FileNotFoundException("speech model file is missing", path);
            }

            return new OfflineRecognizer(model, config, mvn, tokens);
        }

        /// <summary>16-bit PCM to the normalised floats the front end expects.</summary>
        public static float[] ToFloats(short[] samples)
        {
            if (samples == null || samples.Length == 0) return Array.Empty<float>();

            var floats = new float[samples.Length];
            for (var i = 0; i < samples.Length; i++) floats[i] = samples[i] / 32768f;
            return floats;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _recognizer?.Dispose();
                _recognizer = null;
            }
        }
    }
}
