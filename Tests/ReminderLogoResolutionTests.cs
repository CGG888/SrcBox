using System;
using System.Diagnostics;
using System.IO;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Reminder logo resolution must never block the caller: ProcessDue runs on the UI thread when a
    /// reminder is saved or the app starts, and waiting there for the logo download deadlocked the app.
    /// </summary>
    [TestClass]
    public class ReminderLogoResolutionTests
    {
        [TestMethod]
        public void ResolveLogoNonBlocking_ReturnsNullForBlankInput()
        {
            Assert.IsNull(ReminderService.ResolveLogoNonBlocking("CCTV1", null));
            Assert.IsNull(ReminderService.ResolveLogoNonBlocking("CCTV1", "   "));
        }

        [TestMethod]
        public void ResolveLogoNonBlocking_ReturnsAnExistingLocalFile()
        {
            var file = Path.Combine(Path.GetTempPath(), "srcbox-logo-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                File.WriteAllBytes(file, new byte[] { 1, 2, 3 });

                Assert.AreEqual(file, ReminderService.ResolveLogoNonBlocking("CCTV1", file));
            }
            finally { try { File.Delete(file); } catch { } }
        }

        [TestMethod]
        public void ResolveLogoNonBlocking_FallsBackToTheRemoteUrl()
        {
            // Nothing cached: the url is handed through (the toast shows its fallback icon) and the
            // download is scheduled in the background instead of being awaited here.
            var url = "http://127.0.0.1:9/logo/not-cached.png";

            Assert.AreEqual(url, ReminderService.ResolveLogoNonBlocking("CCTV1", url));
        }

        [TestMethod]
        public void ResolveLogoNonBlocking_ReturnsImmediatelyForAnUnreachableUrl()
        {
            var sw = Stopwatch.StartNew();
            var result = ReminderService.ResolveLogoNonBlocking("CCTV1", "http://10.255.255.1/logo.png");
            sw.Stop();

            Assert.AreEqual("http://10.255.255.1/logo.png", result);
            Assert.IsTrue(sw.ElapsedMilliseconds < 1000,
                $"解析台标不应阻塞调用线程，实际耗时 {sw.ElapsedMilliseconds}ms");
        }
    }
}
