using System.Collections.Generic;
using System.Linq;
using LibmpvIptvClient;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Issue #38 follow-up: the settings page dropped every additional EPG source because the form copy
    /// and the apply step only carried <c>Url</c>. Both now go through <see cref="EpgConfig.CopyFrom"/>.
    /// </summary>
    [TestClass]
    public class EpgConfigCopyTests
    {
        [TestMethod]
        public void CopyFrom_CarriesTheAdditionalSources()
        {
            var live = new EpgConfig
            {
                Enabled = true,
                Url = "http://a/epg.xml",
                Urls = new List<string> { "http://b/epg.xml", "http://c/epg.xml" },
                RefreshIntervalHours = 6,
                EnableSmartMatch = false,
                StrictMatchByPlaybackTime = false
            };

            var form = new EpgConfig();
            form.CopyFrom(live);

            CollectionAssert.AreEqual(new[] { "http://b/epg.xml", "http://c/epg.xml" }, form.Urls.ToList());
            CollectionAssert.AreEqual(new[] { "http://a/epg.xml", "http://b/epg.xml", "http://c/epg.xml" }, form.GetEffectiveUrls());
            Assert.AreEqual(6, form.RefreshIntervalHours);
            Assert.IsFalse(form.EnableSmartMatch);
            Assert.IsFalse(form.StrictMatchByPlaybackTime);
        }

        [TestMethod]
        public void CopyFrom_ReplacesTheTargetListInsteadOfAppending()
        {
            var target = new EpgConfig { Url = "http://old", Urls = new List<string> { "http://old2" } };
            target.CopyFrom(new EpgConfig { Url = "http://new", Urls = new List<string> { "http://new2" } });

            CollectionAssert.AreEqual(new[] { "http://new2" }, target.Urls.ToList());
            Assert.AreEqual("http://new", target.Url);
        }

        [TestMethod]
        public void CopyFrom_HandlesNullSourceAndNullList()
        {
            var target = new EpgConfig { Url = "http://keep", Urls = new List<string> { "http://keep2" } };

            target.CopyFrom(null);
            Assert.AreEqual("http://keep", target.Url, "空来源不应清空已有配置");

            target.CopyFrom(new EpgConfig { Url = "http://x", Urls = null! });
            Assert.AreEqual(0, target.Urls.Count);
        }

        [TestMethod]
        public void BuildTempConfig_KeepsEverySourceForTheForm()
        {
            var source = new EpgConfig
            {
                Url = "http://a/epg.xml",
                Urls = new List<string> { "http://b/epg.xml" }
            };

            var config = new SettingsEpgDrawerViewModel().BuildTempConfig(source);

            CollectionAssert.AreEqual(new[] { "http://a/epg.xml", "http://b/epg.xml" }, config.GetEffectiveUrls());
        }

        [TestMethod]
        public void BuildTempConfig_NullSourceReturnsDefaults()
        {
            var config = new SettingsEpgDrawerViewModel().BuildTempConfig(null);

            Assert.AreEqual(0, config.GetEffectiveUrls().Count);
        }
    }
}
