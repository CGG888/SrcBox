using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace LibmpvIptvClient.Controls
{
    public partial class VolumeSlider : UserControl
    {
        public static readonly DependencyProperty VolumeProperty =
            DependencyProperty.Register("Volume", typeof(double), typeof(VolumeSlider),
                new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVolumeChanged));

        public double Volume
        {
            get { return (double)GetValue(VolumeProperty); }
            set { SetValue(VolumeProperty, value); }
        }

        public static readonly DependencyProperty IsMutedProperty =
            DependencyProperty.Register("IsMuted", typeof(bool), typeof(VolumeSlider),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsMutedChanged));

        /// <summary>
        /// 静音状态。true 时音量条变为 MutedBrush（灰色），false 时为 AccentBrush（绿色）。
        /// </summary>
        public bool IsMuted
        {
            get { return (bool)GetValue(IsMutedProperty); }
            set { SetValue(IsMutedProperty, value); }
        }

        public static readonly RoutedEvent VolumeChangedEvent =
            EventManager.RegisterRoutedEvent("VolumeChanged", RoutingStrategy.Bubble, 
                typeof(RoutedPropertyChangedEventHandler<double>), typeof(VolumeSlider));

        public event RoutedPropertyChangedEventHandler<double> VolumeChanged
        {
            add { AddHandler(VolumeChangedEvent, value); }
            remove { RemoveHandler(VolumeChangedEvent, value); }
        }

        private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (VolumeSlider)d;
            control.RaiseEvent(new RoutedPropertyChangedEventArgs<double>((double)e.OldValue, (double)e.NewValue, VolumeChangedEvent));
        }

        private static void OnIsMutedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 触发 Slider 样式的 DataTrigger 重新评估
            var control = (VolumeSlider)d;
            try
            {
                if (control.PART_Slider != null)
                {
                    // 强制 Slider 重新应用样式（让 DataTrigger 重新评估）
                    var style = control.PART_Slider.Style;
                    control.PART_Slider.Style = null;
                    control.PART_Slider.Style = style;
                }
            }
            catch { }
        }

        public VolumeSlider()
        {
            InitializeComponent();
        }

        private void Grid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double delta = e.Delta > 0 ? 5 : -5;
            double newValue = Math.Max(0, Math.Min(100, Volume + delta));
            Volume = newValue;
            
            ShowTooltip(newValue);
            UpdateTooltipPosition(newValue);
            
            // Auto-hide tooltip after delay if needed, but for now relying on MouseLeave or next interaction
            // Since wheel can happen without mouse move, we might want to hide it after a timer?
            // For simplicity, we leave it until MouseLeave or it will stay if mouse is over.
            
            e.Handled = true;
        }

        private void PART_Slider_MouseMove(object sender, MouseEventArgs e)
        {
            var slider = sender as Slider;
            if (slider == null) return;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // Dragging: Show actual volume
                ShowTooltip(Volume);
                UpdateTooltipPosition(Volume);
            }
            else
            {
                // Hovering: Show preview value
                var point = e.GetPosition(slider);
                var percent = point.X / slider.ActualWidth;
                var val = slider.Minimum + (slider.Maximum - slider.Minimum) * percent;
                val = Math.Max(0, Math.Min(100, val));
                
                ShowTooltip(val);
                
                // Position at cursor
                if (VolumeTooltip.Child is FrameworkElement child)
                {
                     VolumeTooltip.HorizontalOffset = point.X - child.ActualWidth / 2;
                }
            }
        }

        private void PART_Slider_MouseLeave(object sender, MouseEventArgs e)
        {
            VolumeTooltip.IsOpen = false;
        }

        private void ShowTooltip(double value)
        {
            if (TooltipText != null)
            {
                TooltipText.Text = $"{(int)value}%";
            }
            if (VolumeTooltip != null)
            {
                VolumeTooltip.IsOpen = true;
            }
        }

        private void UpdateTooltipPosition(double value)
        {
            if (PART_Slider == null || VolumeTooltip == null || !(VolumeTooltip.Child is FrameworkElement child)) return;
            
            double percent = (value - PART_Slider.Minimum) / (PART_Slider.Maximum - PART_Slider.Minimum);
            double x = percent * PART_Slider.ActualWidth;
            VolumeTooltip.HorizontalOffset = x - child.ActualWidth / 2;
        }
    }
}
