using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Xml.Linq;
using LibmpvIptvClient.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Every surface that floats over the video in fullscreen - the top / bottom bars and the detached EPG
    /// and channel list panels - has to look exactly the same in the light and the dark theme: the dark
    /// dictionary is merged into the surface's own resource scope while fullscreen is active, and removed
    /// again for the windowed layout. These tests guard both halves: the merge itself, and the fact that
    /// the bars resolve their brushes through DynamicResource (a StaticResource would freeze the light
    /// brush at load time and the swap would silently do nothing).
    /// </summary>
    [TestClass]
    public class FullscreenOverlayPaletteTests
    {
        [TestMethod]
        public void UseDarkPalette_MergesTheDarkPaletteIntoTheWindowScope()
        {
            RunOnStaThread(() =>
            {
                var window = new System.Windows.Window();

                Assert.IsNull(window.TryFindResource("TextPrimaryBrush"),
                    "a bare window must not resolve theme brushes before the palette is applied");

                FullscreenOverlayTheme.UseDarkPalette(window, true);

                Assert.AreEqual("#FFEEEEEE", ColourOf(window, "TextPrimaryBrush"),
                    "fullscreen must paint the surface with the dark theme's primary text brush");
                Assert.AreEqual("#FF202020", ColourOf(window, "CardBackgroundBrush"),
                    "fullscreen must paint the surface with the dark theme's card background");

                var merged = window.Resources.MergedDictionaries.Count;
                FullscreenOverlayTheme.UseDarkPalette(window, true);
                Assert.AreEqual(merged, window.Resources.MergedDictionaries.Count,
                    "applying the fullscreen palette twice must not stack dictionaries");

                FullscreenOverlayTheme.UseDarkPalette(window, false);
                Assert.AreEqual(0, window.Resources.MergedDictionaries.Count,
                    "leaving fullscreen must drop the dark dictionary so the windowed layout keeps the active theme");
            });
        }

        [DataTestMethod]
        [DataRow("TopOverlay.xaml")]
        [DataRow("OverlayControls.xaml")]
        public void FloatingBars_ResolveTheirThemeBrushesDynamically(string file)
        {
            var usages = Xaml(file).Descendants()
                .Attributes()
                .Select(a => ((string?) a.Value ?? string.Empty).Trim())
                .ToList();

            foreach (var key in new[] { "TopOverlayScrimBrush", "BorderBrushCommon", "TextPrimaryBrush" })
            {
                Assert.AreEqual(0, usages.Count(v => v == $"{{StaticResource {key}}}"),
                    $"{file} must not freeze {key} with StaticResource: the fullscreen palette swaps the dictionary at runtime");
                Assert.IsTrue(usages.Count(v => v == $"{{DynamicResource {key}}}") > 0,
                    $"{file} must resolve {key} dynamically so the fullscreen palette repaints it");
            }
        }

        /// <summary>
        /// The EPG timeline that replaced the plain seek bar paints programme states (current / live / out)
        /// and its detail row from the active theme, so those keys have to stay dynamic for the same reason
        /// as the keys above: the fullscreen palette swaps the dictionary while the bar is already open.
        /// The timeline is not itself a floating bar (the host Border paints the scrim), so only the brushes
        /// it really uses are asserted here.
        /// </summary>
        [TestMethod]
        public void OverlayTimeline_ResolvesItsStateBrushesDynamically()
        {
            var usages = Xaml("TimeshiftTimeline.xaml").Descendants()
                .Attributes()
                .Select(a => ((string?) a.Value ?? string.Empty).Trim())
                .ToList();

            foreach (var key in new[] { "AccentBrush", "CardBackgroundBrush", "StatusTimeshiftBrush", "TextSecondaryBrush", "BorderBrushCommon", "TextPrimaryBrush" })
            {
                Assert.AreEqual(0, usages.Count(v => v == $"{{StaticResource {key}}}"),
                    $"the EPG timeline must not freeze {key} with StaticResource: the fullscreen palette swaps the dictionary at runtime");
                Assert.IsTrue(usages.Count(v => v == $"{{DynamicResource {key}}}") > 0,
                    $"the EPG timeline must resolve {key} dynamically so both themes paint the programme cards");
            }
        }

        private static XDocument Xaml(string file)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Controls", "Components", file);
            Assert.IsTrue(File.Exists(path), $"overlay window not found: {path}");
            return XDocument.Load(path);
        }

        private static string ColourOf(System.Windows.Window window, string key)
        {
            var brush = window.FindResource(key) as SolidColorBrush;
            Assert.IsNotNull(brush, $"{key} must resolve to a solid colour brush");
            return brush!.Color.ToString();
        }

        /// <summary>WPF windows require a single threaded apartment, which the test runner does not use.</summary>
        private static void RunOnStaThread(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new AssertFailedException(failure.Message, failure);
            }
        }
    }
}
