using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient
{
    /// <summary>
    /// Converts a Channel's source health status to a solid color brush for the Ellipse indicator.
    ///
    /// Color mapping:
    ///   Gray  (#8E8E93) - Single-source channel or not yet checked
    ///   Green (#35C759) - Primary source is healthy
    ///   Yellow(#FFD60A) - Primary source unhealthy, but has at least one reachable fallback
    ///   Red   (#FF3B30) - All sources are unreachable
    ///
    /// Note: Source raises Channel.PropertyChanged("Sources") when IsHealthy changes,
    /// which causes WPF to re-evaluate this binding on the Ellipse.
    /// </summary>
    public class SourceHealthToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush GreenBrush  = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x35, 0xC7, 0x59));
        private static readonly SolidColorBrush YellowBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD6, 0x0A));
        private static readonly SolidColorBrush RedBrush    = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x3B, 0x30));
        private static readonly SolidColorBrush GrayBrush  = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8E, 0x8E, 0x93));

        static SourceHealthToColorConverter()
        {
            GreenBrush.Freeze();
            YellowBrush.Freeze();
            RedBrush.Freeze();
            GrayBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not Channel ch)
                return GrayBrush;

            var sources = ch.Sources;
            if (sources == null || sources.Count == 0)
                return GrayBrush;

            if (sources.Count == 1)
                return GrayBrush;

            // Match menu logic: use Tag if available, otherwise fall back to first source
            var tag = ch.Tag ?? sources.FirstOrDefault();

            if (tag != null && !tag.LastChecked.HasValue)
                return GrayBrush;

            if (tag != null)
            {
                var hasHealthyFallback = sources.Any(s => s != tag && s.IsHealthy && s.IsHttpSource);
                if (tag.IsHealthy) return GreenBrush;
                if (hasHealthyFallback) return YellowBrush;
                return RedBrush;
            }

            return GrayBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
