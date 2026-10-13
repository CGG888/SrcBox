using System;
using System.IO;
using LibmpvIptvClient.Services;
using LibmpvIptvClient.Services.WebRemote;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The logo cache keeps a "&lt;sha1&gt;.neg" marker next to the images when a logo cannot be
    /// downloaded. The marker used to be handed out as if it were the logo (it matched the hash by
    /// name-ignoring-extension), which made the desktop decode it as an image and the web remote ask the
    /// player for a /logo/ file that is not an image - thousands of refused requests per day.
    /// </summary>
    [TestClass]
    public class LogoCacheServiceTests
    {
        private static readonly TimeSpan Day = TimeSpan.FromHours(24);
        private string _dir = "";

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "srcbox-logo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string WriteFile(string name, TimeSpan? age = null)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, "x");
            if (age.HasValue) File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age.Value);
            return path;
        }

        [TestMethod]
        public void ANegativeMarkerIsNeverReturnedAsALogo()
        {
            WriteFile("abc123.neg");

            Assert.IsNull(LogoCacheService.FindCachedFile(_dir, "abc123", Day));
        }

        [TestMethod]
        public void AStoredImageIsFoundEvenWhenItsExtensionDiffersFromTheUrl()
        {
            var path = WriteFile("abc123.png");

            Assert.AreEqual(path, LogoCacheService.FindCachedFile(_dir, "abc123", Day));
        }

        [TestMethod]
        public void TheImageWinsWhenTheMarkerSitsNextToIt()
        {
            WriteFile("abc123.neg");
            var image = WriteFile("abc123.jpg");

            Assert.AreEqual(image, LogoCacheService.FindCachedFile(_dir, "abc123", Day));
        }

        [TestMethod]
        public void AnExpiredImageIsNotServed()
        {
            WriteFile("abc123.png", TimeSpan.FromHours(48));

            Assert.IsNull(LogoCacheService.FindCachedFile(_dir, "abc123", Day));
        }

        [TestMethod]
        public void AnotherHashAndHiddenDownloadsAreIgnored()
        {
            WriteFile("other.png");
            WriteFile(".dl_pending");

            Assert.IsNull(LogoCacheService.FindCachedFile(_dir, "abc123", Day));
        }

        [TestMethod]
        public void OnlyRealImagesAndCachePathsCountAsUsableLogos()
        {
            Assert.IsFalse(LogoCacheService.IsUsableLogoFile(Path.Combine(_dir, "abc123.neg")));
            Assert.IsTrue(LogoCacheService.IsUsableLogoFile(Path.Combine(_dir, "abc123.png")));
            Assert.IsFalse(LogoCacheService.IsUsableLogoFile(null));
            Assert.IsFalse(LogoCacheService.IsUsableLogoFile(""));
        }

        [TestMethod]
        public void TheLogoEndpointSeparatesAMissingLogoFromAForbiddenPath()
        {
            var marker = Path.Combine(LogoCacheService.Instance.CacheDirectory, "abc123.neg");

            // Inside the cache: not an image, so it is "no logo" rather than an attack.
            Assert.IsTrue(WebRemoteServer.IsInsideAllowedLogoRoot(marker));
            Assert.IsFalse(WebRemoteServer.IsServableLogoImage(marker));
            Assert.IsFalse(WebRemoteServer.IsAllowedLogoPath(marker));

            // Outside every root: refused.
            var outside = Path.Combine(Path.GetTempPath(), "outside.png");
            Assert.IsFalse(WebRemoteServer.IsInsideAllowedLogoRoot(outside));
            Assert.IsTrue(WebRemoteServer.IsServableLogoImage(outside));
            Assert.IsFalse(WebRemoteServer.IsAllowedLogoPath(outside));
        }
    }
}
