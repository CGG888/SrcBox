using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibmpvIptvClient;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;
using LibmpvIptvClient.Architecture.Presentation.View;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The memory switches in Settings -> Playback: they persist, they reach the form state, and turning
    /// one off really stops the player from remembering the channel / the stream variant.
    /// </summary>
    [TestClass]
    public class PlaybackMemorySettingsTests
    {
        static string NewSettingsPath(out string dir)
        {
            dir = Path.Combine(Path.GetTempPath(), "srcbox-memsettings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, PlaybackSettings.FileName);
        }

        static string CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "srcbox-memuserdata-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static Channel BuildChannel(string name, string tvgId, params string[] urls)
        {
            var ch = new Channel { Name = name, TvgId = tvgId };
            foreach (var url in urls)
            {
                ch.Sources.Add(new Source { Url = url, ChannelId = ch.Id });
            }
            ch.Tag = ch.Sources.FirstOrDefault();
            return ch;
        }

        static MainShellViewModel BuildShell(UserDataStore store)
        {
            var shell = new MainShellViewModel();
            shell.InjectServices(new EpgService(), new FakePlayerEngine(), store);
            return shell;
        }

        [TestMethod]
        public void MemorySettings_AreEnabledByDefault()
        {
            var settings = new PlaybackSettings();

            Assert.IsTrue(settings.AutoLoadLastSource);
            Assert.IsTrue(settings.RememberChannelPerPlaylist);
            Assert.IsTrue(settings.RememberChannelSource);
        }

        [TestMethod]
        public void MemorySettings_SurviveSaveAndLoad()
        {
            var path = NewSettingsPath(out var dir);
            try
            {
                new PlaybackSettings
                {
                    AutoLoadLastSource = false,
                    RememberChannelPerPlaylist = false,
                    RememberChannelSource = false
                }.SaveTo(path);

                var loaded = PlaybackSettings.LoadFrom(path);

                Assert.IsFalse(loaded.AutoLoadLastSource);
                Assert.IsFalse(loaded.RememberChannelPerPlaylist);
                Assert.IsFalse(loaded.RememberChannelSource);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [TestMethod]
        public void PlaybackFormState_CarriesTheMemoryToggles()
        {
            var viewModel = new SettingsPlaybackViewModel();
            var source = new PlaybackSettings
            {
                AutoLoadLastSource = false,
                RememberChannelPerPlaylist = false,
                RememberChannelSource = true
            };

            var form = viewModel.BuildFormState(source);

            Assert.IsFalse(form.RememberPlaylistSource);
            Assert.IsFalse(form.RememberChannel);
            Assert.IsTrue(form.RememberChannelSource);

            var target = new PlaybackSettings();
            viewModel.UpdateSettings(target, form);

            Assert.IsFalse(target.AutoLoadLastSource);
            Assert.IsFalse(target.RememberChannelPerPlaylist);
            Assert.IsTrue(target.RememberChannelSource);
        }

        [TestMethod]
        public void RememberChannel_DoesNothing_WhenPlaylistMemoryIsOff()
        {
            var originalFlag = AppSettings.Current.RememberChannelPerPlaylist;
            var originalSources = AppSettings.Current.SavedSources;
            try
            {
                var sources = new List<M3uSource> { new M3uSource { Name = "A", Url = "http://list/a.m3u8" } };
                AppSettings.Current.SavedSources = sources;
                AppSettings.Current.RememberChannelPerPlaylist = false;

                new MainWindowSourceLoaderViewModel().RememberChannel(
                    "http://list/a.m3u8", BuildChannel("CCTV-1", "cctv1", "http://a/1.m3u8"));

                Assert.AreEqual("", sources[0].LastChannelKey, "the setting is off, so nothing may be remembered");
            }
            finally
            {
                AppSettings.Current.SavedSources = originalSources;
                AppSettings.Current.RememberChannelPerPlaylist = originalFlag;
            }
        }

        [TestMethod]
        public void RememberChannelSource_DoesNothing_WhenSourceMemoryIsOff()
        {
            var tempDir = CreateTempDir();
            var originalFlag = AppSettings.Current.RememberChannelSource;
            try
            {
                AppSettings.Current.RememberChannelSource = false;
                var shell = BuildShell(new UserDataStore(tempDir));
                var channel = BuildChannel("CCTV-2", "cctv2", "http://a/2.m3u8", "http://b/2.m3u8");

                shell.RememberChannelSource(channel, channel.Sources[1]);

                Assert.IsNull(shell.UserDataStore.GetPreferredSource("cctv2"));
            }
            finally
            {
                AppSettings.Current.RememberChannelSource = originalFlag;
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [TestMethod]
        public void ApplyRememberedChannelSource_IsSkipped_WhenSourceMemoryIsOff()
        {
            var tempDir = CreateTempDir();
            var originalFlag = AppSettings.Current.RememberChannelSource;
            try
            {
                // A stream was remembered while the setting was still on ...
                new UserDataStore(tempDir).SetPreferredSource("cctv3", "http://b/3.m3u8");
                AppSettings.Current.RememberChannelSource = false;

                var shell = BuildShell(new UserDataStore(tempDir));
                var channel = BuildChannel("CCTV-3", "cctv3", "http://a/3.m3u8", "http://b/3.m3u8");
                shell.ApplyRememberedChannelSource(channel);

                Assert.AreSame(channel.Sources[0], channel.Tag, "with the setting off the default stream must stay");
            }
            finally
            {
                AppSettings.Current.RememberChannelSource = originalFlag;
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
        [TestMethod]
        public void SavingThePlaybackTab_ReachesTheLiveMemorySwitches()
        {
            // The window builds a fresh settings object from the form and the apply step copies it onto the
            // live settings - that copy list used to miss these three flags, so unchecking one in
            // Settings -> Playback was silently discarded while the checkbox looked saved (issue #6).
            var viewModel = new SettingsPlaybackViewModel();
            var form = viewModel.BuildFormState(new PlaybackSettings
            {
                AutoLoadLastSource = false,
                RememberChannelPerPlaylist = false,
                RememberChannelSource = false
            });

            var built = new PlaybackSettings();
            viewModel.UpdateSettings(built, form);

            var live = new PlaybackSettings();
            MainWindowSettingsManager.ApplyMemorySwitches(live, built);

            Assert.IsFalse(live.AutoLoadLastSource);
            Assert.IsFalse(live.RememberChannelPerPlaylist);
            Assert.IsFalse(live.RememberChannelSource);
        }

        [TestMethod]
        public void ApplyMemorySwitches_LeavesTheRestOfTheSettingsAlone()
        {
            var live = new PlaybackSettings { Decoder = "nvdec", CacheSecs = 7 };

            MainWindowSettingsManager.ApplyMemorySwitches(live, new PlaybackSettings { AutoLoadLastSource = false });

            Assert.AreEqual("nvdec", live.Decoder);
            Assert.AreEqual(7, live.CacheSecs);
        }
    }
}
