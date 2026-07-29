using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO.Compression;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Services
{
    public class M3UParser
    {
        private HttpClient _http => HttpClientService.Instance.Client;
        public string TvgUrl { get; private set; } = "";
        
        public M3UParser()
        {
        }
        public async Task<List<Channel>> ParseFromUrlAsync(string url)
        {
            try
            {
                LibmpvIptvClient.Diagnostics.Logger.Log($"[M3UParser] Downloading from {url}");
                var data = await _http.GetByteArrayAsync(url);
                LibmpvIptvClient.Diagnostics.Logger.Log($"[M3UParser] Downloaded {data.Length} bytes");
                
                string text;
                if (IsGzip(data))
                {
                    LibmpvIptvClient.Diagnostics.Logger.Log("[M3UParser] Detected GZIP content");
                    using var ms = new MemoryStream(data);
                    using var gz = new GZipStream(ms, CompressionMode.Decompress);
                    using var sr = new StreamReader(gz, Encoding.UTF8, true);
                    text = await sr.ReadToEndAsync();
                }
                else
                {
                    text = DetectAndDecodeText(data);
                }
                return Parse(text, new Uri(url));
            }
            catch (Exception ex)
            {
                LibmpvIptvClient.Diagnostics.Logger.Error($"M3U ParseFromUrlAsync failed: {ex}");
                throw; // Rethrow to let caller handle
            }
        }
        public async Task<List<Channel>> ParseFromPathAsync(string path)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var text = DetectAndDecodeText(bytes);
            return Parse(text, null);
        }
        public List<Channel> Parse(string content, Uri? baseUri = null)
        {
            TvgUrl = "";
            if (string.IsNullOrEmpty(content)) return new List<Channel>();
            content = content.TrimStart('\uFEFF', '\u200B');
            var lines = content.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var channels = new List<Channel>();
            if (lines.Length == 0) return channels;
            // 容忍前面有备注行，查找第一行 #EXTM3U
            int startIdx = 0;
            while (startIdx < lines.Length && !lines[startIdx].TrimStart().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                startIdx++;
            
            // 如果没找到 #EXTM3U，尝试查找第一个 #EXTINF
            if (startIdx >= lines.Length)
            {
                startIdx = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                    {
                        startIdx = i - 1;
                        break;
                    }
                }
                // 如果也没找到 #EXTINF，则无法解析（因为我们需要 EXTINF 元数据）
                if (startIdx == -1) return channels;
            }
            
            // 解析头部属性
            if (startIdx >= 0 && lines[startIdx].StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                var header = lines[startIdx];
                var headerAttrs = ParseAttributes(header.Substring("#EXTM3U".Length));
                if (headerAttrs.TryGetValue("x-tvg-url", out var tvg)) TvgUrl = tvg;
                else if (headerAttrs.TryGetValue("url-tvg", out var utvg)) TvgUrl = utvg;
            }

            string? currentInf = null;
            for (int i = startIdx + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    currentInf = line;
                }
                else if (!line.StartsWith("#"))
                {
                    if (currentInf != null)
                    {
                        var ch = BuildChannel(currentInf, line, baseUri);
                        if (ch != null) channels.Add(ch);
                    }
                    currentInf = null;
                }
            }
            return channels;
        }
        Channel? BuildChannel(string extinf, string url, Uri? baseUri)
        {
            var attrs = ParseAttributes(extinf);
            var name = ParseDisplayName(extinf);
            var groupTitle = attrs.GetValueOrDefault("group-title") ?? "";
            var logoValue = attrs.GetValueOrDefault("tvg-logo") ?? attrs.GetValueOrDefault("logo") ?? "";
            var ch = new Channel
            {
                Id = attrs.TryGetValue("tvg-id", out var tid) ? tid : "",
                Name = name ?? "",
                Group = string.Intern(groupTitle),
                Logo = string.Intern(logoValue),
                TvgId = attrs.TryGetValue("tvg-id", out var tid2) ? tid2 : "",
                TvgName = attrs.GetValueOrDefault("tvg-name") ?? "",
                Catchup = attrs.GetValueOrDefault("catchup") ?? "",
                CatchupSource = attrs.GetValueOrDefault("catchup-source") ?? ""
            };

            // Fallback: If logo is empty, try to extract from #EXTINF: -1 logo="http://..."
            if (string.IsNullOrEmpty(ch.Logo))
            {
                var logoMatch = Regex.Match(extinf, @"logo=[""']([^""']+)[""']");
                if (logoMatch.Success)
                {
                    ch.Logo = logoMatch.Groups[1].Value;
                }
            }
            if (string.IsNullOrWhiteSpace(ch.Id))
            {
                var key = (ch.Name + "|" + ch.Group).ToLowerInvariant();
                ch.Id = Convert.ToHexString(Encoding.UTF8.GetBytes(key));
            }
            var srcQual = new SourceQuality();
            string suffix = "";
            try
            {
                // 1. Check URL Suffix
                var idxDollarRaw = url.LastIndexOf('$');
                if (idxDollarRaw >= 0 && idxDollarRaw < url.Length - 1)
                {
                    suffix = url.Substring(idxDollarRaw + 1);
                }

                // 2. Combine Name + Suffix for keyword matching
                var searchTarget = (ch.Name + " " + suffix).ToUpperInvariant();

                // Resolution
                if (searchTarget.Contains("UHD") || searchTarget.Contains("4K") || searchTarget.Contains("超高清") || searchTarget.Contains("2160P"))
                    srcQual.Height = 2160;
                else if (searchTarget.Contains("FHD") || searchTarget.Contains("1080P") || searchTarget.Contains("1080I") || searchTarget.Contains("全高清"))
                    srcQual.Height = 1080;
                else if (searchTarget.Contains("HD") || searchTarget.Contains("720P") || searchTarget.Contains("高清"))
                    srcQual.Height = 720;
                else if (searchTarget.Contains("SD") || searchTarget.Contains("576P") || searchTarget.Contains("480P") || searchTarget.Contains("标清"))
                    srcQual.Height = 576;

                // FPS (Usually only in suffix or name like "50fps")
                var mFps = Regex.Match(searchTarget, @"([0-9]+(\.[0-9]+)?)\s*FPS", RegexOptions.IgnoreCase);
                if (mFps.Success)
                {
                    if (double.TryParse(mFps.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var f)) srcQual.Fps = f;
                }

                // Codec
                if (searchTarget.Contains("HEVC") || searchTarget.Contains("H.265"))
                    srcQual.Codec = "HEVC";
                else if (searchTarget.Contains("AVC") || searchTarget.Contains("H.264"))
                    srcQual.Codec = "H.264";
            }
            catch { }
            var cleaned = NormalizeUrl(url, baseUri);
            var u = ResolveUrl(cleaned, baseUri);
            var src = new Source
            {
                Id = Convert.ToHexString(Encoding.UTF8.GetBytes(ch.Id + "|" + u)),
                Name = suffix, // 将 $ 后面的内容作为源名称（如“组播高清”）
                ChannelId = ch.Id,
                Url = u,
                Protocol = GuessProtocol(u),
                Transport = TransportHint.Auto,
                Quality = srcQual
            };
            ch.Tag = src;
            ch.Sources.Add(src);

            // 确保 Logo 不为空，使用默认图标
            if (string.IsNullOrWhiteSpace(ch.Logo))
            {
                // 可以设置为特定的默认图标 URL，或者留空让前端处理
                // ch.Logo = "pack://application:,,,/srcbox.png"; 
            }

            return ch;
        }
        static bool IsGzip(byte[] data) => data.Length > 2 && data[0] == 0x1F && data[1] == 0x8B;
        static string DetectAndDecodeText(byte[] data)
        {
            // 1. Check for BOM
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(data);
            }

            // 2. Try Strict UTF-8
            try
            {
                // throwOnInvalidBytes: true
                var utf8Strict = new UTF8Encoding(false, true);
                return utf8Strict.GetString(data);
            }
            catch
            {
                // UTF-8 failed, try GBK
                try
                {
                    // Register CodePages for .NET Core compatibility
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                    var gb = Encoding.GetEncoding("GB18030");
                    return gb.GetString(data);
                }
                catch
                {
                    // Fallback to default (usually ANSI/GBK on Chinese Windows)
                    return Encoding.Default.GetString(data);
                }
            }
        }
        static string NormalizeUrl(string input, Uri? baseUri)
        {
            var s = input.Trim();
            // 先尝试移除后缀（$及其后面的内容）
            var idxDollar = s.IndexOf('$');
            if (idxDollar > 0) s = s.Substring(0, idxDollar).Trim();

            // 移除尾部逗号
            if (s.EndsWith(",")) s = s.TrimEnd(',');

            // 检查是否已经是有效 URL
            // 对于 rtp2httpd 等本地代理地址，可能包含中文或特殊字符，IsValidAbsoluteUrl 可能会失败
            // 只要看起来像 HTTP/RTP 协议，就应该保留
            if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase) || 
                s.StartsWith("rtp", StringComparison.OrdinalIgnoreCase) || 
                s.StartsWith("udp", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }

            if (IsValidAbsoluteUrl(s)) return s;

            // Fallback: 尝试移除空格 (针对某些带参数的 URL)
            var idxSpace = s.IndexOf(' ');
            if (idxSpace > 0)
            {
                 var t = s.Substring(0, idxSpace).Trim();
                 if (IsValidAbsoluteUrl(t)) return t;
            }

            return s;
        }
        static bool IsValidAbsoluteUrl(string s)
        {
            return Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps || u.Scheme == "rtsp" || u.Scheme == "rtp" || u.Scheme == "udp" || u.Scheme == "srt" || u.Scheme == "file");
        }
        static string ResolveUrl(string url, Uri? baseUri)
        {
            // 对于 rtp2httpd 等本地代理地址，IsValidAbsoluteUrl 可能会因为中文字符等原因失败，但 Uri.TryCreate 仍然可能成功解析为 Absolute
            if (Uri.TryCreate(url, UriKind.Absolute, out var abs)) return abs.ToString();
            
            // 如果 baseUri 存在，尝试作为相对路径解析
            if (baseUri != null && Uri.TryCreate(baseUri, url, out var rel)) return rel.ToString();
            
            // 如果都失败了，直接返回原始 url (可能是包含非标准字符的绝对路径)
            return url;
        }
        static StreamProtocol GuessProtocol(string url)
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
        static Dictionary<string, string> ParseAttributes(string input)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            // 跳过 #EXTINF: 部分
            int startIdx = 0;
            if (input.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                int colon = input.IndexOf(':');
                if (colon > 0) startIdx = colon + 1;
                else startIdx = 7;
            }

            // 扫描整个字符串，提取 key="value" 或 key=value
            // 我们不依赖“最后一个逗号”来截断，因为频道名也可能不包含逗号，或者属性值包含逗号
            // 使用状态机逐字符扫描是最稳妥的
            
            int i = startIdx;
            int len = input.Length;
            
            while (i < len)
            {
                // 跳过空白
                while (i < len && char.IsWhiteSpace(input[i])) i++;
                if (i >= len) break;

                // 如果遇到逗号，这可能是属性之间的（虽然不规范），也可能是频道名的开始
                // 但如果逗号后面紧跟的是 key=value 格式，则继续解析
                // 如果是单纯的逗号，我们尝试跳过它
                if (input[i] == ',')
                {
                    // 检查逗号后面是否还有属性
                    // 如果后面是普通文本且不包含 '='，很可能是频道名了，停止解析
                    int nextEq = input.IndexOf('=', i);
                    if (nextEq < 0) break; // 后面没有等号了，肯定是频道名
                    
                    // 简单的启发式：如果等号前有空格或逗号，说明可能是个 key
                    // 我们这里简单跳过逗号继续尝试
                    i++;
                    continue;
                }

                // 读取 Key
                int keyStart = i;
                while (i < len && (char.IsLetterOrDigit(input[i]) || input[i] == '-' || input[i] == '_' || input[i] == '.'))
                {
                    i++;
                }
                
                string key = input.Substring(keyStart, i - keyStart);
                if (string.IsNullOrEmpty(key)) 
                {
                    // 遇到非 key 字符（且不是逗号/空格），可能是频道名开始了
                    // 比如直接是 "CCTV-1"
                    break; 
                }

                // 期望 key 后面紧跟 '='
                // 允许 key 和 = 之间有空格（虽然不规范）
                while (i < len && char.IsWhiteSpace(input[i])) i++;
                
                if (i >= len || input[i] != '=')
                {
                    // 不是属性赋值，可能是时长参数（如 -1）或频道名的一部分
                    // 如果是数字（时长），忽略它
                    if (int.TryParse(key, out _)) continue;
                    
                    // 否则认为是频道名开始，停止解析
                    break;
                }
                
                i++; // 跳过 '='
                
                // 跳过值前的空白
                while (i < len && char.IsWhiteSpace(input[i])) i++;
                if (i >= len) break;

                string value = "";
                if (input[i] == '"')
                {
                    // 带引号的值
                    i++; // 跳过 "
                    int valStart = i;
                    while (i < len && input[i] != '"') i++;
                    value = input.Substring(valStart, i - valStart);
                    if (i < len) i++; // 跳过闭合 "
                }
                else
                {
                    // 不带引号的值，读取到空格或逗号为止
                    int valStart = i;
                    while (i < len && !char.IsWhiteSpace(input[i]) && input[i] != ',') i++;
                    value = input.Substring(valStart, i - valStart);
                }

                dict[key] = value;
            }
            
            return dict;
        }
        static string? ParseDisplayName(string extinf)
        {
            var comma = extinf.LastIndexOf(',');
            if (comma < 0) return null;
            return extinf.Substring(comma + 1).Trim();
        }
    }
}
