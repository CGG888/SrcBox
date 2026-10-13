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

        [DataTestMethod]
        [DataRow("zh-CN")]
        [DataRow("en-US")]
        [DataRow("zh-TW")]
        [DataRow("ru-RU")]
        public void WebRemoteStrings_CarryTheVoiceKeysInEveryLanguage(string lang)
        {
            var strings = WebRemoteStrings.Get(lang);

            foreach (var key in new[] { "voice_title", "voice_listening", "voice_unsupported", "voice_not_understood", "voice_examples" })
                Assert.IsTrue(strings.ContainsKey(key) && !string.IsNullOrWhiteSpace(strings[key]), $"{lang}: {key}");
        }
    }
}
