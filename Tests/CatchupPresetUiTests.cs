using System.Windows.Controls;
using LibmpvIptvClient.Controls;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Contract test for the "common formats" combo box in the replay / timeshift drawers: it has to be
    /// filled from the catalogue, fill the url format box when a preset is picked, and never overwrite a
    /// template the user wrote by hand. The real XAML is loaded here, so a broken name or handler shows up
    /// as a test failure instead of an empty settings page.
    /// </summary>
    [TestClass]
    public class CatchupPresetUiTests
    {
        [TestMethod]
        public void PlaybackDrawer_PickingAPreset_FillsTheUrlFormatBox()
        {
            WpfTestHost.EnsureAppResources();
            WpfTestHost.Invoke(() =>
            {
                var drawer = new PlaybackDrawer();
                var config = new ReplayConfig { UrlFormat = "" };
                drawer.Load(config);

                var box = (ComboBox)drawer.FindName("CbPreset");
                var format = (TextBox)drawer.FindName("TbUrlFormat");
                Assert.IsNotNull(box, "the preset combo box must exist in the drawer XAML");
                Assert.IsNotNull(format, "the url format box must exist in the drawer XAML");
                Assert.AreEqual(CatchupFormatPresets.All.Count, box.Items.Count);

                box.SelectedIndex = 0;

                Assert.AreEqual(CatchupFormatPresets.All[0].Template, format.Text,
                    "picking a preset must fill the url format box");
                drawer.Save(config);
                Assert.AreEqual(CatchupFormatPresets.All[0].Template, config.UrlFormat,
                    "the picked preset must be what Save() persists");
            });
        }

        [TestMethod]
        public void TimeshiftDrawer_SavedHandWrittenTemplate_IsNotOverwritten()
        {
            WpfTestHost.EnsureAppResources();
            WpfTestHost.Invoke(() =>
            {
                const string handwritten = "?playseek=${(b)yyyyMMddHHmmss}";
                var drawer = new TimeshiftDrawer();
                var config = new TimeshiftConfig { UrlFormat = handwritten };
                drawer.Load(config);

                var box = (ComboBox)drawer.FindName("CbPreset");
                var format = (TextBox)drawer.FindName("TbUrlFormat");

                Assert.AreEqual(handwritten, format.Text, "Load must keep the saved template");
                Assert.AreEqual(-1, box.SelectedIndex, "a hand written template matches no preset");
            });
        }

        [TestMethod]
        public void TimeshiftDrawer_SavedPreset_IsHighlighted()
        {
            WpfTestHost.EnsureAppResources();
            WpfTestHost.Invoke(() =>
            {
                var preset = CatchupFormatPresets.All[CatchupFormatPresets.All.Count - 1];
                var drawer = new TimeshiftDrawer();
                var config = new TimeshiftConfig { UrlFormat = preset.Template };
                drawer.Load(config);

                var box = (ComboBox)drawer.FindName("CbPreset");
                var format = (TextBox)drawer.FindName("TbUrlFormat");

                Assert.AreEqual(preset.Template, format.Text);
                Assert.AreEqual(CatchupFormatPresets.All.Count - 1, box.SelectedIndex,
                    "a saved template that equals a preset must be highlighted");
            });
        }
    }
}
