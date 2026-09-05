using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibmpvIptvClient.Helpers;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    /// <summary>
    /// Builds and shows a right-click context menu for a channel item,
    /// displaying source health status and allowing manual source switching.
    /// </summary>
    public class MainWindowChannelContextMenuManager
    {
        private readonly MainShellViewModel _shell;

        // Track MenuItem references per Source so we can update icons after re-check
        private readonly Dictionary<Source, MenuItem> _sourceItems = new();

        public MainWindowChannelContextMenuManager(MainShellViewModel shell)
        {
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        }

        public void ShowContextMenu(FrameworkElement fe, Channel channel)
        {
            if (fe == null || channel == null) return;

            _sourceItems.Clear();

            // Immediately probe the Tag source so right-click always shows accurate latency.
            // This is an automatic probe → skipped when the user disabled source health scanning.
            var tag = channel.Tag ?? channel.Sources?.FirstOrDefault();
            if (tag != null && tag.IsHttpSource && AppSettings.Current.EnableSourceHealthScan)
            {
                // Wire callback so Ellipse updates when background scan completes
                tag.OnHealthChanged = () => channel.NotifySourceHealthChanged();
                // Fire-and-forget probe - don't block UI thread
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SourceHealthService.Instance.ProbeSourceAsync(tag);
                        if (tag.LatencyMs == 0)
                        {
                            await SourceHealthService.Instance.ProbeSourceAsync(tag);
                        }
                        channel.NotifySourceHealthChanged();
                    }
                    catch { }
                });
            }

            var cm = new ContextMenu();
            fe.ContextMenu = cm;
            var textBrush = GetTextBrushForMenu(cm);

            var sources = channel.Sources;
            if (sources == null || sources.Count == 0)
            {
                cm.Items.Add(new MenuItem { Header = "无源信息", IsEnabled = false });
            }
            else
            {
                // Classify by URL path: /rtp/ or /udp/ = 组播; all others = 单播
                bool IsMulticast(Source s) =>
                    !string.IsNullOrWhiteSpace(s.Url) &&
                    (s.Url.Contains("/rtp/", StringComparison.OrdinalIgnoreCase)
                        || s.Url.Contains("/udp/", StringComparison.OrdinalIgnoreCase));

                // Build full ordered list: multicast first, then unicast
                var orderedSources = sources.OrderBy(s => IsMulticast(s) ? 0 : 1).ToList();

                // Build numbered labels keyed by Source.Id
                int mcIdx = 1, ucIdx = 1;
                var labels = new Dictionary<string, string>();
                foreach (var s in orderedSources)
                {
                    if (!string.IsNullOrWhiteSpace(s.Name))
                        labels[s.Id] = s.Name;
                    else if (IsMulticast(s))
                        labels[s.Id] = $"组播{mcIdx++:D2}";
                    else
                        labels[s.Id] = $"单播{ucIdx++:D2}";
                }

                // Header: current source marked with ● (colored bullet)
                var tagLabel = labels.TryGetValue(tag.Id, out var tl) ? tl : (tag.Name ?? ShortenUrl(tag.Url));
                var tagItem = MakeMenuItemWithLatency(tag, tagLabel, textBrush, isHeader: true, channel: channel, isTagSource: true);
                tagItem.IsEnabled = false;
                cm.Items.Add(tagItem);
                _sourceItems[tag] = tagItem;

                // Fallback sources
                var fallbacks = orderedSources.Where(s => s.Id != tag.Id).ToList();
                if (fallbacks.Count > 0)
                {
                    cm.Items.Add(new Separator());
                    foreach (var s in fallbacks)
                    {
                        var label = labels.TryGetValue(s.Id, out var l) ? l : (s.Name ?? ShortenUrl(s.Url));
                        var mi = MakeMenuItemWithLatency(s, label, textBrush, isHeader: false, channel: channel, isTagSource: false);
                        mi.Click += (_, _) => { try { SwitchToSource(channel, s); } catch { } };
                        cm.Items.Add(mi);
                        _sourceItems[s] = mi;
                    }
                }

                // Re-check action: update icons after scan completes
                cm.Items.Add(new Separator());
                var refreshSp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                refreshSp.Children.Add(new TextBlock
                {
                    Text = "●",
                    Foreground = GrayBrush,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                refreshSp.Children.Add(new TextBlock
                {
                    Text = "检测源健康",
                    Foreground = textBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var refresh = new MenuItem { Header = refreshSp, IsEnabled = true };
                refresh.Click += (_, _) =>
                {
                    SourceHealthService.Instance.StartImmediateRecheck(channel);
                    _ = RefreshIconsAfterProbe(channel);
                };
                cm.Items.Add(refresh);
            }

            cm.IsOpen = true;
        }

        /// <summary>
        /// Returns the appropriate text brush for the current theme.
        /// Uses the application's TextPrimaryBrush resource (dark: #EEEEEE, light: #111111).
        /// </summary>
        private static Brush GetTextBrushForMenu(ContextMenu? cm)
        {
            try { return (Brush)cm!.FindResource("TextPrimaryBrush"); }
            catch { return BlackBrush; }
        }

        /// <summary>
        /// Creates a MenuItem with live-updating latency via WPF data binding.
        /// Uses Binding.Source = s to avoid polluting MenuItem.DataContext inheritance.
        /// </summary>
        private MenuItem MakeMenuItemWithLatency(Source s, string label, Brush textBrush, bool isHeader, Channel channel, bool isTagSource)
        {
            var bulletBrush = GetHealthBrush(s, channel, isTagSource);

            var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = "●",
                Foreground = bulletBrush,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Latency TextBlock bound to Source.LatencyMs via explicit Source=
            var latencyTb = new TextBlock { Foreground = textBrush, VerticalAlignment = VerticalAlignment.Center };

            if (s.IsHttpSource)
            {
                latencyTb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("LatencyMs")
                {
                    Source = s,
                    StringFormat = " {0}ms",
                    Mode = System.Windows.Data.BindingMode.OneWay
                });
            }
            else
            {
                latencyTb.Text = " --ms";
            }

            sp.Children.Add(latencyTb);
            return new MenuItem { Header = sp, IsEnabled = !isHeader };
        }


        /// <summary>
        /// Creates a StackPanel for an action item (no colored bullet, just text).
        /// </summary>
        private static object MakeActionItem(string text, Color? bulletColor)
        {
            var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            if (bulletColor.HasValue)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "●",
                    Foreground = new SolidColorBrush(bulletColor.Value),
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
            }
            sp.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                VerticalAlignment = VerticalAlignment.Center
            });
            return sp;
        }

        /// <summary>
        /// Gets the WPF brush for source health, matching SourceHealthToColorConverter logic.
        /// Gray  — not checked, null source, non-HTTP source, or single-source channel
        /// Green — healthy (reachable, failures < 3)
        /// Yellow — Tag source unhealthy but has a healthy HTTP fallback; or non-Tag unhealthy (failures >= 3)
        /// Red   — unreachable, or Tag source unhealthy with no fallback
        /// </summary>
        private static SolidColorBrush GetHealthBrush(Source s, Channel ch, bool isTagSource)
        {
            if (s == null) return GrayBrush;
            if (!s.IsHttpSource) return GrayBrush;
            if (!s.LastChecked.HasValue) return GrayBrush;

            if (isTagSource)
            {
                if (s.IsHealthy) return GreenBrush;
                // Tag unhealthy: check for any healthy HTTP fallback
                var hasHealthyFallback = ch.Sources?.Any(src => src != s && src.IsHealthy && src.IsHttpSource) ?? false;
                return hasHealthyFallback ? YellowBrush : RedBrush;
            }
            else
            {
                // Non-tag fallback sources: use IsHealthy directly
                if (!s.IsReachable) return RedBrush;
                if (s.IsHealthy) return GreenBrush;
                return YellowBrush;
            }
        }

        private static readonly SolidColorBrush GreenBrush  = new SolidColorBrush(Color.FromRgb(0x35, 0xC7, 0x59));
        private static readonly SolidColorBrush YellowBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A));
        private static readonly SolidColorBrush RedBrush    = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
        private static readonly SolidColorBrush GrayBrush  = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));
        private static readonly SolidColorBrush WhiteBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BlackBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));

        static MainWindowChannelContextMenuManager()
        {
            GreenBrush.Freeze();
            YellowBrush.Freeze();
            RedBrush.Freeze();
            GrayBrush.Freeze();
            WhiteBrush.Freeze();
            BlackBrush.Freeze();
        }

        private async System.Threading.Tasks.Task RefreshIconsAfterProbe(Channel channel)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(AppSettings.Current.SourceHealthProbeTimeoutSec * 1000 + 500);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var currentTag = channel.Tag;
                    foreach (var kvp in _sourceItems)
                    {
                        var s = kvp.Key;
                        var mi = kvp.Value;
                        if (mi.Tag is StackPanel sp && sp.Children.Count >= 1 && sp.Children[0] is TextBlock bullet)
                        {
                            bullet.Text = "●";
                            bullet.Foreground = GetHealthBrush(s, channel, isTagSource: s == currentTag);
                        }
                    }
                });
            }
            catch { }
        }

        private void SwitchToSource(Channel channel, Source targetSource)
        {
            try
            {
                channel.Tag = targetSource;

                if (_shell.CurrentChannel == channel && !string.IsNullOrWhiteSpace(targetSource.Url))
                {
                    _shell.ChannelPlaybackActions.PlayChannel(channel);
                    Diagnostics.Logger.Info($"[Source] Manual switch to {targetSource.Name ?? targetSource.Url}");
                }
                else
                {
                    channel.Tag = targetSource;
                    channel.NotifySourceHealthChanged(); // refresh Ellipse color
                    Diagnostics.Logger.Info($"[Source] Selected source {targetSource.Name ?? targetSource.Url} for {channel.Name}");
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.Debug($"[Source] SwitchToSource error: {ex.Message}");
            }
        }

        private static string ShortenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "(空)";
            try
            {
                var uri = new Uri(url);
                return uri.Host + uri.AbsolutePath;
            }
            catch
            {
                return url.Length > 40 ? url.Substring(0, 40) + "..." : url;
            }
        }
    }
}
