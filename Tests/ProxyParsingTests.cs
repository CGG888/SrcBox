using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The Windows "ProxyServer" value can be a per-protocol list; the old parser nested a
    /// "!Contains('=')" check inside "Contains('=')", so those values were ignored and the app went direct.
    /// </summary>
    [TestClass]
    public class ProxyParsingTests
    {
        [TestMethod]
        public void ParseProxyServer_AcceptsThePlainHostPortForm()
        {
            var uri = RegistryProxyProvider.ParseProxyServer("127.0.0.1:7890");

            Assert.IsNotNull(uri);
            Assert.AreEqual("http://127.0.0.1:7890/", uri!.AbsoluteUri);
        }

        [TestMethod]
        public void ParseProxyServer_PrefersTheHttpEntryOfThePerProtocolForm()
        {
            var uri = RegistryProxyProvider.ParseProxyServer("http=127.0.0.1:7890;https=127.0.0.1:7891");

            Assert.IsNotNull(uri);
            Assert.AreEqual("127.0.0.1", uri!.Host);
            Assert.AreEqual(7890, uri.Port);
        }

        [TestMethod]
        public void ParseProxyServer_FallsBackToTheFirstEntryWhenThereIsNoHttpOne()
        {
            var https = RegistryProxyProvider.ParseProxyServer("https=10.0.0.5:3128");
            Assert.IsNotNull(https);
            Assert.AreEqual("10.0.0.5", https!.Host);
            Assert.AreEqual(3128, https.Port);

            var socks = RegistryProxyProvider.ParseProxyServer("socks=127.0.0.1:1080");
            Assert.IsNotNull(socks);
            Assert.AreEqual(1080, socks!.Port);
        }

        [TestMethod]
        public void ParseProxyServer_SkipsEmptyEntries()
        {
            var uri = RegistryProxyProvider.ParseProxyServer("http=;https=1.2.3.4:80");

            Assert.IsNotNull(uri);
            Assert.AreEqual("1.2.3.4", uri!.Host);
            Assert.AreEqual(80, uri.Port);
        }

        [TestMethod]
        public void ParseProxyServer_ReturnsNullForMissingOrUnusableValues()
        {
            Assert.IsNull(RegistryProxyProvider.ParseProxyServer(null));
            Assert.IsNull(RegistryProxyProvider.ParseProxyServer("   "));
            Assert.IsNull(RegistryProxyProvider.ParseProxyServer("http=;ftp=;"));
        }
    }
}
