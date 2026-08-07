using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;

namespace LibmpvIptvClient.Services
{
    public class IptvCheckerClient
    {
        HttpClient _http => HttpClientService.Instance.Client;
        readonly string _baseUrl;
        readonly string _jsonEndpoint;
        readonly string _m3uEndpoint;
        readonly string _token;
        readonly M3UParser _m3u;
        public IptvCheckerClient(M3UParser m3u, string baseUrl, string jsonEndpoint, string m3uEndpoint, string token)
        {
            _m3u = m3u;
            _baseUrl = baseUrl?.TrimEnd('/') ?? "";
            _jsonEndpoint = jsonEndpoint;
            _m3uEndpoint = m3uEndpoint;
            _token = token ?? "";
        }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl);
        public async Task<List<Channel>> LoadChannelsAsync()
        {
            if (!IsConfigured) return new List<Channel>();
            var ok = await TryLoadJson();
            if (ok != null && ok.Count > 0) return ok;
            try
            {
                var m3uUrl = BuildUrl(_m3uEndpoint);
                return await _m3u.ParseFromUrlAsync(m3uUrl);
            }
            catch
            {
                return new List<Channel>();
            }
        }
        async Task<List<Channel>?> TryLoadJson()
        {
            try
            {
                var url = BuildUrl(_jsonEndpoint);
                var txt = await _http.GetStringAsyncWithRetry(url);
                var node = JsonNode.Parse(txt);
                if (node == null) return null;
                if (node is JsonArray arr) return ParseArray(arr);
                if (node is JsonObject obj)
                {
                    if (obj.TryGetPropertyValue("channels", out var chs) && chs is JsonArray a2) return ParseArray(a2);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
        List<Channel> ParseArray(JsonArray arr)
        {
            var list = new List<Channel>();
            foreach (var n in arr)
            {
                if (n is not JsonObject o) continue;
                var ch = new Channel
                {
                    Id = o.TryGetPropertyValue("id", out var idNode) ? idNode?.ToString() ?? "" : "",
                    Name = o.TryGetPropertyValue("name", out var nameNode) ? nameNode?.ToString() ?? "" : "",
                    Group = o.TryGetPropertyValue("group", out var groupNode) ? groupNode?.ToString() ?? "" : "",
                    Logo = o.TryGetPropertyValue("logo", out var logoNode) ? logoNode?.ToString() ?? "" : ""
                };
                if (string.IsNullOrWhiteSpace(ch.Id))
                {
                    var key = (ch.Name + "|" + ch.Group).ToLowerInvariant();
                    ch.Id = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(key));
                }
                if (o.TryGetPropertyValue("sources", out var sourcesNode) && sourcesNode is JsonArray sarr)
                {
                    foreach (var s in sarr)
                    {
                        string url = "";
                        if (s is JsonObject so)
                        {
                            url = so.TryGetPropertyValue("url", out var u) ? u?.ToString() ?? "" : "";
                        }
                        else if (s is JsonValue sv)
                        {
                            url = sv.ToString();
                        }
                        if (string.IsNullOrWhiteSpace(url)) continue;
                        var proto = GuessProtocolFromString("", url);
                        var src = new Source
                        {
                            Id = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(ch.Id + "|" + url)),
                            ChannelId = ch.Id,
                            Url = url,
                            Protocol = proto,
                            Transport = TransportHint.Auto,
                            Quality = new SourceQuality()
                        };
                        if (ch.Tag == null) ch.Tag = src;
                        ch.Sources.Add(src);
                    }
                }
                else if (o.TryGetPropertyValue("url", out var urlNode))
                {
                    var url = urlNode?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        var proto = GuessProtocolFromString("", url);
                        var src = new Source
                        {
                            Id = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(ch.Id + "|" + url)),
                            ChannelId = ch.Id,
                            Url = url,
                            Protocol = proto,
                            Transport = TransportHint.Auto,
                            Quality = new SourceQuality()
                        };
                        ch.Tag = src;
                        ch.Sources.Add(src);
                    }
                }
                list.Add(ch);
            }
            return list;
        }
        static StreamProtocol GuessProtocolFromString(string? proto, string url)
        {
            if (!string.IsNullOrEmpty(proto))
            {
                var p = proto.ToLowerInvariant();
                if (p.Contains("hls")) return StreamProtocol.HLS;
                if (p.Contains("dash")) return StreamProtocol.DASH;
                if (p.Contains("rtsp")) return StreamProtocol.RTSP;
                if (p.Contains("rtp") || p.Contains("udp")) return StreamProtocol.RTP;
                if (p.Contains("srt")) return StreamProtocol.SRT;
            }
            var u = url.ToLowerInvariant();
            if (u.Contains(".m3u8")) return StreamProtocol.HLS;
            if (u.Contains(".mpd")) return StreamProtocol.DASH;
            if (u.StartsWith("rtsp://")) return StreamProtocol.RTSP;
            if (u.StartsWith("rtp://") || u.StartsWith("udp://")) return StreamProtocol.RTP;
            if (u.StartsWith("srt://")) return StreamProtocol.SRT;
            if (u.StartsWith("http://") || u.StartsWith("https://")) return StreamProtocol.HTTP;
            return StreamProtocol.FILE;
        }
        string BuildUrl(string endpoint)
        {
            var ep = endpoint.StartsWith("/") ? endpoint : "/" + endpoint;
            var url = _baseUrl + ep;
            if (!string.IsNullOrEmpty(_token))
            {
                url += (url.Contains("?") ? "&" : "?") + "token=" + Uri.EscapeDataString(_token);
            }
            return url;
        }
    }
}
