using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class PanelStatePersistenceTests
    {
        [TestMethod]
        public void Defaults_OpenChannelListAndClosedEpg()
        {
            var settings = new PlaybackSettings();

            Assert.IsTrue(settings.ShowChannelList, "the channel list is open by default");
            Assert.IsFalse(settings.ShowEpgPanel, "the EPG panel is closed by default");
            Assert.IsTrue(settings.RememberWindowGeometry);
        }

        // The panel state and the window geometry must survive a save/load cycle, otherwise the
        // next launch cannot reopen the same layout.
        [TestMethod]
        public void PanelStateAndGeometry_SurviveSerialization()
        {
            var settings = new PlaybackSettings
            {
                ShowChannelList = false,
                ShowEpgPanel = true,
                WindowWidth = 1440,
                WindowHeight = 810,
                WindowLeft = 120,
                WindowTop = 60,
                WindowMaximized = true
            };

            var json = JsonSerializer.Serialize(settings);
            var restored = JsonSerializer.Deserialize<PlaybackSettings>(json);

            Assert.IsNotNull(restored);
            Assert.IsFalse(restored!.ShowChannelList);
            Assert.IsTrue(restored.ShowEpgPanel);
            Assert.AreEqual(1440d, restored.WindowWidth);
            Assert.AreEqual(810d, restored.WindowHeight);
            Assert.AreEqual(120d, restored.WindowLeft);
            Assert.AreEqual(60d, restored.WindowTop);
            Assert.IsTrue(restored.WindowMaximized);
        }

        // Settings written by an older version have no panel fields; the defaults must be used.
        [TestMethod]
        public void LegacySettings_WithoutPanelFields_KeepDefaults()
        {
            const string legacy = "{\"Language\":\"zh-CN\",\"ConfirmOnClose\":true}";

            var restored = JsonSerializer.Deserialize<PlaybackSettings>(legacy);

            Assert.IsNotNull(restored);
            Assert.IsTrue(restored!.ShowChannelList);
            Assert.IsFalse(restored.ShowEpgPanel);
        }
    }
}
