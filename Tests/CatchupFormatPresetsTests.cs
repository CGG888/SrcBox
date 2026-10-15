using System.Collections.Generic;
using System.Linq;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Guards the catchup format catalogue offered by the settings page. Every entry has to survive the
    /// placeholder expansion untouched by stray braces - a template the provider cannot read is worse than
    /// no template, because the failure looks like "replay does nothing" (issue #6, 重庆联通 / 广西联通).
    /// </summary>
    [TestClass]
    public class CatchupFormatPresetsTests
    {
        static readonly System.DateTime Start = new System.DateTime(2026, 3, 19, 20, 53, 0, System.DateTimeKind.Local);
        static readonly System.DateTime End = new System.DateTime(2026, 3, 19, 21, 23, 0, System.DateTimeKind.Local);

        [TestMethod]
        public void EveryPreset_IsAParameterOnlySegment()
        {
            Assert.IsTrue(CatchupFormatPresets.All.Count >= 6, "the catalogue should cover the common shapes");

            foreach (var preset in CatchupFormatPresets.All)
            {
                Assert.IsTrue(preset.Template.StartsWith("?") || preset.Template.StartsWith("&"),
                    $"{preset.Id} must be appended to the live url: {preset.Template}");
                Assert.IsFalse(preset.Template.Contains("://"), $"{preset.Id} must not carry a host");
            }
        }

        [TestMethod]
        public void EveryPreset_IdIsUniqueAndNamedForItsLabelKey()
        {
            var ids = CatchupFormatPresets.All.Select(p => p.Id).ToList();

            CollectionAssert.AllItemsAreUnique(ids);
            foreach (var preset in CatchupFormatPresets.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(preset.Id));
                Assert.AreEqual("UI_Catchup_Preset_" + preset.Id, preset.LabelKey);
            }
        }

        [TestMethod]
        public void EveryPreset_FullyExpands()
        {
            foreach (var preset in CatchupFormatPresets.All)
            {
                var url = UrlPlaceholderExpander.Expand(preset.Template, Start, End);

                Assert.IsFalse(url.Contains('{'), $"{preset.Id} left a placeholder behind: {url}");
                Assert.IsFalse(url.Contains('}'), $"{preset.Id} left a placeholder behind: {url}");
                Assert.IsFalse(url.Contains('$'), $"{preset.Id} left a placeholder behind: {url}");
            }
        }

        [TestMethod]
        public void KodiUtcPreset_UsesUnixSeconds()
        {
            var preset = CatchupFormatPresets.All.First(p => p.Id == "utc_only");
            var url = UrlPlaceholderExpander.Expand(preset.Template, Start, End);

            var value = url.Substring(url.IndexOf('=') + 1);
            Assert.AreEqual(10, value.Length, url);
            Assert.AreEqual(new System.DateTimeOffset(Start.ToUniversalTime()).ToUnixTimeSeconds().ToString(), value);
        }

        // The two shapes reported in issue #6: a ZTE style RTSP portal answered to "{utc}", a PLTV portal to
        // "playseek=<start>-<end>".
        [TestMethod]
        public void ReportedOperatorShapes_AreCovered()
        {
            var templates = new HashSet<string>(CatchupFormatPresets.All.Select(p => p.Template));

            Assert.IsTrue(templates.Contains("?time={utc}"), "the Kodi single-timestamp shape must be offered");
            Assert.IsTrue(templates.Contains("?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}"),
                "the ZTE/PLTV playseek shape must be offered");
        }
    }
}
