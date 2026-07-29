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
