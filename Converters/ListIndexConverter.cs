using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace LibmpvIptvClient
{
    public class ListIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var fe = value as FrameworkElement;
            if (fe == null) return "0";
            // 找到所属的 ItemsControl 与对应的容器（ListBoxItem/ContentPresenter）
            ItemsControl? ic = null;
            DependencyObject p = fe;
            while (p != null && ic == null)
            {
                p = VisualTreeHelper.GetParent(p);
                ic = p as ItemsControl;
            }
            if (ic == null) return "0";
            var container = ic.ContainerFromElement(fe);
            var generator = ic.ItemContainerGenerator;
            try
            {
                // 优先用数据项在 Items 中的索引，避免虚拟化回收导致的容器复用问题
                var dataItem = fe.DataContext;
                int idx = ic.Items.IndexOf(dataItem);
                if (idx >= 0) return (idx + 1).ToString();
                // 回退到容器索引
                if (container != null)
                {
                    idx = generator.IndexFromContainer(container);
                    if (idx >= 0) return (idx + 1).ToString();
                }
            }
            catch { }
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
    }

    public class IntPlusOneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i) return (i + 1).ToString();
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
    }
}
