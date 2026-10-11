using System;
using System.Runtime.CompilerServices;
using System.Windows;

namespace LibmpvIptvClient.Helpers
{
    /// <summary>
    /// Every surface that floats over the video in fullscreen - the detached EPG / channel list panels and
    /// the top / bottom bars - is painted with the dark palette even while the light theme is active, so
    /// fullscreen looks the same in both themes. The dark dictionary is merged into the surface's own
    /// resources, which take precedence over <c>Application.Resources</c>; the surface's controls resolve
    /// their brushes through <c>DynamicResource</c>, so swapping the dictionary repaints them.
    /// </summary>
    public static class FullscreenOverlayTheme
    {
        /// <summary>Theme dictionary that defines the fullscreen palette.</summary>
        private const string DarkDictionaryPath = "Resources/Colors.Dark.xaml";

        /// <summary>
        /// Pack URI of that dictionary. It is addressed through the owning assembly instead of a relative
        /// URI so it also resolves when the dictionary is merged outside the application, e.g. in a test.
        /// </summary>
        private static readonly Uri DarkDictionaryUri = new(
            $"pack://application:,,,/{typeof(FullscreenOverlayTheme).Assembly.GetName().Name};component/{DarkDictionaryPath}",
            UriKind.Absolute);

        /// <summary>
        /// The dictionary each window got from <see cref="UseDarkPalette"/>, so turning the palette off
        /// removes exactly that one and the window falls back to the active theme.
        /// </summary>
        private static readonly ConditionalWeakTable<Window, ResourceDictionary> Merged = new();

        /// <summary>
        /// Merges the dark palette into <paramref name="window"/> (idempotent) or removes it again. Passing
        /// null is a no-op, so callers do not have to care whether the bar exists yet.
        /// </summary>
        public static void UseDarkPalette(Window? window, bool dark)
        {
            if (window == null) return;

            try
            {
                if (dark)
                {
                    if (Merged.TryGetValue(window, out _)) return;

                    var dictionary = new ResourceDictionary { Source = DarkDictionaryUri };
                    window.Resources.MergedDictionaries.Add(dictionary);
                    Merged.Add(window, dictionary);
                }
                else if (Merged.TryGetValue(window, out var dictionary))
                {
                    window.Resources.MergedDictionaries.Remove(dictionary);
                    Merged.Remove(window);
                }
            }
            catch
            {
                // A bar that cannot be re-themed must not take the fullscreen switch down.
            }
        }

        /// <summary>
        /// Palette of the detached fullscreen panels: dark surfaces plus the shared translucent scrim, with
        /// the panel card background left transparent so the scrim is what shows through.
        /// </summary>
        public static void ApplyFullscreenPanelPalette(Window window)
        {
            UseDarkPalette(window, true);
            window.Resources["CardBackgroundBrush"] = System.Windows.Media.Brushes.Transparent;
            window.SetResourceReference(Window.BackgroundProperty, "TopOverlayScrimBrush");
        }
    }
}
