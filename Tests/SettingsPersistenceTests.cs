using System;
using System.IO;
using LibmpvIptvClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Settings persistence: writes must be atomic, failures must be visible and a damaged file must not
    /// silently reset the user's whole configuration. The tests use an explicit path so they never touch
    /// the real user_settings.json.
    /// </summary>
    [TestClass]
    public class SettingsPersistenceTests
    {
        static string NewPath(out string dir)
        {
            dir = Path.Combine(Path.GetTempPath(), "srcbox-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, PlaybackSettings.FileName);
        }

        [TestMethod]
        public void SaveTo_ThenLoadFrom_RoundTripsTheValues()
        {
            var path = NewPath(out var dir);
            try
            {
                var settings = new PlaybackSettings
                {
                    Language = "en-US",
                    ThemeMode = "dark",
                    ShowChannelList = false,
                    ShowEpgPanel = true,
                    FccPrefetchCount = 2,
                    Decoder = "d3d11va"
                };
                settings.Epg.Url = "http://epg.example/x.xml";
                settings.Timeshift.DurationHours = 9;

                settings.SaveTo(path);
                var loaded = PlaybackSettings.LoadFrom(path);

                Assert.AreEqual("en-US", loaded.Language);
                Assert.AreEqual("dark", loaded.ThemeMode);
                Assert.IsFalse(loaded.ShowChannelList);
                Assert.IsTrue(loaded.ShowEpgPanel);
                Assert.AreEqual(2, loaded.FccPrefetchCount);
                Assert.AreEqual("d3d11va", loaded.Decoder);
                Assert.AreEqual("http://epg.example/x.xml", loaded.Epg.Url);
                Assert.AreEqual(9, loaded.Timeshift.DurationHours);
            }
            finally { TryDeleteDir(dir); }
        }

        [TestMethod]
        public void SaveTo_LeavesNoTemporaryFileBehind()
        {
            var path = NewPath(out var dir);
            try
            {
                new PlaybackSettings().SaveTo(path);
                new PlaybackSettings { Language = "ru-RU" }.SaveTo(path);   // second save goes through Replace

                Assert.IsTrue(File.Exists(path));
                Assert.IsFalse(File.Exists(path + ".tmp"), "临时文件必须被清理");
            }
            finally { TryDeleteDir(dir); }
        }

        [TestMethod]
        public void SaveTo_OverwritingCreatesABackupOfThePreviousFile()
        {
            var path = NewPath(out var dir);
            try
            {
                new PlaybackSettings { Language = "zh-CN" }.SaveTo(path);
                new PlaybackSettings { Language = "zh-TW" }.SaveTo(path);

                Assert.AreEqual("zh-TW", PlaybackSettings.LoadFrom(path).Language);
                Assert.IsTrue(File.Exists(path + ".bak"), "覆盖写入应留下上一份备份");
            }
            finally { TryDeleteDir(dir); }
        }

        [TestMethod]
        public void LoadFrom_DamagedFile_IsBackedUpInsteadOfSilentlyDropped()
        {
            var path = NewPath(out var dir);
            try
            {
                File.WriteAllText(path, "{ this is not valid json ");

                var loaded = PlaybackSettings.LoadFrom(path);

                Assert.IsNotNull(loaded);
                Assert.AreEqual("", loaded.Epg.Url, "损坏文件应回退到默认设置");
                Assert.IsTrue(File.Exists(path), "原始损坏文件应保留");
                Assert.IsTrue(File.Exists(path + ".broken"), "损坏文件必须另存一份以便排查");
            }
            finally { TryDeleteDir(dir); }
        }

        [TestMethod]
        public void LoadFrom_MissingFile_ReturnsDefaultsWithoutCreatingABackup()
        {
            var path = NewPath(out var dir);
            try
            {
                var loaded = PlaybackSettings.LoadFrom(path);

                Assert.IsNotNull(loaded);
                Assert.IsFalse(File.Exists(path + ".broken"), "缺文件不算损坏，不应生成 .broken");
                Assert.IsFalse(File.Exists(path), "读取不应凭空创建配置文件");
            }
            finally { TryDeleteDir(dir); }
        }

        [TestMethod]
        public void LoadFrom_MigratesLegacyTopLevelKeys()
        {
            var path = NewPath(out var dir);
            try
            {
                File.WriteAllText(path, """
                {
                  "CustomEpgUrl": "http://old/epg.xml",
                  "CustomLogoUrl": "http://old/logo/",
                  "TimeshiftHours": 2
                }
                """);

                var loaded = PlaybackSettings.LoadFrom(path);

                Assert.AreEqual("http://old/epg.xml", loaded.Epg.Url, "旧配置里的 CustomEpgUrl 必须迁移");
                Assert.AreEqual("http://old/logo/", loaded.Logo.Url, "旧配置里的 CustomLogoUrl 必须迁移");
                Assert.AreEqual(2, loaded.Timeshift.DurationHours, "旧配置里的 TimeshiftHours 必须迁移");
            }
            finally { TryDeleteDir(dir); }
        }

        [TestMethod]
        public void LoadFrom_PrefersTheCurrentNestedKeysOverLegacyOnes()
        {
            var path = NewPath(out var dir);
            try
            {
                File.WriteAllText(path, """
                {
                  "Epg": { "Url": "http://new/epg.xml" },
                  "CustomEpgUrl": "http://old/epg.xml",
                  "Timeshift": { "DurationHours": 8 },
                  "TimeshiftHours": 2
                }
                """);

                var loaded = PlaybackSettings.LoadFrom(path);

                Assert.AreEqual("http://new/epg.xml", loaded.Epg.Url);
                Assert.AreEqual(8, loaded.Timeshift.DurationHours);
            }
            finally { TryDeleteDir(dir); }
        }

        [TestMethod]
        public void LoadFrom_ToleratesLegacyKeysWithUnexpectedTypes()
        {
            var path = NewPath(out var dir);
            try
            {
                File.WriteAllText(path, """
                {
                  "CustomEpgUrl": 123,
                  "TimeshiftHours": "3"
                }
                """);

                var loaded = PlaybackSettings.LoadFrom(path);

                Assert.AreEqual("", loaded.Epg.Url, "非字符串的旧 key 应被忽略而不是抛异常");
                Assert.AreEqual(3, loaded.Timeshift.DurationHours, "字符串形式的数字应能迁移");
            }
            finally { TryDeleteDir(dir); }
        }

        static void TryDeleteDir(string dir)
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
