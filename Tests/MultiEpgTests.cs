using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibmpvIptvClient.Architecture.Application.Settings;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class MultiEpgTests
    {
        [TestMethod]
        public void GetEffectiveUrls_KeepsOrderAndRemovesDuplicates()
        {
            var config = new EpgConfig
            {
                Url = " http://epg.test/one.xml ",
                Urls = new System.Collections.Generic.List<string>
                {
                    "",
                    "http://epg.test/two.xml",
                    "http://epg.test/ONE.xml"
                }
            };

            var urls = config.GetEffectiveUrls();

            Assert.AreEqual(2, urls.Count);
            Assert.AreEqual("http://epg.test/one.xml", urls[0]);
            Assert.AreEqual("http://epg.test/two.xml", urls[1]);
        }

        // Issue #38: several EPG sources must be merged, not replace each other.
        [TestMethod]
        public async Task LoadEpgAsync_MergesProgramsFromSeveralSources()
        {
            var url1 = "http://epg.test/one.xml";
            var url2 = "http://epg.test/two.xml";
            SeedCache(url1, Xml("cctv1", "CCTV-1", "节目A"));
            SeedCache(url2, Xml("cctv2", "CCTV-2", "节目B"));

            var service = new EpgService();
            await service.LoadEpgAsync(new[] { url1, url2 });

            var first = service.GetPrograms("cctv1", "CCTV-1", "CCTV-1");
            var second = service.GetPrograms("cctv2", "CCTV-2", "CCTV-2");

            Assert.IsNotNull(first, "programmes of the first source must survive the merge");
            Assert.IsNotNull(second, "programmes of the second source must be merged in");
            Assert.AreEqual("节目A", first!.First().Title);
            Assert.AreEqual("节目B", second!.First().Title);
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
