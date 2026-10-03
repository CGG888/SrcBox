using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class TxtParserTests
    {
        // Shaped after the playlist reported in issue #39: "名称,url" lines with
        // "分组名,#genre#" headers (some carrying a trailing space or a "DE=3" suffix) and junk lines.
        const string Sample =
            "抖音K歌,#genre#,DE=3\n" +
            "小米YU7,http://example.com/a.m3u8\n" +
            "AK电影,#genre# \n" +
            "盗源狗si全家 全家下地狱\n" +
            "功夫,http://example.com/b.m3u8\n";

        [TestMethod]
        public void Parse_AssignsGenreGroups_AndSkipsJunkLines()
        {
            var channels = new TxtParser().Parse(Sample);

            Assert.AreEqual(2, channels.Count);
            Assert.AreEqual("抖音K歌", channels[0].Group);
            Assert.AreEqual("小米YU7", channels[0].Name);
            Assert.AreEqual("AK电影", channels[1].Group);
            Assert.AreEqual("功夫", channels[1].Name);
        }

        [TestMethod]
        public void Parse_WithoutGenreHeader_LeavesGroupEmpty()
        {
            var channels = new TxtParser().Parse("CCTV1,http://example.com/cctv1.m3u8");

            Assert.AreEqual(1, channels.Count);
            Assert.AreEqual("", channels[0].Group);
        }

        // Plain-text lists are served from urls without a .txt suffix, so the m3u parser must
        // detect them and delegate instead of returning an empty playlist.
        [TestMethod]
        public void M3uParser_FallsBackToTxtParser()
        {
            var channels = new M3UParser().Parse(Sample);

            Assert.AreEqual(2, channels.Count);
            Assert.AreEqual("抖音K歌", channels[0].Group);
        }

        [TestMethod]
        public void M3uParser_AppliesExtGrp()
        {
            const string m3u =
                "#EXTM3U\n" +
                "#EXTGRP:央视\n" +
                "#EXTINF:-1 tvg-id=\"cctv1\",CCTV-1\n" +
                "http://example.com/1.m3u8\n";

            var channels = new M3UParser().Parse(m3u);

            Assert.AreEqual(1, channels.Count);
            Assert.AreEqual("央视", channels[0].Group);
        }
    }
}
