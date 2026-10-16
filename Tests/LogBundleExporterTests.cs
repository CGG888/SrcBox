using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using LibmpvIptvClient.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// "导出日志" has to produce a report that can actually explain a stream that never starts: the player log
    /// plus the newest mpv logs in one zip. Users used to attach the player log alone, which says "5s 未见画面"
    /// and nothing about what mpv/FFmpeg saw (issue #6).
    /// </summary>
    [TestClass]
    public class LogBundleExporterTests
    {
        static string NewDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "srcbox-logbundle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [TestMethod]
        public void Bundle_ContainsThePlayerLogAndTheNewestMpvLogs()
        {
            var dir = NewDir();
            var zipPath = Path.Combine(dir, "out.zip");
            try
            {
                var main = Path.Combine(dir, "SrcBox-20261016.log");
                File.WriteAllText(main, "player log");
                File.WriteAllText(Path.Combine(dir, "SrcBox-20261016.debug.log"), "debug log");
                for (var i = 0; i < 4; i++)
                {
                    var file = Path.Combine(dir, $"mpv-20261016-11250{i}-main-{i + 1}.log");
                    File.WriteAllText(file, "mpv " + i);
                    File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(i));
                }

                var count = LogBundleExporter.Build(zipPath, dir, main, Path.Combine(dir, "SrcBox-20261016.debug.log"), maxMpvLogs: 2);

                Assert.AreEqual(4, count, "player log + debug log + the two newest mpv logs");
                using var zip = ZipFile.OpenRead(zipPath);
                var entries = zip.Entries.Select(e => e.Name).OrderBy(n => n).ToArray();
                CollectionAssert.Contains(entries, "SrcBox-20261016.log");
                CollectionAssert.Contains(entries, "SrcBox-20261016.debug.log");
                CollectionAssert.Contains(entries, "mpv-20261016-112503-main-4.log");
                CollectionAssert.Contains(entries, "mpv-20261016-112502-main-3.log");
                Assert.AreEqual(4, entries.Length);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [TestMethod]
        public void Bundle_ReportsZeroWhenThereIsNothingToCollect()
        {
            var dir = NewDir();
            try
            {
                Assert.AreEqual(0, LogBundleExporter.Build(Path.Combine(dir, "out.zip"), dir, null, null));
                Assert.IsFalse(File.Exists(Path.Combine(dir, "out.zip")), "an empty bundle must not be written");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [TestMethod]
        public void Bundle_ReadsAnMpvLogThatIsStillOpenForWriting()
        {
            // mpv holds its log file open while playing, so the export must not depend on exclusive access.
            var dir = NewDir();
            try
            {
                var file = Path.Combine(dir, "mpv-20261016-112506-main-1.log");
                using var locked = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
                var payload = System.Text.Encoding.UTF8.GetBytes("rtsp: Status 302: Redirecting to ...");
                locked.Write(payload, 0, payload.Length);
                locked.Flush();
                var zipPath = Path.Combine(dir, "out.zip");

                var count = LogBundleExporter.Build(zipPath, dir, null, null);

                Assert.AreEqual(1, count);
                using var zip = ZipFile.OpenRead(zipPath);
                using var reader = new StreamReader(zip.Entries[0].Open());
                StringAssert.Contains(reader.ReadToEnd(), "Status 302");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
