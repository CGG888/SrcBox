using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Guards the scrim shared by every surface that floats over the video (the fullscreen EPG and channel
    /// list windows, the top bar and the bottom overlay). It has to fade out to fully transparent in both
    /// themes; the light theme used to define it as a solid brush, which painted those surfaces opaque.
    /// </summary>
    [TestClass]
    public class OverlayScrimTests
    {
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        [DataTestMethod]
        [DataRow("Light")]
        [DataRow("Dark")]
        public void OverlayScrim_FadesOutToFullyTransparent(string theme)
        {
            var brush = FindBrush("TopOverlayScrimBrush", theme);

            Assert.IsNotNull(brush, $"Resources/Colors.{theme}.xaml must define TopOverlayScrimBrush");
            Assert.AreEqual("LinearGradientBrush", brush!.Name.LocalName,
                $"TopOverlayScrimBrush in the {theme} theme must be a gradient, not a solid colour");

            var stops = brush.Elements()
                .Where(e => e.Name.LocalName == "GradientStop")
                .Select(e => (string?) e.Attribute("Color"))
                .ToList();

            Assert.IsTrue(stops.Count >= 2, "the scrim needs at least a start and an end stop");
            Assert.IsTrue(AlphaOf(stops[0]) is > 0 and < 255,
                $"the top of the {theme} scrim must stay translucent (was {stops[0]})");
            Assert.AreEqual(0, AlphaOf(stops[^1]),
                $"the bottom of the {theme} scrim must be fully transparent (was {stops[^1]})");
        }

        [TestMethod]
        public void LightAndDarkThemes_DefineTheSameResourceKeys()
        {
            var light = Keys("Light");
            var dark = Keys("Dark");

            CollectionAssert.AreEqual(light, dark,
                "both theme dictionaries are swapped wholesale at runtime, so they must define the same keys. " +
                "Only in light: " + string.Join(", ", light.Except(dark)) +
                "; only in dark: " + string.Join(", ", dark.Except(light)));
        }

        private static XElement? FindBrush(string key, string theme) =>
            Dictionary(theme).Root?
                .Elements()
                .FirstOrDefault(e => (string?) e.Attribute(Xaml + "Key") == key);

        private static List<string> Keys(string theme) =>
            Dictionary(theme).Root?
                .Elements()
                .Select(e => (string?) e.Attribute(Xaml + "Key"))
                .Where(k => !string.IsNullOrEmpty(k))
                .Select(k => k!)
                .ToList() ?? new List<string>();

        private static XDocument Dictionary(string theme)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", $"Colors.{theme}.xaml");
            Assert.IsTrue(File.Exists(path), $"theme dictionary not found: {path}");
            return XDocument.Load(path);
        }

        /// <summary>Alpha channel of a theme colour written as #AARRGGBB (#RRGGBB means opaque).</summary>
        private static int AlphaOf(string? color)
        {
            var hex = (color ?? string.Empty).TrimStart('#');
            if (hex.Length != 8) return 255;
            return int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alpha)
                ? alpha
                : 255;
        }
    }
}
