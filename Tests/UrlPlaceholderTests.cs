using System;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class UrlPlaceholderTests
    {
        static readonly DateTime Start = new DateTime(2026, 3, 19, 20, 53, 0, DateTimeKind.Local);
        static readonly DateTime End = new DateTime(2026, 3, 19, 21, 23, 0, DateTimeKind.Local);

        [TestMethod]
        public void Expand_LocalMacro_UsesLocalTime()
        {
            var url = UrlPlaceholderExpander.Expand(
                "http://h/live.m3u8?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}", Start, End);

            Assert.AreEqual("http://h/live.m3u8?playseek=20260319205300-20260319212300", url);
        }

        // Issue #13: rtp2httpd's "${utc:...}" macro used to leave a stray '$' in the url.
        [TestMethod]
        public void Expand_UtcMacro_DoesNotLeaveDollarSign()
        {
            var url = UrlPlaceholderExpander.Expand(
                "http://h/x?Playseek=${utc:yyyyMMddHHmmss}-${utcend:yyyyMMddHHmmss}", Start, End);

            var expected = "http://h/x?Playseek="
                + Start.ToUniversalTime().ToString("yyyyMMddHHmmss") + "-"
                + End.ToUniversalTime().ToString("yyyyMMddHHmmss");

            Assert.AreEqual(expected, url);
            Assert.IsFalse(url.Contains('$'), url);
        }

        // Issue #13: "yyyyMMddHHmmss:utc" used to render as "...:u下c" on Chinese Windows.
        [TestMethod]
        public void Expand_UtcSuffixMacro_ConvertsToUtc()
        {
            var url = UrlPlaceholderExpander.Expand("http://h/x?Playseek=${(b)yyyyMMddHHmmss:utc}", Start, End);

            Assert.AreEqual("http://h/x?Playseek=" + Start.ToUniversalTime().ToString("yyyyMMddHHmmss"), url);
        }

        // Issue #37: percent-encoded path/query escapes must survive placeholder expansion.
        [TestMethod]
        public void Expand_KeepsPercentEncodedPathAndQuery()
        {
            var url = UrlPlaceholderExpander.Expand(
                "http://h/%E5%A4%AE%E8%A7%86/CCTV5%2B/catchup?playseek=${(b)yyyyMMddHHmmss}&r2h-token=abc%3D",
                Start, End);

            StringAssert.Contains(url, "/%E5%A4%AE%E8%A7%86/CCTV5%2B/");
            StringAssert.Contains(url, "r2h-token=abc%3D");
            StringAssert.Contains(url, "playseek=20260319205300");
        }

        [TestMethod]
        public void Expand_EscapedPlaceholders_AreDecoded()
        {
            var url = UrlPlaceholderExpander.Expand("http://h/x?playseek=%24%7B(b)yyyyMMddHHmmss%7D", Start, End);

            Assert.AreEqual("http://h/x?playseek=20260319205300", url);
        }

        [TestMethod]
        public void Expand_AppendsEpgTime_OnlyWhenRequested()
        {
            var withParam = UrlPlaceholderExpander.Expand("http://h/x?a=1", Start, End, appendEpgTime: true);
            Assert.AreEqual("http://h/x?a=1&epg_time=2026-03-19T20%3A53", withParam);

            var without = UrlPlaceholderExpander.Expand("http://h/x?a=1", Start, End);
            Assert.AreEqual("http://h/x?a=1", without);
        }
    }
}
