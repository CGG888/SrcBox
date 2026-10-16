using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The archive probe has to surface what the relay answered - that verdict is what tells a wrong url
    /// apart from an asset the server has no archive for - so it is checked against a real socket that
    /// answers like an RTSP portal instead of a mock.
    /// </summary>
    [TestClass]
    public class RtspUrlProbeTests
    {
        /// <summary>Fake portal: accepts one DESCRIBE and answers with the given status/headers.</summary>
        static int StartPortal(string response, out Thread thread, out string seenRequest)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var captured = new StringBuilder();
            var t = new Thread(() =>
            {
                try
                {
                    using var client = listener.AcceptTcpClient();
                    using var stream = client.GetStream();
                    var buffer = new byte[1024];
                    var read = stream.Read(buffer, 0, buffer.Length);
                    captured.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    var bytes = Encoding.ASCII.GetBytes(response);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                    Thread.Sleep(200);
                }
                catch { }
                finally { try { listener.Stop(); } catch { } }
            });
            t.IsBackground = true;
            t.Start();
            thread = t;
            seenRequest = "";
            return port;
        }

        [TestMethod]
        public void Describe_ReportsARefusedArchiveWindow()
        {
            var port = StartPortal("RTSP/1.0 454 Session Not Found\r\nServer: FakePortal/1.0\r\nCSeq: 1\r\n\r\n",
                                   out var thread, out _);

            var verdict = RtspUrlProbe.Describe($"rtsp://127.0.0.1:{port}/PLTV/x.smil?playseek=20261015220000-20261015223600");

            thread.Join(2000);
            StringAssert.Contains(verdict, "454");
            StringAssert.Contains(verdict, "SDP=no");
            StringAssert.Contains(verdict, "FakePortal");
        }

        [TestMethod]
        public void Describe_ReportsAnAcceptedArchiveWindowWithSdp()
        {
            var port = StartPortal("RTSP/1.0 200 OK\r\nServer: FakePortal/1.0\r\nContent-Type: application/sdp\r\nCSeq: 1\r\n\r\nv=0\r\n",
                                   out var thread, out _);

            var verdict = RtspUrlProbe.Describe($"rtsp://127.0.0.1:{port}/PLTV/x.smil?playseek=20261016001300-20261016013200");

            thread.Join(2000);
            StringAssert.Contains(verdict, "200 OK");
            StringAssert.Contains(verdict, "SDP=yes");
        }

        [TestMethod]
        public void Describe_SendsTheQueryToTheServer()
        {
            const string playseek = "?playseek=20261015213000-20261015215600";
            var captured = new StringBuilder();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var t = new Thread(() =>
            {
                try
                {
                    using var client = listener.AcceptTcpClient();
                    using var stream = client.GetStream();
                    var buffer = new byte[1024];
                    var read = stream.Read(buffer, 0, buffer.Length);
                    captured.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    var bytes = Encoding.ASCII.GetBytes("RTSP/1.0 200 OK\r\n\r\n");
                    stream.Write(bytes, 0, bytes.Length);
                }
                catch { }
                finally { try { listener.Stop(); } catch { } }
            }) { IsBackground = true };
            t.Start();

            RtspUrlProbe.Describe($"rtsp://127.0.0.1:{port}/PLTV/x.smil{playseek}");
            t.Join(2000);

            StringAssert.Contains(captured.ToString(), "DESCRIBE /PLTV/x.smil" + playseek + " RTSP/1.0");
        }

        [TestMethod]
        public void Describe_ReportsAFailureInsteadOfThrowing()
        {
            // Nothing listens on this port: the probe must return a readable reason.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            var verdict = RtspUrlProbe.Describe($"rtsp://127.0.0.1:{port}/x.smil", timeoutMs: 800);

            Assert.IsFalse(string.IsNullOrWhiteSpace(verdict));
            Assert.IsFalse(verdict.Contains("RTSP/1.0"));
        }

        /// <summary>
        /// Portal that answers the first DESCRIBE with a redirect to itself and the second one with SDP,
        /// like the Huawei style entry server that points at the node owning the asset.
        /// </summary>
        static int StartRedirectingPortal(string locationQuery, out Thread thread)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var t = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < 2; i++)
                    {
                        using var client = listener.AcceptTcpClient();
                        using var stream = client.GetStream();
                        var buffer = new byte[1024];
                        stream.Read(buffer, 0, buffer.Length);
                        var response = i == 0
                            ? $"RTSP/1.0 302 Moved Temporarily\r\nServer: HWServer/1.0\r\nCSeq: 1\r\nLocation: rtsp://127.0.0.1:{port}/node.smil{locationQuery}\r\n\r\n"
                            : "RTSP/1.0 200 OK\r\nServer: HWServer/1.0\r\nContent-Type: application/sdp\r\nCSeq: 1\r\n\r\nv=0\r\n";
                        var bytes = Encoding.ASCII.GetBytes(response);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush();
                        Thread.Sleep(50);
                    }
                }
                catch { }
                finally { try { listener.Stop(); } catch { } }
            }) { IsBackground = true };
            t.Start();
            thread = t;
            return port;
        }

        [TestMethod]
        public void Describe_FollowsARedirectThatKeepsTheArchiveWindow()
        {
            var port = StartRedirectingPortal("?playseek=20261015220000-20261015223600", out var thread);

            var verdict = RtspUrlProbe.Describe($"rtsp://127.0.0.1:{port}/PLTV/x.smil?playseek=20261015220000-20261015223600");

            thread.Join(3000);
            StringAssert.Contains(verdict, "302");
            StringAssert.Contains(verdict, "200 OK");
            StringAssert.Contains(verdict, "SDP=yes");
            Assert.IsFalse(verdict.Contains("重定向丢弃"), "the redirect kept playseek, so nothing was dropped");
        }

        [TestMethod]
        public void Describe_FlagsARedirectThatDroppedTheArchiveWindow()
        {
            // A redirected url without playseek serves live tv, not the archived programme - the one thing
            // the player cannot recover from, so the probe has to say it out loud.
            var port = StartRedirectingPortal("?from=2&hms_devid=184", out var thread);

            var verdict = RtspUrlProbe.Describe($"rtsp://127.0.0.1:{port}/PLTV/x.smil?playseek=20261015220000-20261015223600");

            thread.Join(3000);
            StringAssert.Contains(verdict, "重定向丢弃 playseek");
        }
    }
}
