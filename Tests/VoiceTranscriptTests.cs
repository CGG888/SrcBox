using LibmpvIptvClient.Services.WebRemote.Voice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The SenseVoice front end decorates every sentence with control tokens. They must never reach the
    /// command router, which would otherwise match nothing and read the tokens back to the user.
    /// </summary>
    [TestClass]
    public class VoiceTranscriptTests
    {
        [TestMethod]
        public void CleanTranscript_StripsTheSenseVoiceControlTokens()
        {
            var clean = OfflineSpeechRecognizer.CleanTranscript(
                "<|yue|> <|NEUTRAL|> <|Speech|> <|woitn|> 欢迎大家来体验语音识别模型");

            Assert.AreEqual("欢迎大家来体验语音识别模型", clean);
        }

        [TestMethod]
        public void CleanTranscript_KeepsASentenceThatHasNoTokens()
        {
            Assert.AreEqual("换到一台", OfflineSpeechRecognizer.CleanTranscript("换到一台"));
        }

        [TestMethod]
        public void CleanTranscript_CollapsesTheSpaceTheRemovedTokensLeaveBehind()
        {
            Assert.AreEqual("现在在播什么", OfflineSpeechRecognizer.CleanTranscript("  <|zh|>   <|NEUTRAL|>  现在在播什么  "));
        }

        [TestMethod]
        public void CleanTranscript_ReturnsEmptyWhenNothingButTokensWasHeard()
        {
            Assert.AreEqual("", OfflineSpeechRecognizer.CleanTranscript("<|zh|> <|NEUTRAL|> <|Speech|> <|woitn|>"));
        }

        [TestMethod]
        public void CleanTranscript_TreatsBlankInputAsSilence()
        {
            Assert.AreEqual("", OfflineSpeechRecognizer.CleanTranscript(null));
            Assert.AreEqual("", OfflineSpeechRecognizer.CleanTranscript("   "));
        }

        [TestMethod]
        public void ToFloats_ScalesInt16ToTheRangeTheFrontEndWants()
        {
            var floats = OfflineSpeechRecognizer.ToFloats(new short[] { 0, 32767, -32768 });

            Assert.AreEqual(3, floats.Length);
            Assert.AreEqual(0f, floats[0], 0.0001f);
            Assert.AreEqual(1f, floats[1], 0.001f);
            Assert.AreEqual(-1f, floats[2], 0.0001f);
            Assert.AreEqual(0, OfflineSpeechRecognizer.ToFloats(System.Array.Empty<short>()).Length);
        }

        [TestMethod]
        public void Forget_DropsTheWarmEngineSoTheNextCallStartsOver()
        {
            var first = OfflineSpeechRecognizer.For(null);
            Assert.AreSame(first, OfflineSpeechRecognizer.For(null));

            OfflineSpeechRecognizer.Forget();

            Assert.AreNotSame(first, OfflineSpeechRecognizer.For(null));
        }

        [TestMethod]
        public void VerifyRunnable_FailsLoudlyWhenTheModelIsNotOnDisk()
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "srcbox-voice-check-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);

            try
            {
                var error = Assert.ThrowsException<System.IO.FileNotFoundException>(
                    () => OfflineSpeechRecognizer.VerifyRunnable(directory));

                StringAssert.Contains(error.Message, "speech model file is missing");
            }
            finally
            {
                OfflineSpeechRecognizer.Forget();
                try { System.IO.Directory.Delete(directory, true); } catch { }
            }
        }
    }
}
