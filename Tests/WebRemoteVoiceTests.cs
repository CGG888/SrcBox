using System.Collections.Generic;
using LibmpvIptvClient.Architecture.Application.Settings;
using LibmpvIptvClient.Services.WebRemote;
using LibmpvIptvClient.Services.WebRemote.Voice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Covers the voice plumbing around the router: the page hooks the phone drives, the settings that
    /// cross to the client, the GET /api/voice query parsing and the LAN-only address filter.
    /// </summary>
    [TestClass]
    public class WebRemoteVoiceTests
    {
        [TestMethod]
        public void RemoteHtml_ExposesTheVoiceButtonOverlayAndCommandChannel()
        {
            var html = WebRemoteServer.GetRemoteHtml("zh-CN");

            StringAssert.Contains(html, "id=\"voiceFab\"");
            StringAssert.Contains(html, "id=\"voiceOverlay\"");
            StringAssert.Contains(html, "id=\"voiceMicBtn\"");
            StringAssert.Contains(html, "id=\"voiceOptions\"");
            StringAssert.Contains(html, "onclick=\"toggleVoice()\"");
            // The page must reach the router over the existing socket, not a second protocol.
            StringAssert.Contains(html, "send('voiceCommand'");
        }

        [TestMethod]
        public void RemoteHtml_BakesInTheVoiceSettingsFromConfig()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US", 0, null, null, "{\"enabled\":true,\"speak\":false}");

            StringAssert.Contains(html, "const VOICE = {\"enabled\":true,\"speak\":false};");
        }

        [TestMethod]
        public void BuildVoiceJson_ProjectsOnlyWhatTheClientNeeds()
        {
            var json = WebRemoteServer.BuildVoiceJson(new VoiceConfig
            {
                Enabled = false,
                SpeakBack = false,
                HandsFree = true,
                Lang = "en-US",
                VolumeStep = 5,
                Token = "secret-token",
                CustomRules = new List<VoiceRule> { new VoiceRule { Action = "channel", Target = "CCTV-1" } }
            });

            StringAssert.Contains(json, "\"enabled\":false");
            StringAssert.Contains(json, "\"speak\":false");
            StringAssert.Contains(json, "\"handsFree\":true");
            StringAssert.Contains(json, "\"lang\":\"en-US\"");
            // The token and the custom rules stay on the server: the page never sees them.
            Assert.IsFalse(json.Contains("secret-token"), json);
            Assert.IsFalse(json.Contains("CCTV-1"), json);
        }

        [TestMethod]
        public void QueryValues_DecodesTheVoiceRequestLine()
        {
            var values = WebRemoteServer.QueryValues("GET /api/voice?text=%E6%8D%A2%E5%88%B0+1%E5%8F%B0&token=abc123&dry=1 HTTP/1.1");

            Assert.AreEqual("换到 1台", values["text"]);
            Assert.AreEqual("abc123", values["token"]);
            Assert.AreEqual("1", values["dry"]);
        }

        [TestMethod]
        public void QueryValues_WithoutAQueryString_IsEmpty()
        {
            Assert.AreEqual(0, WebRemoteServer.QueryValues("GET /api/voice HTTP/1.1").Count);
        }

        [DataTestMethod]
        [DataRow("192.168.1.20", true)]
        [DataRow("10.1.2.3", true)]
        [DataRow("172.20.5.6", true)]
        [DataRow("172.32.5.6", false)]
        [DataRow("127.0.0.1", true)]
        [DataRow("169.254.10.1", true)]
        [DataRow("100.64.0.7", true)]
        [DataRow("8.8.8.8", false)]
        [DataRow("2001:4860:4860::8888", false)]
        [DataRow("fe80::1", true)]
        [DataRow("not-an-address", true)]
        public void IsPrivateAddress_OnlyTheLanReachesTheVoiceEndpoint(string address, bool allowed)
        {
            Assert.AreEqual(allowed, WebRemoteServer.IsPrivateAddress(address), address);
        }

        [TestMethod]
        public void RemoteHtml_KeepsThePresetsTappableAndThePanelRecoverable()
        {
            var html = WebRemoteServer.GetRemoteHtml("zh-CN");

            // The advertised presets have to be tappable commands, not only a sentence: on a phone whose
            // browser cannot reach an online speech service the recogniser never produces any text.
            StringAssert.Contains(html, "function renderVoicePresets()");
            StringAssert.Contains(html, "renderVoicePresets();");
            // A command that never reached the player must not leave the panel on "working" forever.
            StringAssert.Contains(html, "voiceReplyTimeout");
            StringAssert.Contains(html, "T.voice_no_reply");
            StringAssert.Contains(html, "if (!send('voiceCommand'");
        }

        [TestMethod]
        public void RemoteHtml_NeverRefusesTheMicrophoneOnAPlainHttpOrigin()
        {
            var html = WebRemoteServer.GetRemoteHtml("zh-CN");

            // A phone on a plain http LAN address has to be allowed to try: the browser, not a page-side
            // flag, decides whether recognition can run, and the only way out (the https switch) has to be
            // offered right in the panel instead of leaving the microphone button dead.
            StringAssert.Contains(html, "function voiceSupported() { return !!VoiceSR; }");
            StringAssert.Contains(html, "id=\"voiceHint\"");
            StringAssert.Contains(html, "id=\"voiceHttpsBtn\"");
            StringAssert.Contains(html, "onclick=\"openSecureRemote()\"");
            StringAssert.Contains(html, "function voiceRefreshHint()");
            StringAssert.Contains(html, "voiceRefreshHint();");
            // And the reason the microphone stayed shut has to be readable from the desktop log.
            StringAssert.Contains(html, "send('voiceDiag'");
            StringAssert.Contains(html, "voiceDiag('started', '')");
        }

        [TestMethod]
        public void RemoteHtml_RecoversWhenTheSpeechServiceNeverAnswers()
        {
            var html = WebRemoteServer.GetRemoteHtml("zh-CN");

            // A session that never ended - the phone's speech service stops answering after 'started' - leaves
            // the recogniser in the started state, so every later tap throws InvalidStateError: the page has
            // to notice that, drop the dead object and start a fresh one.
            StringAssert.Contains(html, "name === 'InvalidStateError'");
            StringAssert.Contains(html, "function voiceDetachRecognition");
            StringAssert.Contains(html, "voiceRec = null;");
            // A service that starts but never answers must end the session and say so instead of leaving the
            // panel on 'listening' forever, and the microphone itself has to be provable on its own (that is
            // what raises the phone's microphone indicator).
            StringAssert.Contains(html, "function armVoiceStall");
            StringAssert.Contains(html, "voiceDiag('stall'");
            StringAssert.Contains(html, "function voiceProbeMicrophone");
            StringAssert.Contains(html, "voiceDiag('mic'");
            // A transcript has to reach the desktop log, and the fallback advice has to be reachable on a
            // secure origin too (the phone case, where the https hint is hidden by design).
            StringAssert.Contains(html, "voiceDiag('result'");
            StringAssert.Contains(html, "function voiceShowFallbackHint");
            StringAssert.Contains(html, "function voiceShowHintForFailure");
            // The desktop log has to name the browser that is asking, because the speech service behind
            // SpeechRecognition belongs to the browser vendor, not to this application.
            StringAssert.Contains(html, "navigator.userAgentData");
            StringAssert.Contains(html, "ua: (navigator.userAgent");
        }

        [DataTestMethod]
        [DataRow("zh-CN")]
        [DataRow("en-US")]
        [DataRow("zh-TW")]
        [DataRow("ru-RU")]
        public void WebRemoteStrings_CarryTheVoiceKeysInEveryLanguage(string lang)
        {
            var strings = WebRemoteStrings.Get(lang);

            foreach (var key in new[] { "voice_title", "voice_listening", "voice_unsupported", "voice_not_understood", "voice_examples", "voice_no_reply" })
                Assert.IsTrue(strings.ContainsKey(key) && !string.IsNullOrWhiteSpace(strings[key]), $"{lang}: {key}");
        }
    }
}
