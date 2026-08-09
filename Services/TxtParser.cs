using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient.Services
{
    public class TxtParser
    {
        public async Task<List<Channel>> ParseFromPathAsync(string path)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var text = DetectAndDecodeText(bytes);
            return Parse(text);
        }

        public async Task<List<Channel>> ParseFromUrlAsync(string url)
        {
            var http = HttpClientService.Instance.Client;
            var data = await http.GetByteArrayAsyncWithRetry(url);
            var text = DetectAndDecodeText(data);
            return Parse(text);
        }

        public List<Channel> Parse(string content)
        {
            var channels = new List<Channel>();
            if (string.IsNullOrEmpty(content)) return channels;

            content = content.TrimStart('\uFEFF', '\u200B');
            var lines = content.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var channel = TryParseLine(line);
                if (channel != null)
                {
                    channels.Add(channel);
                }
            }

            return channels;
        }

        private Channel? TryParseLine(string line)
        {
            string name;
            string url;

            if (line.Contains(','))
            {
                var parts = line.Split(',', 2);
                name = parts[0].Trim();
                url = parts[1].Trim();
            }
            else if (line.Contains(' '))
            {
                var idx = line.IndexOf(' ');
                name = line.Substring(0, idx).Trim();
                url = line.Substring(idx).Trim();
            }
            else
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                return null;

            if (!IsValidStreamUrl(url))
                return null;

            var channel = ChannelPool.Rent();
            channel.Id = Convert.ToHexString(Encoding.UTF8.GetBytes(name.ToLowerInvariant()));
            channel.Name = name;
            channel.Group = "";
            channel.Logo = "";
            channel.TvgId = "";
            channel.TvgName = "";

            var srcQual = GuessQuality(name);
            var src = new Source
            {
                Id = Convert.ToHexString(Encoding.UTF8.GetBytes(channel.Id + "|" + url)),
                Name = "",
                ChannelId = channel.Id,
                Url = url,
                Protocol = GuessProtocol(url),
                Transport = TransportHint.Auto,
                Quality = srcQual
            };

            channel.Tag = src;
            channel.Sources.Add(src);

            return channel;
        }

        private bool IsValidStreamUrl(string url)
        {
            url = url.Trim().ToLowerInvariant();
            return url.StartsWith("http://") || url.StartsWith("https://") ||
                   url.StartsWith("rtp://") || url.StartsWith("udp://") ||
                   url.StartsWith("rtsp://") ||
                   url.StartsWith("srt://");
        }

        private StreamProtocol GuessProtocol(string url)
        {
            var u = url.ToLowerInvariant();
            if (u.Contains(".m3u8") || u.StartsWith("hls+")) return StreamProtocol.HLS;
            if (u.Contains(".mpd") || u.StartsWith("dash+")) return StreamProtocol.DASH;
            if (u.StartsWith("rtsp://")) return StreamProtocol.RTSP;
            if (u.StartsWith("rtp://") || u.StartsWith("udp://")) return StreamProtocol.RTP;
            if (u.StartsWith("srt://")) return StreamProtocol.SRT;
            if (u.StartsWith("http://") || u.StartsWith("https://")) return StreamProtocol.HTTP;
            return StreamProtocol.FILE;
        }

        private SourceQuality GuessQuality(string name)
        {
            var s = name.ToUpperInvariant();
            var q = new SourceQuality();

            if (s.Contains("UHD") || s.Contains("4K") || s.Contains("2160P"))
                q.Height = 2160;
            else if (s.Contains("FHD") || s.Contains("1080P") || s.Contains("1080I"))
                q.Height = 1080;
            else if (s.Contains("HD") || s.Contains("720P"))
                q.Height = 720;
            else if (s.Contains("SD") || s.Contains("576P") || s.Contains("480P"))
                q.Height = 576;

            if (s.Contains("HEVC") || s.Contains("H.265"))
                q.Codec = "HEVC";
            else if (s.Contains("AVC") || s.Contains("H.264"))
                q.Codec = "H.264";

            var fpsMatch = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)\s*FPS", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, out var fps))
                q.Fps = fps;

            return q;
        }

        private string DetectAndDecodeText(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(data);
            }

            try
            {
                var utf8Strict = new UTF8Encoding(false, true);
                return utf8Strict.GetString(data);
            }
            catch
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    var gb = Encoding.GetEncoding("GB18030");
                    return gb.GetString(data);
                }
                catch
                {
                    return Encoding.Default.GetString(data);
                }
            }
        }
    }
}
