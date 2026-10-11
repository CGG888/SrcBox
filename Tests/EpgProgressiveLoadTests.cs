using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The guide is downloaded while the first channel is already playing, so the panel must be able to
    /// repaint the moment a source is merged instead of waiting for the one-minute refresh timer.
    /// </summary>
    [TestClass]
    public class EpgProgressiveLoadTests
    {
        [TestMethod]
        public void IsLoading_IsFalseBeforeAnyLoad()
        {
            Assert.IsFalse(new EpgService().IsLoading);
        }

        // Every merged source is published: the first guide is usable while the next one is still read.
        [TestMethod]
        public async Task LoadEpgAsync_PublishesEachSourceBeforeTheNextOneIsMerged()
        {
            var url1 = "http://progressive.test/one.xml";
            var url2 = "http://progressive.test/two.xml";
            SeedCache(url1, Xml("news1", "NEWS-1", "节目A"));
            SeedCache(url2, Xml("news2", "NEWS-2", "节目B"));

            var service = new EpgService();
            var snapshots = new List<(int First, int Second, bool Loading)>();
            await service.LoadEpgAsync(new[] { url1, url2 }, () =>
            {
                snapshots.Add((
                    service.GetPrograms("news1", "NEWS-1", "NEWS-1").Count,
                    service.GetPrograms("news2", "NEWS-2", "NEWS-2").Count,
                    service.IsLoading));
            });

            Assert.IsTrue(snapshots.Count >= 3,
                $"one report per source plus one on completion, got {snapshots.Count}");

            // First report: the first source is queryable while the second one has not been read yet.
            Assert.IsTrue(snapshots[0].First > 0, "the first source must be published at its own report");
            Assert.AreEqual(0, snapshots[0].Second, "the second source must not be published before it is read");
            Assert.IsTrue(snapshots[0].Loading, "the load is still running while sources are pending");

            // Last report: everything merged and the loading flag cleared.
            var last = snapshots[snapshots.Count - 1];
            Assert.IsTrue(last.First > 0 && last.Second > 0, "both sources must be merged when the load ends");
            Assert.IsFalse(last.Loading, "the loading flag must be cleared once the load is over");
        }

        // A failed source must still produce the completion report, otherwise the panel never leaves "loading".
        [TestMethod]
        public async Task LoadEpgAsync_ReportsProgressWhenASourceCannotBeParsed()
        {
            var url = "http://progressive.test/broken.xml";
            SeedCache(url, "<tv><channel id=\"broken\">");

            var service = new EpgService();
            var reports = 0;
            await service.LoadEpgAsync(new[] { url }, () => reports++);

            Assert.IsFalse(service.IsLoading, "a failed load must not leave the service in the loading state");
            Assert.IsTrue(reports >= 1, "the caller needs the completion report to leave its loading state");
        }

        static string Xml(string channelId, string displayName, string title) =>
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<tv>\n" +
            $"  <channel id=\"{channelId}\"><display-name>{displayName}</display-name></channel>\n" +
            $"  <programme start=\"20261003120000 +0800\" stop=\"20261003130000 +0800\" channel=\"{channelId}\"><title>{title}</title></programme>\n" +
            "</tv>\n";

        // The service reads its 12h temp-file cache first, so tests can run without network.
        static void SeedCache(string url, string xml)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(url)));
            var path = Path.Combine(Path.GetTempPath(), $"iptv_epg_{hash}.dat");
            File.WriteAllText(path, xml);
        }
    }
}
