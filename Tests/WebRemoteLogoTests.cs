using System.IO;
using LibmpvIptvClient.Services.WebRemote;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The /logo/ endpoint serves the channel cache to the phone page. Two details broke it: a cache
    /// busting query turned every request into a 404 that the browser then cached for an hour, and the
    /// .bmp/.ico types the endpoint accepts were answered as application/octet-stream.
    /// </summary>
    [TestClass]
    public class WebRemoteLogoTests
    {
        [TestMethod]
        public void TheQueryStringIsNotPartOfThePath()
        {
            var path = WebRemoteServer.LogoFilePathFromRequest("GET /logo/D%3A%5Ccache%5Ctv.png?v=1712 HTTP/1.1");
            Assert.AreEqual(@"D:\cache\tv.png", path);
        }

        [TestMethod]
        public void AnEncodedWindowsPathIsDecoded()
        {
            var path = WebRemoteServer.LogoFilePathFromRequest("GET /logo/C%3A%5CUsers%5Cme%5Clogo.png HTTP/1.1");
            Assert.AreEqual(@"C:\Users\me\logo.png", path);
        }

        [DataTestMethod]
        [DataRow("GET /logo/ HTTP/1.1")]
        [DataRow("GET /logo/  HTTP/1.1")]
        [DataRow("GET /favicon.ico HTTP/1.1")]
        [DataRow("no-path-here")]
        [DataRow("")]
        public void ARequestWithoutAPathHasNoFile(string request)
            => Assert.IsNull(WebRemoteServer.LogoFilePathFromRequest(request), request);

        [DataTestMethod]
        [DataRow("a.png", "image/png")]
        [DataRow("a.PNG", "image/png")]
        [DataRow("a.jpg", "image/jpeg")]
        [DataRow("a.jpeg", "image/jpeg")]
        [DataRow("a.gif", "image/gif")]
        [DataRow("a.webp", "image/webp")]
        [DataRow("a.bmp", "image/bmp")]
        [DataRow("a.ico", "image/x-icon")]
        [DataRow("a.neg", "application/octet-stream")]
        [DataRow("a.txt", "application/octet-stream")]
        public void EveryAcceptedImageTypeHasAContentType(string file, string expected)
            => Assert.AreEqual(expected, WebRemoteServer.LogoContentType(file), file);

        [TestMethod]
        public void AcceptedAndRejectedLogoPathsStayApart()
        {
            Assert.IsTrue(WebRemoteServer.IsServableLogoImage("logo.png"));
            Assert.IsTrue(WebRemoteServer.IsServableLogoImage("logo.ico"));
            // The ".neg" marker records a logo that could not be downloaded, and is not an image.
            Assert.IsFalse(WebRemoteServer.IsServableLogoImage(Path.Combine(Path.GetTempPath(), "abc.neg")));
            Assert.IsFalse(WebRemoteServer.IsServableLogoImage(null));
        }
    }
}
