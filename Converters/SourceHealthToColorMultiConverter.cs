using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient
{
    /// <summary>
    /// Converts a Channel to its current source health color brush.
    /// </summary>
    public class SourceHealthToColorMultiConverter : IValueConverter
    {
        private static readonly System.Windows.Media.SolidColorBrush GreenBrush  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x35, 0xC7, 0x59));
        private static readonly System.Windows.Media.SolidColorBrush YellowBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD6, 0x0A));
        private static readonly System.Windows.Media.SolidColorBrush RedBrush    = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x3B, 0x30));
        private static readonly System.Windows.Media.SolidColorBrush GrayBrush  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8E, 0x8E, 0x93));

        static SourceHealthToColorMultiConverter()
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

            var tag = ch.Tag ?? sources.FirstOrDefault();
            if (tag == null)
                return GrayBrush;

            if (!tag.LastChecked.HasValue)
                return GrayBrush;

            var hasHealthyFallback = sources.Any(s => s != tag && s.IsHealthy && s.IsHttpSource);
            if (tag.IsHealthy) return GreenBrush;
            if (hasHealthyFallback) return YellowBrush;
            return RedBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
