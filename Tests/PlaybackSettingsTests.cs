using Microsoft.VisualStudio.TestTools.UnitTesting;
using LibmpvIptvClient;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class PlaybackSettingsTests
    {
        [TestMethod]
        public void DefaultValues_ShouldBeCorrect()
        {
            var settings = new PlaybackSettings();
            
            // EPG
            Assert.IsTrue(settings.Epg.Enabled);
            Assert.AreEqual("", settings.Epg.Url);
            Assert.AreEqual(24, settings.Epg.RefreshIntervalHours);
            
            // Logo
            Assert.IsTrue(settings.Logo.Enabled);
            Assert.AreEqual("", settings.Logo.Url);
            
            // Replay
            Assert.IsTrue(settings.Replay.Enabled);
            Assert.AreEqual("", settings.Replay.UrlFormat);
            Assert.AreEqual(72, settings.Replay.DurationHours);
            
            // Timeshift
            Assert.IsTrue(settings.Timeshift.Enabled);
            Assert.AreEqual("", settings.Timeshift.UrlFormat);
            Assert.AreEqual(6, settings.Timeshift.DurationHours);
        }

        [TestMethod]
        public void CompatibilityProperties_ShouldMapToNewConfig()
        {
            var settings = new PlaybackSettings();
            
            settings.CustomEpgUrl = "http://epg.com";
            Assert.AreEqual("http://epg.com", settings.Epg.Url);
            
            settings.Epg.Url = "http://new.com";
            Assert.AreEqual("http://new.com", settings.CustomEpgUrl);
            
            settings.CustomLogoUrl = "http://logo.com";
            Assert.AreEqual("http://logo.com", settings.Logo.Url);

            settings.TimeshiftHours = 12;
            Assert.AreEqual(12, settings.Timeshift.DurationHours);
        }

        [TestMethod]
        public void Deinterlace_DefaultValues_ShouldBeAuto()
        {
            // 反交错默认应为 auto（智能检测，零副作用）
            var settings = new PlaybackSettings();
            Assert.AreEqual("auto", settings.Deinterlace);
            Assert.AreEqual("auto", settings.DeinterlaceFieldParity);
            Assert.AreEqual("yadif", settings.DeinterlaceAlgorithm);
        }

        [TestMethod]
        public void Deinterlace_CanBeChanged()
        {
            // 验证三个字段均可独立修改
            var settings = new PlaybackSettings();
            settings.Deinterlace = "yes";
            settings.DeinterlaceFieldParity = "tff";
            settings.DeinterlaceAlgorithm = "bwdif";
            Assert.AreEqual("yes", settings.Deinterlace);
            Assert.AreEqual("tff", settings.DeinterlaceFieldParity);
            Assert.AreEqual("bwdif", settings.DeinterlaceAlgorithm);

            // 关闭态
            settings.Deinterlace = "no";
            Assert.AreEqual("no", settings.Deinterlace);
        }

        [TestMethod]
        public void Deinterlace_SurvivesSerialization()
        {
            // 验证字段能正确序列化到 JSON 并回读
            var settings = new PlaybackSettings
            {
                Deinterlace = "yes",
                DeinterlaceFieldParity = "bff",
                DeinterlaceAlgorithm = "bwdif"
            };
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            Assert.IsTrue(json.Contains("\"Deinterlace\":\"yes\""));
            Assert.IsTrue(json.Contains("\"DeinterlaceFieldParity\":\"bff\""));
            Assert.IsTrue(json.Contains("\"DeinterlaceAlgorithm\":\"bwdif\""));

            var round = System.Text.Json.JsonSerializer.Deserialize<PlaybackSettings>(json);
            Assert.IsNotNull(round);
            Assert.AreEqual("yes", round!.Deinterlace);
            Assert.AreEqual("bff", round.DeinterlaceFieldParity);
            Assert.AreEqual("bwdif", round.DeinterlaceAlgorithm);
        }
    }
}
