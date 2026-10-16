using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// One-shot RTSP DESCRIBE used to explain an archive that will not start. The relay itself answers with
    /// the verdict - "200 OK" when it will serve the requested <c>playseek</c> window, and a status such as
    /// "454 Session Not Found" / "404 Not Found" / "503" when it will not - which is the difference between
    /// "our url is wrong" and "this asset has no archive".
    ///
    /// Portal style platforms (Huawei HWServer and friends) first answer "302 Moved Temporarily" and point at
    /// the node that actually holds the asset, so the probe follows the redirect and reports the final verdict
    /// as well. It also flags a redirect that dropped the archive parameter: the player hands the entry url to
    /// FFmpeg, which replaces the whole url with the redirect target, and a target without <c>playseek</c>
    /// would serve live tv instead of the requested window.
    ///
    /// A DESCRIBE alone does not open a media session, and the probe is only reached while the debug mode is
    /// on, so it can never slow down normal playback.
    /// </summary>
    public static class RtspUrlProbe
    {
        private const int MaxRedirects = 2;

        /// <summary>Query keys a portal uses to name the requested archive window.</summary>
        private static readonly string[] ArchiveKeys =
        {
            "playseek", "starttime", "endtime", "starttimeutc", "endtimeutc",
            "start", "end", "time", "timestamp", "seek", "tvdr"
        };

        private static readonly Regex CredentialsRegex = new Regex(@"://([^:/@]+):([^@/]+)@", RegexOptions.Compiled);

        public static string Describe(string url, int timeoutMs = 2500)
        {
            try
            {
                var archiveKey = FindArchiveKey(url);
                var hops = new List<string>();
                var current = url;

                for (var hop = 0; hop <= MaxRedirects; hop++)
                {
                    var reply = DescribeOnce(current, timeoutMs);
                    if (reply == null) return hops.Count == 0 ? "无响应" : string.Join(" → ", hops);

                    if (!reply.Value.IsRedirect)
                    {
                        hops.Add(reply.Value.Describe());
                        return string.Join(" → ", hops);
                    }

                    var location = reply.Value.Location!;
                    hops.Add($"{reply.Value.Describe()} → {MaskCredentials(AbsoluteLocation(location, current))}"
                             + (archiveKey != null && !HasKey(location, archiveKey) ? $"[重定向丢弃 {archiveKey}]" : ""));
                    current = AbsoluteLocation(location, current);
                }

                hops.Add("重定向层数过多");
                return string.Join(" → ", hops);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Absolute form of a Location header; portals send absolute urls, but keep relative ones usable.</summary>
        private static string AbsoluteLocation(string location, string baseUrl)
        {
            if (location.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
                location.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase))
            {
                return location;
            }

            try
            {
                var origin = new Uri(baseUrl);
                if (location.StartsWith("/", StringComparison.Ordinal))
                    return $"{origin.Scheme}://{origin.Authority}{location}";
            }
            catch { }
            return location;
        }

        /// <summary>Portal redirects carry the account token as a query value; never copy a password into the log.</summary>
        private static string MaskCredentials(string url)
            => CredentialsRegex.Replace(url, "://$1:***@");

        private static string? FindArchiveKey(string url)
        {
            try
            {
                var query = new Uri(url).Query;
                foreach (var pair in query.TrimStart('?').Split('&'))
                {
                    var key = pair.Split('=')[0];
                    foreach (var candidate in ArchiveKeys)
                    {
                        if (string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase)) return candidate;
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool HasKey(string url, string key)
        {
            var queryIndex = url.IndexOf('?');
            var query = queryIndex >= 0 ? url.Substring(queryIndex + 1) : url;
            foreach (var pair in query.Split('&'))
            {
                if (string.Equals(pair.Split('=')[0], key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static ProbeReply? DescribeOnce(string url, int timeoutMs)
        {
            var uri = new Uri(url);
            var port = uri.Port > 0 ? uri.Port : 554;

            using var client = new TcpClient();
            var connect = client.ConnectAsync(uri.Host, port);
            if (!connect.Wait(timeoutMs)) return null;

            using var stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;
            stream.WriteTimeout = timeoutMs;

            var request = $"DESCRIBE {uri.PathAndQuery} RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: SrcBox-Diagnose\r\nAccept: application/sdp\r\n\r\n";
            var payload = Encoding.ASCII.GetBytes(request);
            stream.Write(payload, 0, payload.Length);

            var text = ReadHeaders(stream);
            if (string.IsNullOrWhiteSpace(text)) return null;

            var lines = text.Split('\n');
            var statusLine = lines[0].Trim().Replace("\r", "");
            var statusCode = ParseStatusCode(statusLine);
            var hasSdp = text.IndexOf("application/sdp", StringComparison.OrdinalIgnoreCase) >= 0;
            string? server = null;
            string? location = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (server == null && line.StartsWith("Server:", StringComparison.OrdinalIgnoreCase))
                    server = line.Substring(7).Trim().Replace("\r", "");
                else if (location == null && line.StartsWith("Location:", StringComparison.OrdinalIgnoreCase))
                    location = line.Substring(9).Trim().Replace("\r", "");
            }

            return new ProbeReply(statusCode, statusLine, hasSdp, server, location);
        }

        /// <summary>Reads until the header block ends, so a Location split across packets is still seen.</summary>
        private static string ReadHeaders(NetworkStream stream)
        {
            var buffer = new byte[2048];
            var text = new StringBuilder();
            while (text.Length < 8192)
            {
                int read;
                try { read = stream.Read(buffer, 0, buffer.Length); }
                catch { break; }
                if (read <= 0) break;
                text.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (text.ToString().Contains("\r\n\r\n")) break;
            }
            return text.ToString();
        }

        private static int ParseStatusCode(string statusLine)
        {
            var parts = statusLine.Split(' ');
            return parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : 0;
        }

        private readonly struct ProbeReply
        {
            public ProbeReply(int statusCode, string statusLine, bool hasSdp, string? server, string? location)
            {
                StatusCode = statusCode;
                StatusLine = statusLine;
                HasSdp = hasSdp;
                Server = server;
                Location = location;
            }

            public int StatusCode { get; }
            public string StatusLine { get; }
            public bool HasSdp { get; }
            public string? Server { get; }
            public string? Location { get; }

            public bool IsRedirect => StatusCode >= 300 && StatusCode < 400 && !string.IsNullOrWhiteSpace(Location);

            public string Describe()
                => $"{StatusLine} ({(HasSdp ? "SDP=yes" : "SDP=no")}{(string.IsNullOrWhiteSpace(Server) ? "" : ", " + Server)})";
        }
    }
}
