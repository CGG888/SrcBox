using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Xaml.Behaviors;
using LibmpvIptvClient.Services;
using LibmpvIptvClient.Models;
using Popup = System.Windows.Controls.Primitives.Popup;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;

namespace LibmpvIptvClient.Behaviors
{
    /// <summary>
    /// Shows a channel preview thumbnail popup on mouse hover.
    /// Popup appears immediately to the LEFT of the hovered channel item.
    /// Does NOT interfere with channel list clicks (IsHitTestVisible=false).
    /// </summary>
    public class ChannelPreviewBehavior : Behavior<FrameworkElement>
    {
        private Popup? _popup;
        private string? _currentUrl;

        // The currently open popup across all instances
        private static Popup? _activePopup;

        #region IsEnabled DependencyProperty
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.Register(
                nameof(IsEnabled),
                typeof(bool),
                typeof(ChannelPreviewBehavior),
                new PropertyMetadata(true));

        public bool IsEnabled
        {
            get => (bool)GetValue(IsEnabledProperty);
            set => SetValue(IsEnabledProperty, value);
        }
        #endregion

        #region PreviewWidth DependencyProperty
        public static readonly DependencyProperty PreviewWidthProperty =
            DependencyProperty.Register(
                nameof(PreviewWidth),
                typeof(double),
                typeof(ChannelPreviewBehavior),
                new PropertyMetadata(214.0));

        public double PreviewWidth
        {
            get => (double)GetValue(PreviewWidthProperty);
            set => SetValue(PreviewWidthProperty, value);
        }
        #endregion

        #region PreviewHeight DependencyProperty
        public static readonly DependencyProperty PreviewHeightProperty =
            DependencyProperty.Register(
                nameof(PreviewHeight),
                typeof(double),
                typeof(ChannelPreviewBehavior),
                new PropertyMetadata(120.0));

        public double PreviewHeight
        {
            get => (double)GetValue(PreviewHeightProperty);
            set => SetValue(PreviewHeightProperty, value);
        }
        #endregion

        protected override void OnAttached()
        {
            base.OnAttached();
            _popup = CreatePopup();
            AssociatedObject.MouseEnter += OnMouseEnter;
            AssociatedObject.MouseLeave += OnMouseLeave;
            AssociatedObject.Unloaded += OnUnloaded;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.MouseEnter -= OnMouseEnter;
            AssociatedObject.MouseLeave -= OnMouseLeave;
            AssociatedObject.Unloaded -= OnUnloaded;
            Cleanup();
            base.OnDetaching();
        }

        private void Cleanup()
        {
            _currentUrl = null;
            if (_popup != null)
            {
                if (_activePopup == _popup) _activePopup = null;
                try { _popup.IsOpen = false; } catch { }
                try { _popup.Child = null; } catch { }
                _popup = null;
            }
        }

        /// <summary>
        /// Closes the currently active popup, if any.
        /// Call this when starting channel playback so the popup doesn't block clicks.
        /// </summary>
        public static void CloseActivePopupIfAny()
        {
            if (_activePopup != null)
            {
                try { _activePopup.IsOpen = false; } catch { }
                _activePopup = null;
            }
        }

        /// <summary>
        /// Extracts stream URL from Channel DataContext.
        /// </summary>
        private static string? GetStreamUrlFromDataContext(FrameworkElement element)
        {
            if (element.DataContext is not Channel ch) return null;

            if (ch.Tag is Source src && !string.IsNullOrWhiteSpace(src.Url))
                return src.Url;

            if (ch.Sources != null && ch.Sources.Count > 0)
            {
                foreach (var s in ch.Sources)
                {
                    if (!string.IsNullOrWhiteSpace(s.Url))
                        return s.Url;
                }
            }

            return null;
        }

        private Popup CreatePopup()
        {
            var popup = new Popup
            {
                AllowsTransparency = true,
                Placement = PlacementMode.Left,
                StaysOpen = true,
                IsOpen = false,
                IsHitTestVisible = false,
            };

            var border = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Width = AppSettings.Current.ChannelPreviewWidth,
                Height = AppSettings.Current.ChannelPreviewHeight,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 2,
                    Opacity = 0.3,
                    Direction = 270
                }
            };

            border.Child = new System.Windows.Controls.TextBlock
            {
                Text = "...",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 12
            };

            popup.Child = border;

            return popup;
        }

        private void UpdatePopupContent(Popup popup, BitmapImage? preview)
        {
            var border = popup.Child as System.Windows.Controls.Border;
            if (border == null) return;

            if (preview != null)
            {
                border.Child = new System.Windows.Controls.Image
                {
                    Source = preview,
                    Stretch = Stretch.UniformToFill,
                    Width = AppSettings.Current.ChannelPreviewWidth,
                    Height = AppSettings.Current.ChannelPreviewHeight
                };
            }
            else
            {
                border.Child = new System.Windows.Controls.TextBlock
                {
                    Text = "...",
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 12
                };
            }
        }

        private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!IsEnabled) return;
            if (!AppSettings.Current.EnableChannelPreview) return;
            if (_popup == null) return;

            var channelUrl = GetStreamUrlFromDataContext(AssociatedObject);
            if (string.IsNullOrWhiteSpace(channelUrl)) return;

            // Close the previously active popup from a different instance
            if (_activePopup != null && _activePopup != _popup)
            {
                var prev = _activePopup;
                _activePopup = null;
                try { prev.IsOpen = false; } catch { }
            }
            _currentUrl = channelUrl;
            _activePopup = _popup;

            // Show popup with placeholder
            _popup.PlacementTarget = AssociatedObject;
            _popup.Placement = PlacementMode.Left;
            UpdatePopupContent(_popup, null);
            _popup.IsOpen = true;

            // Fetch thumbnail asynchronously (does NOT modify URL)
            _ = FetchThumbnailAsync(channelUrl);
        }

        private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_popup == null) return;

            // Close only if this instance's popup is the active one
            if (_activePopup == _popup)
            {
                _activePopup = null;
                _currentUrl = null;
                try { _popup.IsOpen = false; } catch { }
            }
        }

        private async Task FetchThumbnailAsync(string channelUrl)
        {
            if (_currentUrl != channelUrl) return;

            BitmapImage? preview = null;
            try
            {
                preview = await ThumbnailPreviewService.Instance.GetPreviewAsync(channelUrl).ConfigureAwait(false);
            }
            catch { }

            if (_currentUrl != channelUrl) return;

            var popup = _popup;
            if (popup == null) return;

            popup.Dispatcher.Invoke(() =>
            {
                if (_currentUrl != channelUrl) return;
                UpdatePopupContent(popup, preview);
            });
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Cleanup();
        }
    }
}
