using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Helpers
{
    public static class ImageExtensions
    {
        private static readonly BitmapImage _defaultImage;
        private static readonly LruCache<string, BitmapImage> _memoryCache = new LruCache<string, BitmapImage>(maxItems: 50);
        private const int DecodeWidth = 160;

        static ImageExtensions()
        {
            try
            {
                _defaultImage = new BitmapImage(new Uri("pack://application:,,,/srcbox.png"));
                _defaultImage.Freeze();
            }
            catch
            {
                _defaultImage = new BitmapImage();
            }
        }

        public static string GetRemoteUrl(DependencyObject obj)
        {
            return (string)obj.GetValue(RemoteUrlProperty);
        }

        public static void SetRemoteUrl(DependencyObject obj, string value)
        {
            obj.SetValue(RemoteUrlProperty, value);
        }

        public static readonly DependencyProperty RemoteUrlProperty =
            DependencyProperty.RegisterAttached("RemoteUrl", typeof(string), typeof(ImageExtensions), new PropertyMetadata(null, OnRemoteUrlChanged));

        private static async void OnRemoteUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is System.Windows.Controls.Image img)
            {
                var url = e.NewValue as string;
                if (string.IsNullOrWhiteSpace(url))
                {
                    img.Source = _defaultImage;
                    return;
                }

                if (_memoryCache.TryGet(url, out var cached))
                {
                    img.Source = cached;
                    return;
                }

                img.Source = _defaultImage;

                try
                {
                    var bitmap = await LoadImageAsync(url);
                    if (bitmap != null)
                    {
                        _memoryCache.Set(url, bitmap);
                        if (GetRemoteUrl(img) == url)
                        {
                            img.Source = bitmap;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to load {url}: {ex.Message}");
                }
            }
        }

        private static async Task<BitmapImage?> LoadImageAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            try
            {
                string processedUrl = url.Trim();
                if (processedUrl.StartsWith("//"))
                    processedUrl = "http:" + processedUrl;
                else if (!processedUrl.Contains("://") && !Path.IsPathRooted(processedUrl))
                {
                    if (!File.Exists(Path.GetFullPath(processedUrl)) && processedUrl.Contains("."))
                        processedUrl = "http://" + processedUrl;
                }

                string? filePath = null;

                if (processedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    filePath = LogoCacheService.Instance.GetCachedPath(processedUrl);
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        filePath = await LogoCacheService.Instance.GetLogoPathAsync("", processedUrl);
                    }
                }
                else if (File.Exists(processedUrl))
                {
                    filePath = processedUrl;
                }
                else
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return null;

                return await Task.Run(() =>
                {
                    try
                    {
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        img.StreamSource = new MemoryStream(File.ReadAllBytes(filePath));
                        img.DecodePixelWidth = DecodeWidth;
                        img.EndInit();
                        img.Freeze();
                        return img;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Bitmap decode error for {url}: {ex.Message}");
                        return null;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Load image error for {url}: {ex.Message}");
            }
            return null;
        }
    }

    internal class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _maxItems;
        private readonly LinkedList<(TKey Key, TValue Value)> _list = new();
        private readonly Dictionary<TKey, LinkedListNode<(TKey, TValue)>> _dict = new();

        public LruCache(int maxItems)
        {
            _maxItems = maxItems;
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_dict.TryGetValue(key, out var node))
            {
                value = node.Value.Item2;
                _list.Remove(node);
                _list.AddLast(node);
                return true;
            }
            value = default!;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            if (_dict.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddLast(node);
                node.Value = (key, value);
            }
            else
            {
                if (_dict.Count >= _maxItems)
                {
                    var first = _list.First;
                    if (first != null)
                    {
                        _dict.Remove(first.Value.Item1);
                        _list.RemoveFirst();
                    }
                }
                node = new LinkedListNode<(TKey, TValue)>((key, value));
                _list.AddLast(node);
                _dict[key] = node;
            }
        }
    }
}
