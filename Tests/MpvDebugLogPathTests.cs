using System;
using System.IO;
using System.Linq;
using LibmpvIptvClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The mpv debug log (F1 debug mode) has to survive a second player existing. Every instance used to write
    /// "mpv-yyyyMMdd.log", and because opening that path truncates it, the settings test player or a duration
    /// probe threw away the log of the playback being investigated - the one artefact a "stream never starts"
    /// report needs (issue #6).
    /// </summary>
    [TestClass]
    public class MpvDebugLogPathTests
    {
        [TestMethod]
        public void EveryInstanceGetsItsOwnLogFile()
        {
            var now = new DateTime(2026, 10, 16, 11, 25, 6);

            var main = MpvInterop.BuildDebugLogPath(@"C:\logs", "main", now, 1);
            var test = MpvInterop.BuildDebugLogPath(@"C:\logs", "test-rtsp", now, 2);

            Assert.AreNotEqual(main, test, "two instances in the same second must not share a file");
            Assert.IsTrue(Path.GetFileName(main).StartsWith("mpv-20261016-112506-main-", StringComparison.Ordinal));
            Assert.IsTrue(Path.GetFileName(test).Contains("test-rtsp", StringComparison.Ordinal));
            Assert.IsTrue(main.EndsWith(".log", StringComparison.Ordinal));
        }

        [TestMethod]
        public void OldMpvLogsArePrunedButTheNewestOnesSurvive()
        {
            var dir = Path.Combine(Path.GetTempPath(), "srcbox-mpvlogs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                for (var i = 0; i < 5; i++)
                {
                    var file = Path.Combine(dir, $"mpv-2026101{i}-000000-main-{i}.log");
                    File.WriteAllText(file, "x");
                    File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(i));
                }
                // Not an mpv log and must never be deleted.
                var other = Path.Combine(dir, "SrcBox-20261016.log");
                File.WriteAllText(other, "x");

                MpvInterop.PruneMpvLogs(dir, keep: 2);

                var remaining = Directory.GetFiles(dir, "mpv-*.log").Select(Path.GetFileName).OrderBy(f => f).ToArray();
                Assert.AreEqual(2, remaining.Length);
                Assert.AreEqual("mpv-20261013-000000-main-3.log", remaining[0]);
                Assert.AreEqual("mpv-20261014-000000-main-4.log", remaining[1]);
                Assert.IsTrue(File.Exists(other), "only mpv logs are pruned");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
