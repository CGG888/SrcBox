using System;
using System.Net.Sockets;
using System.Text;

namespace LibmpvIptvClient.Services
{
    /// <summary>
    /// One-shot RTSP DESCRIBE used to explain an archive that will not start. The relay itself answers with
    /// the verdict - "200 OK" when it will serve the requested <c>playseek</c> window, and a status such as
    /// "454 Session Not Found" / "404 Not Found" / "503" when it will not - which is the difference between
    /// "our url is wrong" and "this asset has no archive". A DESCRIBE alone does not open a media session,
    /// and the probe is only reached while the debug mode is on, so it can never slow down normal playback.
    /// </summary>
    public static class RtspUrlProbe
    {
        public static string Describe(string url, int timeoutMs = 2500)
        {
            try
            {
                var uri = new Uri(url);
                var port = uri.Port > 0 ? uri.Port : 554;

                using var client = new TcpClient();
                var connect = client.ConnectAsync(uri.Host, port);
                if (!connect.Wait(timeoutMs)) return "连接超时";

                using var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                stream.WriteTimeout = timeoutMs;

                var request = $"DESCRIBE {uri.PathAndQuery} RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: SrcBox-Diagnose\r\nAccept: application/sdp\r\n\r\n";
                var payload = Encoding.ASCII.GetBytes(request);
                stream.Write(payload, 0, payload.Length);

                var buffer = new byte[4096];
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) return "无响应";

                var text = Encoding.ASCII.GetString(buffer, 0, read);
                var status = text.Split('\n')[0].Trim().Replace("\r", "");
                var hasSdp = text.IndexOf("application/sdp", StringComparison.OrdinalIgnoreCase) >= 0 ? "SDP=yes" : "SDP=no";
                var server = "";
                foreach (var line in text.Split('\n'))
                {
                    if (line.StartsWith("Server:", StringComparison.OrdinalIgnoreCase))
                    {
                        server = ", " + line.Substring(7).Trim().Replace("\r", "");
                        break;
                    }
                }
                return $"{status} ({hasSdp}{server})";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
