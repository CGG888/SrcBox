using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public class MainWindowSourceLoaderViewModel : ViewModelBase
    {
        public async Task<(List<Channel> channels, string? tvgUrl)> LoadChannelsAsync(
            ChannelService channelService,
            string m3uUrl,
            Action<string> logAction)
        {
            if (channelService == null || string.IsNullOrWhiteSpace(m3uUrl))
                return (new List<Channel>(), null);

            logAction?.Invoke("加载频道 " + m3uUrl);
            var (channels, tvgUrl) = await channelService.LoadChannelsAsync(m3uUrl, true);
            
            if (channels.Count == 0)
            {
                logAction?.Invoke("未解析到频道");
            }
            else
            {
                logAction?.Invoke("解析频道数量 " + channels.Count);
            }

            return (channels, tvgUrl);
        }

        public List<Channel> LoadSingleStream(string url, string streamLabel)
        {
            var channels = new List<Channel>();
            var ch = new Channel 
            { 
                Name = streamLabel, 
                Tag = new Source { Url = url },
                Group = streamLabel,
                Logo = "/srcbox.png"
            };
            ch.Sources = new List<Source> { (Source)ch.Tag };
            channels.Add(ch);
            return channels;
        }

        public void UpdateLastSource(string m3uPath, M3uSource src)
        {
            if (!string.IsNullOrEmpty(m3uPath))
            {
                AppSettings.Current.LastLocalM3uPath = m3uPath;
            }
            else if (src != null)
            {
                AppSettings.Current.LastLocalM3uPath = "";
                if (AppSettings.Current.SavedSources != null)
                {
                    foreach (var s in AppSettings.Current.SavedSources)
                    {
                        s.IsSelected = (s == src);
                    }
                }
            }
            AppSettings.Current.Save();
        }

        // ---- Per-playlist channel memory -------------------------------------------------
        // Each saved playlist remembers the channel the user last watched in it. Switching back to a
        // playlist resumes that channel instead of leaving the previous playlist's channel playing.

        private string _pendingRestoreUrl = "";
        private string _pendingRestoreChannelKey = "";

        /// <summary>Arms the memory restore for a playlist the user switched to. Only an explicit
        /// switch arms it, so auto-loading a playlist (e.g. at startup) never starts playback.</summary>
        public void ArmChannelRestore(M3uSource? src)
        {
            _pendingRestoreUrl = src?.Url ?? "";
            _pendingRestoreChannelKey = src?.LastChannelKey ?? "";
        }

        /// <summary>Returns the channel key to restore for the playlist that just finished loading and
        /// clears the arming state. Returns an empty string when this load was not an armed switch.</summary>
        public string TakePendingRestoreKey(string? loadedUrl)
        {
            var armedUrl = _pendingRestoreUrl;
            var key = _pendingRestoreChannelKey;
            _pendingRestoreUrl = "";
            _pendingRestoreChannelKey = "";
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(loadedUrl)) return "";
            return string.Equals(armedUrl, loadedUrl, StringComparison.OrdinalIgnoreCase) ? key : "";
        }

        /// <summary>Persists the channel the user is watching under the playlist it came from. A null
        /// channel is a no-op: stopping playback must not erase what the playlist remembers.</summary>
        public void RememberChannel(string? sourceUrl, Channel? channel)
        {
            if (channel == null) return;
            try
            {
                if (ApplyRememberedChannel(AppSettings.Current.SavedSources, sourceUrl, UserDataStore.ComputeKey(channel)))
                {
                    AppSettings.Current.Save();
                }
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Error("[SourceLoader.RememberChannel] " + ex.Message);
            }
        }

        /// <summary>Writes <paramref name="channelKey"/> onto the saved playlist with
        /// <paramref name="sourceUrl"/>. Returns true when the stored value changed (caller saves).</summary>
        public static bool ApplyRememberedChannel(List<M3uSource>? sources, string? sourceUrl, string? channelKey)
        {
            if (sources == null || string.IsNullOrWhiteSpace(sourceUrl)) return false;
            var src = sources.FirstOrDefault(s => string.Equals(s.Url, sourceUrl, StringComparison.OrdinalIgnoreCase));
            if (src == null) return false;
            var key = channelKey ?? "";
            if (string.Equals(src.LastChannelKey, key, StringComparison.Ordinal)) return false;
            src.LastChannelKey = key;
            return true;
        }

        /// <summary>Finds the channel a stored memory key points at, or null when it is gone.</summary>
        public static Channel? FindChannelByKey(IEnumerable<Channel>? channels, string? channelKey)
        {
            if (channels == null || string.IsNullOrWhiteSpace(channelKey)) return null;
            return channels.FirstOrDefault(c =>
                string.Equals(UserDataStore.ComputeKey(c), channelKey, StringComparison.Ordinal));
        }

        public string SanitizeUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var s = input.Trim();
            
            var idx = s.IndexOf('$');
            if (idx > 0) s = s.Substring(0, idx);
            s = s.Trim().TrimEnd(',');
            
            try
            {
                if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !s.Contains("%"))
                {
                    bool hasNonAscii = s.Any(c => c > 127);
                    if (hasNonAscii)
                    {
                        var uri = new Uri(s);
                        return uri.AbsoluteUri;
                    }
                }
            }
            catch { }

            return s;
        }

        public bool IsMulticast(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return false;
                var s = url.ToLowerInvariant();
                if (s.StartsWith("udp://239.")) return true;
                if (s.Contains("/rtp/239.")) return true;
                if (System.Text.RegularExpressions.Regex.IsMatch(url, @"239\.\d+\.\d+\.\d+")) return true;
            }
            catch { }
            return false;
        }

        public List<Source> BuildSourcesForChannel(Channel ch, List<Channel> allChannels)
        {
            var list = new List<Source>();
            if (ch.Sources != null) 
            {
                foreach (var s in ch.Sources) 
                {
                    if (!list.Exists(x => string.Equals(x.Url, s.Url, StringComparison.OrdinalIgnoreCase))) 
                        list.Add(s);
                }
            }

            if (allChannels != null)
            {
                foreach (var c in allChannels)
                {
                    if (c == null) continue;
                    if (string.Equals(c.Name, ch.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (c.Sources != null)
                        {
                            foreach (var s in c.Sources)
                            {
                                if (!list.Exists(x => string.Equals(x.Url, s.Url, StringComparison.OrdinalIgnoreCase))) 
                                    list.Add(s);
                            }
                        }
                        if (c.Tag != null && !list.Exists(x => string.Equals(x.Url, c.Tag.Url, StringComparison.OrdinalIgnoreCase))) 
                            list.Add(c.Tag);
                    }
                }
            }
            return list;
        }
    }
}
