using System;
using System.Collections.Generic;
using System.Linq;
using LibmpvIptvClient.Services.WebRemote;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Web Remote access link / QR code helpers and the four language dictionaries behind the
    /// remote page (standard remote pad included).
    /// </summary>
    [TestClass]
    public class WebRemoteAddressTests
    {
        [TestMethod]
        public void BuildUrl_UsesHttpAndPort()
        {
            Assert.AreEqual("http://192.168.1.20:8899/", WebRemoteAddressHelper.BuildUrl("192.168.1.20", 8899));
        }

        [TestMethod]
        public void BuildUrl_WithoutHost_IsEmpty()
        {
            Assert.AreEqual("", WebRemoteAddressHelper.BuildUrl(null, 8899));
            Assert.AreEqual("", WebRemoteAddressHelper.BuildUrl("", 8899));
            Assert.AreEqual("", WebRemoteAddressHelper.BuildUrl("   ", 8899));
        }

        [TestMethod]
        public void BuildUrl_WithInvalidPort_IsEmpty()
        {
            Assert.AreEqual("", WebRemoteAddressHelper.BuildUrl("192.168.1.20", 0));
            Assert.AreEqual("", WebRemoteAddressHelper.BuildUrl("192.168.1.20", -1));
        }

        [TestMethod]
        public void IsUsable_RejectsLoopbackLinkLocalAndAny()
        {
            Assert.IsTrue(WebRemoteAddressHelper.IsUsable("192.168.1.20"));
            Assert.IsTrue(WebRemoteAddressHelper.IsUsable("10.0.0.7"));
            Assert.IsFalse(WebRemoteAddressHelper.IsUsable("127.0.0.1"));
            Assert.IsFalse(WebRemoteAddressHelper.IsUsable("169.254.10.3"));
            Assert.IsFalse(WebRemoteAddressHelper.IsUsable("0.0.0.0"));
            Assert.IsFalse(WebRemoteAddressHelper.IsUsable(""));
            Assert.IsFalse(WebRemoteAddressHelper.IsUsable(null));
            Assert.IsFalse(WebRemoteAddressHelper.IsUsable("not-an-ip"));
            Assert.IsFalse(WebRemoteAddressHelper.IsUsable("::1"));
        }

        [TestMethod]
        public void IsPrivateRange_RecognisesTheThreeRfc1918Blocks()
        {
            Assert.IsTrue(WebRemoteAddressHelper.IsPrivateRange("10.1.2.3"));
            Assert.IsTrue(WebRemoteAddressHelper.IsPrivateRange("192.168.0.1"));
            Assert.IsTrue(WebRemoteAddressHelper.IsPrivateRange("172.16.0.1"));
            Assert.IsTrue(WebRemoteAddressHelper.IsPrivateRange("172.31.255.254"));
            Assert.IsFalse(WebRemoteAddressHelper.IsPrivateRange("172.32.0.1"));
            Assert.IsFalse(WebRemoteAddressHelper.IsPrivateRange("172.15.0.1"));
            Assert.IsFalse(WebRemoteAddressHelper.IsPrivateRange("8.8.8.8"));
        }

        [TestMethod]
        public void GetLocalIPv4Addresses_OnlyReturnsUsableAddresses()
        {
            var addresses = WebRemoteAddressHelper.GetLocalIPv4Addresses();

            Assert.IsNotNull(addresses);
            Assert.AreEqual(addresses.Distinct().Count(), addresses.Count);
            foreach (var address in addresses)
            {
                Assert.IsTrue(WebRemoteAddressHelper.IsUsable(address), address);
            }
        }

        [TestMethod]
        public void QrCode_WithoutText_IsNull()
        {
            Assert.IsNull(WebRemoteQrCode.Create(null));
            Assert.IsNull(WebRemoteQrCode.Create(""));
        }

        [TestMethod]
        public void QrCode_ForAccessUrl_RendersAFrozenBitmap()
        {
            var image = WebRemoteQrCode.Create("http://192.168.1.20:8899/");

            Assert.IsNotNull(image);
            Assert.IsTrue(image!.PixelWidth > 0);
            Assert.IsTrue(image.IsFrozen);
        }

        [TestMethod]
        public void WebRemoteStrings_ContainTheStandardRemoteLabels()
        {
            foreach (var lang in new[] { "zh-CN", "en-US", "zh-TW", "ru-RU" })
            {
                var strings = WebRemoteStrings.Get(lang);
                foreach (var key in new[] { "view_standard", "view_full", "num_hint", "num_placeholder", "channel_not_found" })
                {
                    Assert.IsTrue(strings.ContainsKey(key), $"{lang} is missing {key}");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(strings[key]), $"{lang}.{key} is empty");
                }
            }
        }

        [TestMethod]
        public void WebRemoteStrings_ExposeTheSameKeysInAllFourLanguages()
        {
            var reference = WebRemoteStrings.Get("zh-CN").Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.IsTrue(reference.Count > 0);

            foreach (var lang in new[] { "zh-CN", "en-US", "zh-TW", "ru-RU" })
            {
                var strings = WebRemoteStrings.Get(lang);
                var keys = strings.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                CollectionAssert.AreEqual(reference, keys, $"{lang} does not expose the same keys as zh-CN");

                foreach (var pair in strings)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Value), $"{lang}.{pair.Key} is empty");
                }
            }
        }

        [TestMethod]
        public void RemoteHtml_LeavesNoPlaceholderBehind()
        {
            var html = WebRemoteServer.GetRemoteHtml("zh-CN");

            var leftover = System.Text.RegularExpressions.Regex.Matches(html, "__[A-Z_]+__")
                .Select(m => m.Value)
                .Distinct()
                .ToList();

            CollectionAssert.AreEqual(new List<string>(), leftover);
        }

        [TestMethod]
        public void RemoteHtml_ContainsTheStandardRemotePad()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US");

            foreach (var marker in new[]
                     {
                         "remote-pad", "rp-dpad", "rp-numpad", "rp-num-btn", "rp-num-display", "rp-vol",
                         "id=\"viewToggle\"", "toggleRemoteView()", "numpadPress(1)", "ch-number"
                     })
            {
                Assert.IsTrue(html.Contains(marker), $"remote html is missing {marker}");
            }
        }

        [TestMethod]
        public void RemoteHtml_PadFooterExposesFullscreenAndCloseButtons()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US");

            var start = html.IndexOf("<div class=\"rp-footer\"", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "standard remote pad has no footer");

            var end = html.IndexOf("</div>", start, StringComparison.Ordinal);
            var footer = html.Substring(start, end - start);

            Assert.IsTrue(footer.Contains("fullscreen()"), "pad footer is missing the fullscreen button");
            Assert.IsTrue(footer.Contains("exitApp()"), "pad footer is missing the close button");
            Assert.AreEqual(4,
                System.Text.RegularExpressions.Regex.Matches(footer, "rp-text-btn").Count,
                "pad footer should expose exactly four buttons");
        }

        [TestMethod]
        public void RemoteHtml_KeepsNeumorphicDepthAndDarkThemeContrast()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US");

            // Depth tokens + the dark-theme overrides that keep raised controls visible and
            // keep the accent fills (regular/OK buttons) from being flattened by the surface tile.
            foreach (var marker in new[]
                     {
                         "--neu-grad:", "--neu-grad-well:", "--neu-hi:", "--neu-shadow-out-sm:",
                         "body.dark-theme .rp-ok", "body.dark-theme .btn.active",
                         "body.dark-theme .rp-num-btn.rp-num-ok", ".rp-footer .rp-text-btn.rp-exit"
                     })
            {
                Assert.IsTrue(html.Contains(marker), $"remote html is missing {marker}");
            }
        }
    }
}
