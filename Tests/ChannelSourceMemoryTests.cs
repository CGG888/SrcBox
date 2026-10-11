using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Verifies the per-channel "remembered source variant" behaviour: the stream the user picked
    /// for a channel survives a restart and is selected again the next time that channel plays.
    /// </summary>
    [TestClass]
    public class ChannelSourceMemoryTests
    {
        private static string CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "srcbox-srcmem-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static Channel BuildChannel(string name, string tvgId, params string[] urls)
        {
            var ch = new Channel { Name = name, TvgId = tvgId };
            foreach (var url in urls)
            {
                ch.Sources.Add(new Source { Url = url, ChannelId = ch.Id });
            }
            ch.Tag = ch.Sources.FirstOrDefault();
            return ch;
        }

        private static MainShellViewModel BuildShell(UserDataStore store)
        {
            var shell = new MainShellViewModel();
            shell.InjectServices(new EpgService(), new FakePlayerEngine(), store);
            return shell;
        }

        /// <summary>Close and reopen the player: the picked stream must come back.</summary>
        [TestMethod]
        public void Simulate_CloseAndReopen_PickedSourceIsRemembered()
        {
            var tempDir = CreateTempDir();
            try
            {
                // Session 1: the user switches CCTV-1 to its second stream.
                var shell1 = BuildShell(new UserDataStore(tempDir));
                var channel1 = BuildChannel("CCTV-1", "cctv1", "http://a/1.m3u8", "http://b/1.m3u8");
                shell1.RememberChannelSource(channel1, channel1.Sources[1]);
                Assert.AreEqual("http://b/1.m3u8", shell1.UserDataStore.GetPreferredSource("cctv1"));

                // Session 2: fresh process, fresh channel objects, store loaded from disk.
                var shell2 = BuildShell(new UserDataStore(tempDir));
                var channel2 = BuildChannel("CCTV-1", "cctv1", "http://a/1.m3u8", "http://b/1.m3u8");
                Assert.AreSame(channel2.Sources[0], channel2.Tag, "precondition: default source is selected");

                shell2.ApplyRememberedChannelSource(channel2);

                Assert.AreSame(channel2.Sources[1], channel2.Tag, "the remembered stream should be selected again");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <summary>A remembered url that is no longer offered must not break the default selection.</summary>
        [TestMethod]
        public void ApplyRememberedSource_UnknownUrl_KeepsDefaultSource()
        {
            var tempDir = CreateTempDir();
            try
            {
                var shell = BuildShell(new UserDataStore(tempDir));
                var channel = BuildChannel("CCTV-2", "cctv2", "http://a/2.m3u8", "http://b/2.m3u8");
                shell.RememberChannelSource(channel, new Source { Url = "http://old/2.m3u8" });

                shell.ApplyRememberedChannelSource(channel);

                Assert.AreSame(channel.Sources[0], channel.Tag, "unknown stream must fall back to the default");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <summary>Empty channel/source must not create a memory entry.</summary>
        [TestMethod]
        public void RememberChannelSource_IgnoresEmptyInput()
        {
            var tempDir = CreateTempDir();
            try
            {
                var shell = BuildShell(new UserDataStore(tempDir));
                var channel = BuildChannel("CCTV-3", "cctv3", "http://a/3.m3u8");
                shell.RememberChannelSource(null, channel.Sources[0]);
                shell.RememberChannelSource(channel, null);
                shell.RememberChannelSource(channel, new Source { Url = "   " });

                Assert.IsNull(shell.UserDataStore.GetPreferredSource("cctv3"));
                Assert.AreEqual(0, CountPreferredSources(tempDir), "nothing should have been persisted");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <summary>The variant the user already plays must not be re-selected (auto-degrade replays it).</summary>
        [TestMethod]
        public void ApplyRememberedSource_SkipsChannelThatIsAlreadyPlaying()
        {
            var tempDir = CreateTempDir();
            try
            {
                var shell = BuildShell(new UserDataStore(tempDir));
                var channel = BuildChannel("CCTV-4", "cctv4", "http://a/4.m3u8", "http://b/4.m3u8");
                shell.RememberChannelSource(channel, channel.Sources[1]);

                // Auto-degrade picked the healthy first stream while this channel is on the air.
                channel.Tag = channel.Sources[0];
                shell.CurrentChannel = channel;
                shell.ApplyRememberedChannelSource(channel);

                Assert.AreSame(channel.Sources[0], channel.Tag, "the playing channel must keep its current variant");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <summary>TvgId-less channels must keep one stable key even after the user changes the stream.</summary>
        [TestMethod]
        public void ComputeChannelKey_IsStable_WhenSelectedSourceChanges()
        {
            var channel = BuildChannel("无台标频道", "", "http://a/x.m3u8", "http://b/x.m3u8");
            var before = UserDataStore.ComputeChannelKey(channel);

            channel.Tag = channel.Sources[1];

            Assert.AreEqual(before, UserDataStore.ComputeChannelKey(channel), "the key must not follow the selected source");
            Assert.AreEqual("无台标频道|http://a/x.m3u8", before);
            Assert.AreEqual("cctv1", UserDataStore.ComputeChannelKey(BuildChannel("CCTV-1", "cctv1", "http://a/1.m3u8")));
        }

        /// <summary>Changing the picked stream must replace (not append to) the remembered value.</summary>
        [TestMethod]
        public void SetPreferredSource_ReplacesValue_AndSurvivesReload()
        {
            var tempDir = CreateTempDir();
            try
            {
                var store = new UserDataStore(tempDir);
                store.SetPreferredSource("cctv5", "http://a/5.m3u8");
                store.SetPreferredSource("cctv5", "http://b/5.m3u8");

                Assert.AreEqual(1, CountPreferredSources(tempDir), "one entry per channel");
                Assert.AreEqual("http://b/5.m3u8", new UserDataStore(tempDir).GetPreferredSource("cctv5"));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <summary>Reads how many preferred sources are stored on disk (0 when the file has none yet).</summary>
        private static int CountPreferredSources(string dir)
        {
            var path = Path.Combine(dir, "user_data.json");
            if (!File.Exists(path)) return 0;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("PreferredSources", out var element) &&
                   element.ValueKind == System.Text.Json.JsonValueKind.Object
                ? element.EnumerateObject().Count()
                : 0;
        }
    }
}
