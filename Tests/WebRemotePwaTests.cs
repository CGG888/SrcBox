using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using LibmpvIptvClient.Services.WebRemote;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Covers the LAN PWA install path: the https port the page is told about, the in-page install
    /// entry, and the phone sized standard remote layout.
    /// </summary>
    [TestClass]
    public class WebRemotePwaTests
    {
        private static readonly string[] Languages = { "zh-CN", "en-US", "zh-TW", "ru-RU" };

        private static readonly string[] InstallKeys =
        {
            "install_short", "install_title", "install_go", "install_later", "install_android",
            "install_manual", "install_ios", "install_insecure", "install_cert", "install_cert_pem",
            "install_cert_ios", "install_cert_steps", "install_nocert_android", "install_nocert_ios",
            "install_https_open"
        };

        [TestMethod]
        public void RemoteHtml_InjectsTheHttpsPortForTheInstallStep()
        {
            var secure = WebRemoteServer.GetRemoteHtml("en-US", 8900);
            var plain = WebRemoteServer.GetRemoteHtml("en-US");

            StringAssert.Contains(secure, "const HTTPS_PORT = 8900;");
            StringAssert.Contains(plain, "const HTTPS_PORT = 0;");
            Assert.IsFalse(secure.Contains("__HTTPS_PORT__"), "https port placeholder was not replaced");
        }

        [TestMethod]
        public void RemoteHtml_ExposesTheInstallEntryAndModal()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US", 8900);

            foreach (var marker in new[]
                     {
                         "id=\"installFab\"", "showInstallModal()", "id=\"installModal\"", "id=\"installGo\"",
                         "id=\"installBody\"", "beforeinstallprompt", "appinstalled",
                         "display-mode: standalone", "isSecureContext", "srcbox_install_dismissed", "\"/cert\"",
                         "\"/cert.pem\"", "\"/cert.mobileconfig\"", "id=\"fsFab\"", "toggleBrowserFullscreen()"
                     })
            {
                Assert.IsTrue(html.Contains(marker), $"remote html is missing {marker}");
            }
        }

        [TestMethod]
        public void RemoteHtml_FitsTheStandardPadIntoThePhoneViewport()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US");

            foreach (var marker in new[]
                     {
                         "body.remote-mode { height: 100vh; height: 100dvh;",
                         "justify-content: space-between",
                         "--safe-vh: calc((100vh - env(safe-area-inset-top, 0px) - env(safe-area-inset-bottom, 0px)) / 100)",
                         "@supports (height: 100dvh)",
                         "clamp(36px, calc(var(--safe-vh) * 6.6), 64px)",
                         "clamp(30px, calc(var(--safe-vh) * 5.2), 56px)",
                         "clamp(8px, calc(var(--safe-vh) * 1.4), 16px)"
                     })
            {
                Assert.IsTrue(html.Contains(marker), $"remote html is missing {marker}");
            }
        }

        [TestMethod]
        public void RemoteHtml_CoversTheIosSafeAreasInEveryMode()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US");

            // Without viewport-fit=cover the insets stay zero and the pad keeps the letterboxed look.
            StringAssert.Contains(html, "viewport-fit=cover");
            // The status bar style is black-translucent, so the header must reserve the top inset itself.
            StringAssert.Contains(html, "apple-mobile-web-app-status-bar-style");
            StringAssert.Contains(html, "black-translucent");
            // The pad, the browser mode container and the password gate all keep clear of the notch
            // and the home indicator.
            Assert.IsTrue(html.Contains(".container { max-width: 100%; margin: 0 auto; padding: 12px; padding-top: calc(12px + env(safe-area-inset-top, 0px)); padding-right: calc(12px + env(safe-area-inset-right, 0px)); padding-bottom: calc(12px + env(safe-area-inset-bottom, 0px))"),
                "the browser mode container does not reserve the safe areas");
            Assert.IsTrue(html.Contains("body.remote-mode .container { flex: 1; min-height: 0; display: flex; flex-direction: column; padding-top: max(clamp(6px, calc(var(--safe-vh) * 1.2), 12px), env(safe-area-inset-top, 0px))"),
                "the standard pad container does not reserve the safe areas");
            // Sideways the pad keeps the browser gutter, so both modes share one footprint under the header.
            StringAssert.Contains(html, "padding-right: calc(12px + env(safe-area-inset-right, 0px))");
            StringAssert.Contains(html, "padding-left: calc(12px + env(safe-area-inset-left, 0px))");
            StringAssert.Contains(html, "max(clamp(6px, calc(var(--safe-vh) * 1.2), 12px), env(safe-area-inset-bottom, 0px))");
            StringAssert.Contains(html, "#passwordOverlay { position: fixed; top: 0; left: 0; right: 0; bottom: 0;");
            StringAssert.Contains(html, "padding-bottom: calc(16px + env(safe-area-inset-bottom, 0px))");
            // A 180px apple touch icon ships with the app and is what iOS actually uses.
            StringAssert.Contains(html, "apple-touch-icon-180.png");
        }

        [TestMethod]
        public void WebRemoteStrings_ProvideEveryInstallLabel()
        {
            foreach (var lang in Languages)
            {
                var strings = WebRemoteStrings.Get(lang);
                foreach (var key in InstallKeys)
                {
                    Assert.IsTrue(strings.TryGetValue(key, out var value), $"{lang} is missing {key}");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{lang}.{key} is empty");
                }
            }
        }

        [TestMethod]
        public void BuildUrl_SupportsTheHttpsScheme()
        {
            Assert.AreEqual("https://192.168.1.20:8900/", WebRemoteAddressHelper.BuildUrl("192.168.1.20", 8900, "https"));
            Assert.AreEqual("http://192.168.1.20:8899/", WebRemoteAddressHelper.BuildUrl("192.168.1.20", 8899));
        }

        [TestMethod]
        public void LooksLikeTlsHandshake_OnlyAcceptsATlsRecordHeader()
        {
            Assert.IsTrue(WebRemoteServer.LooksLikeTlsHandshake(0x16), "0x16 is a TLS handshake record");
            Assert.IsFalse(WebRemoteServer.LooksLikeTlsHandshake((byte)'G'), "plain 'GET' must stay http");
            Assert.IsFalse(WebRemoteServer.LooksLikeTlsHandshake(0x00), "a zero byte must not be treated as TLS");
        }

        [TestMethod]
        public void RequestPath_StripsTheQueryAndKeepsTheRoute()
        {
            Assert.AreEqual("/cert.pem", WebRemoteServer.RequestPath("GET /cert.pem?x=1 HTTP/1.1"));
            Assert.AreEqual("/cert", WebRemoteServer.RequestPath("GET /cert HTTP/1.1"));
            Assert.AreEqual("", WebRemoteServer.RequestPath(""));
        }
    }

    /// <summary>
    /// Exercises the self-signed CA through an injected directory so the developer machine's real
    /// certificate store stays untouched.
    /// </summary>
    [TestClass]
    public class WebRemoteCertificateTests
    {
        private string _directory = "";

        [TestInitialize]
        public void Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "srcbox-certs-" + Guid.NewGuid().ToString("N"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
            catch
            {
                // A leftover temp folder must never fail the run.
            }
        }

        [TestMethod]
        public void EnsureCore_IssuesAServerCertificateSignedByTheLocalCa()
        {
            var material = WebRemoteCertificateService.EnsureCore(_directory);

            using var server = material.Server;
            using var root = material.Root;

            Assert.IsTrue(root.HasPrivateKey, "the CA certificate has no private key");
            var constraints = root.Extensions.Cast<X509Extension>()
                .OfType<X509BasicConstraintsExtension>()
                .FirstOrDefault();
            Assert.IsNotNull(constraints, "the CA certificate has no basic constraints");
            Assert.IsTrue(constraints!.CertificateAuthority, "the root certificate is not a CA");

            Assert.IsTrue(server.HasPrivateKey, "the server certificate has no private key");
            Assert.AreEqual(root.Subject, server.Issuer, "the server certificate is not issued by the local CA");
            Assert.IsTrue(server.NotAfter > DateTime.Now, "the server certificate is already expired");

            var san = server.Extensions.Cast<X509Extension>().FirstOrDefault(x => x.Oid?.Value == "2.5.29.17");
            Assert.IsNotNull(san, "the server certificate has no subject alternative name");

            Assert.IsTrue(File.Exists(Path.Combine(_directory, "srcbox-ca.pfx")), "the CA was not persisted");
            Assert.IsTrue(File.Exists(Path.Combine(_directory, "srcbox-server.pfx")), "the server certificate was not persisted");
        }

        [TestMethod]
        public void EnsureCore_KeepsServingTheSameCertificateWhileTheAddressesDoNotChange()
        {
            var first = WebRemoteCertificateService.EnsureCore(_directory);
            var second = WebRemoteCertificateService.EnsureCore(_directory);

            Assert.AreEqual(first.Server.Thumbprint, second.Server.Thumbprint,
                "the server certificate was reissued although nothing changed");
            Assert.AreEqual(first.Root.Thumbprint, second.Root.Thumbprint,
                "the CA was reissued although nothing changed");

            first.Server.Dispose();
            first.Root.Dispose();
            second.Server.Dispose();
            second.Root.Dispose();
        }

        [TestMethod]
        public void EnsureCore_ReissuesTheServerCertificateWhenTheAddressListChanges()
        {
            var first = WebRemoteCertificateService.EnsureCore(_directory);
            File.WriteAllText(Path.Combine(_directory, "subjects.txt"), "stale-address-list");

            var second = WebRemoteCertificateService.EnsureCore(_directory);

            Assert.AreNotEqual(first.Server.Thumbprint, second.Server.Thumbprint,
                "the server certificate was not reissued after the address list changed");
            Assert.AreEqual(first.Root.Thumbprint, second.Root.Thumbprint,
                "the CA should survive an address change");

            first.Server.Dispose();
            first.Root.Dispose();
            second.Server.Dispose();
            second.Root.Dispose();
        }

        /// <summary>
        /// Guards the choice of key storage flags: a certificate that imports but cannot complete a
        /// handshake is exactly the failure this whole path exists to prevent, so the certificate the
        /// service actually hands to the server is put through a real TLS handshake here.
        /// </summary>
        [TestMethod]
        public async Task EnsureCore_CertificateCompletesATlsHandshake()
        {
            var material = WebRemoteCertificateService.EnsureCore(_directory);
            using var certificate = material.Server;
            using var root = material.Root;

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;

                var serverTask = Task.Run(async () =>
                {
                    using var accepted = await listener.AcceptTcpClientAsync();
                    using var serverSsl = new SslStream(accepted.GetStream(), false);
                    await serverSsl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate
                    });
                });

                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, port);
                using var clientSsl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
                await clientSsl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost"
                });

                Assert.IsTrue(clientSsl.IsAuthenticated, "the web remote certificate did not complete a TLS handshake");
                Assert.AreEqual(certificate.Subject, clientSsl.RemoteCertificate?.Subject);

                await serverTask;
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public void CollectSubjects_CoversLoopbackAndTheReportedCount()
        {
            var subjects = WebRemoteCertificateService.CollectSubjects();

            CollectionAssert.Contains(subjects, "localhost");
            CollectionAssert.Contains(subjects, "127.0.0.1");

            var material = WebRemoteCertificateService.EnsureCore(_directory);
            Assert.AreEqual(subjects.Count, material.SubjectCount, "the reported SAN count does not match the subject list");

            material.Server.Dispose();
            material.Root.Dispose();
        }

        [TestMethod]
        public void ToPem_WrapsTheCertificateAtSixtyFourCharacters()
        {
            var material = WebRemoteCertificateService.EnsureCore(_directory);
            var pem = WebRemoteCertificateService.ToPem(material.RootDer);

            StringAssert.StartsWith(pem, "-----BEGIN CERTIFICATE-----\n");
            StringAssert.EndsWith(pem, "-----END CERTIFICATE-----\n");
            foreach (var line in pem.Split('\n').Where(l => l.Length > 0 && !l.StartsWith("---")))
            {
                Assert.IsTrue(line.Length <= 64, $"PEM line is {line.Length} characters long");
            }

            // The phone installer must be able to rebuild the exact DER from the text form.
            var base64 = pem.Replace("-----BEGIN CERTIFICATE-----", "").Replace("-----END CERTIFICATE-----", "")
                .Replace("\n", "");
            CollectionAssert.AreEqual(material.RootDer, Convert.FromBase64String(base64));

            material.Server.Dispose();
            material.Root.Dispose();
        }

        [TestMethod]
        public void BuildRootCaProfile_InstallsTheRootAsAnAppleRootPayload()
        {
            var material = WebRemoteCertificateService.EnsureCore(_directory);
            var profile = WebRemoteCertificateService.BuildRootCaProfile(material.RootDer);

            StringAssert.Contains(profile, "<key>PayloadType</key>\n            <string>com.apple.security.root</string>");
            StringAssert.Contains(profile, "<key>PayloadCertificateFileName</key>");
            StringAssert.Contains(profile, "<string>srcbox-ca.crt</string>");
            StringAssert.Contains(profile, "local.srcbox.webremote.ca.");
            StringAssert.Contains(profile, "<key>PayloadRemovalDisallowed</key>");

            // Same certificate, same profile identifiers, so reinstalling updates instead of duplicating.
            Assert.AreEqual(profile, WebRemoteCertificateService.BuildRootCaProfile(material.RootDer));

            material.Server.Dispose();
            material.Root.Dispose();
        }
    }

    /// <summary>
    /// The remote serves plain http and https from one port (the first byte decides), which is what lets
    /// a phone bootstrap the page over http and then install it over https on the very same address.
    /// </summary>
    [TestClass]
    public class WebRemoteTransportTests
    {
        private static int ReserveFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                // The LAN certificate is self-signed by design; the transport is what is under test here.
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        }

        [TestMethod]
        public void OnePortAnswersBothHttpAndHttps()
        {
            var port = ReserveFreePort();
            // The app loads this certificate on startup; loading it here keeps the test independent of the
            // running instance's port and directory state.
            var certificate = WebRemoteCertificateService.GetServerCertificate();
            Assert.IsNotNull(certificate, "no server certificate could be created");

            using var server = new WebRemoteServer();
            server.Start(port);
            try
            {
                Assert.IsTrue(server.IsRunning, "the listener did not start");
                Assert.IsTrue(server.IsTlsAvailable, "TLS was not enabled even though a certificate exists");

                using var client = CreateClient();
                var http = client.GetStringAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult();
                var https = client.GetStringAsync($"https://127.0.0.1:{port}/").GetAwaiter().GetResult();

                StringAssert.Contains(http, "installFab", "the plain http page is not the remote page");
                Assert.AreEqual(http, https, "http and https returned different pages from the same port");

                var pem = client.GetStringAsync($"http://127.0.0.1:{port}/cert.pem").GetAwaiter().GetResult();
                StringAssert.Contains(pem, "-----BEGIN CERTIFICATE-----");

                var profile = client.GetStringAsync($"https://127.0.0.1:{port}/cert.mobileconfig").GetAwaiter().GetResult();
                StringAssert.Contains(profile, "com.apple.security.root");
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
