using System;
using System.Collections.Generic;
using System.IO;
using LibmpvIptvClient;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Per-playlist channel memory: every saved M3U playlist remembers the channel the user last watched
    /// in it, so switching back to that playlist resumes the channel. These tests cover the pure helpers
    /// plus the settings round trip that proves the memory survives a restart.
    /// </summary>
    [TestClass]
    public class M3uSourceMemoryTests
    {
        static string NewPath(out string dir)
        {
            dir = Path.Combine(Path.GetTempPath(), "srcbox-m3u-memory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, PlaybackSettings.FileName);
        }

        [TestMethod]
        public void ApplyRememberedChannel_StoresTheKeyOnTheMatchingPlaylistOnly()
        {
            var a = new M3uSource { Name = "A", Url = "http://example/a.m3u" };
            var b = new M3uSource { Name = "B", Url = "http://example/b.m3u" };
            var sources = new List<M3uSource> { a, b };

            Assert.IsTrue(MainWindowSourceLoaderViewModel.ApplyRememberedChannel(sources, "HTTP://EXAMPLE/A.M3U", "cctv1"));

            Assert.AreEqual("cctv1", a.LastChannelKey);
            Assert.AreEqual("", b.LastChannelKey);
        }

        [TestMethod]
        public void ApplyRememberedChannel_ReturnsFalseWhenNothingChanges()
        {
            var a = new M3uSource { Name = "A", Url = "http://example/a.m3u", LastChannelKey = "cctv1" };
            var sources = new List<M3uSource> { a };

            Assert.IsFalse(MainWindowSourceLoaderViewModel.ApplyRememberedChannel(sources, a.Url, "cctv1"));
            Assert.IsFalse(MainWindowSourceLoaderViewModel.ApplyRememberedChannel(sources, "http://example/missing.m3u", "cctv2"));
            Assert.IsFalse(MainWindowSourceLoaderViewModel.ApplyRememberedChannel(sources, "", "cctv2"));
            Assert.IsFalse(MainWindowSourceLoaderViewModel.ApplyRememberedChannel(null, a.Url, "cctv2"));

            Assert.AreEqual("cctv1", a.LastChannelKey);
        }

        [TestMethod]
        public void ArmChannelRestore_ThenTakeKey_OnlyRestoresTheArmedPlaylist()
        {
            var loader = new MainWindowSourceLoaderViewModel();
            var a = new M3uSource { Name = "A", Url = "http://example/a.m3u", LastChannelKey = "cctv1" };

            Assert.AreEqual("", loader.TakePendingRestoreKey(a.Url), "nothing armed yet");

            loader.ArmChannelRestore(a);
            Assert.AreEqual("", loader.TakePendingRestoreKey("http://example/other.m3u"), "another playlist loaded");
            Assert.AreEqual("", loader.TakePendingRestoreKey(a.Url), "the arming state must be consumed");

            loader.ArmChannelRestore(a);
            Assert.AreEqual("cctv1", loader.TakePendingRestoreKey("HTTP://EXAMPLE/A.M3U"), "the url match ignores case");

            loader.ArmChannelRestore(new M3uSource { Name = "Empty", Url = a.Url });
            Assert.AreEqual("", loader.TakePendingRestoreKey(a.Url), "a playlist without memory restores nothing");
        }

        [TestMethod]
        public void FindChannelByKey_ResolvesByTvgIdAndByNameUrlFallback()
        {
            var byTvgId = new Channel { Name = "CCTV1", TvgId = "cctv1" };
            var byNameUrl = new Channel { Name = "CCTV2", Tag = new Source { Url = "http://example/2" } };
            var channels = new List<Channel> { byTvgId, byNameUrl };

            Assert.AreSame(byTvgId, MainWindowSourceLoaderViewModel.FindChannelByKey(channels, "cctv1"));
            Assert.AreSame(byNameUrl, MainWindowSourceLoaderViewModel.FindChannelByKey(channels, "CCTV2|http://example/2"));
            Assert.IsNull(MainWindowSourceLoaderViewModel.FindChannelByKey(channels, "gone"));
            Assert.IsNull(MainWindowSourceLoaderViewModel.FindChannelByKey(channels, ""));
            Assert.IsNull(MainWindowSourceLoaderViewModel.FindChannelByKey(null, "cctv1"));
        }

        [TestMethod]
        public void LastChannelKey_RoundTripsThroughTheSettingsFile()
        {
            var path = NewPath(out var dir);
            try
            {
                var settings = new PlaybackSettings();
                settings.SavedSources.Add(new M3uSource { Name = "A", Url = "http://example/a.m3u", LastChannelKey = "cctv1" });
                settings.SavedSources.Add(new M3uSource { Name = "B", Url = "http://example/b.m3u" });

                settings.SaveTo(path);
                var loaded = PlaybackSettings.LoadFrom(path);

                Assert.AreEqual(2, loaded.SavedSources.Count);
                Assert.AreEqual("cctv1", loaded.SavedSources[0].LastChannelKey);
                Assert.AreEqual("", loaded.SavedSources[1].LastChannelKey);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
