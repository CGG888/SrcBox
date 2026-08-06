using System;
using System.Globalization;
using System.Windows.Data;

namespace LibmpvIptvClient.Converters
{
    public class ActionToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string;
                if (string.Equals(s, "play", StringComparison.OrdinalIgnoreCase))
                    return LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Action_Play", "播放");
                if (string.Equals(s, "record_front", StringComparison.OrdinalIgnoreCase))
                    return LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Action_RecordFront", "前台录制");
                if (string.Equals(s, "record_back", StringComparison.OrdinalIgnoreCase))
                    return LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Action_RecordBack", "后台录制");
                return LibmpvIptvClient.Helpers.ResxLocalizer.Get("Reminder_Action_Notify", "通知");
            }
            catch { return "通知"; }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null!;
    }
}
