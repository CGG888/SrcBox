using LibmpvIptvClient;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// This libmpv build has no rtsp-user / rtsp-password properties (writes fail silently), so the
    /// credentials have to be carried inside the url. These tests pin that behaviour.
    /// </summary>
    [TestClass]
    public class RtspCredentialInjectionTests
    {
        static MpvInterop EngineWith(string? user, string? password)
        {
            var settings = new PlaybackSettings();
            settings.HttpHeaders.RtspUser = user ?? "";
            settings.HttpHeaders.EncryptedRtspPassword =
                string.IsNullOrEmpty(password) ? "" : CryptoUtil.ProtectString(password);

            var mpv = new MpvInterop();
            mpv.SetSettings(settings);
            return mpv;
        }

        [TestMethod]
        public void InjectsCredentialsIntoRtspUrls()
        {
            using var mpv = EngineWith("alice", "p@ss word");

            Assert.AreEqual("rtsp://alice:p%40ss%20word@cam.local/stream",
                mpv.WithRtspCredentials("rtsp://cam.local/stream"));
        }

        [TestMethod]
        public void InjectsTheUserWithoutAPassword()
        {
            using var mpv = EngineWith("alice", null);

            Assert.AreEqual("rtsp://alice@cam.local/stream",
                mpv.WithRtspCredentials("rtsp://cam.local/stream"));
        }

        [TestMethod]
        public void LeavesUrlsThatAlreadyCarryCredentialsAlone()
        {
            using var mpv = EngineWith("alice", "secret");

            Assert.AreEqual("rtsp://bob@cam.local/stream",
                mpv.WithRtspCredentials("rtsp://bob@cam.local/stream"));
        }

        [TestMethod]
        public void IgnoresNonRtspUrlsAndUnconfiguredUsers()
        {
            using var mpv = EngineWith("alice", "secret");
            Assert.AreEqual("http://example/x.m3u8", mpv.WithRtspCredentials("http://example/x.m3u8"));
            Assert.AreEqual("", mpv.WithRtspCredentials(""));

            using var noUser = EngineWith(null, "secret");
            Assert.AreEqual("rtsp://cam.local/stream", noUser.WithRtspCredentials("rtsp://cam.local/stream"));
        }
    }
}
