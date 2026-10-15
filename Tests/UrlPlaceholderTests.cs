using System;
using System.Linq;
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

        // ---------------- Kodi / IPTV-org vocabulary (issue #6) ----------------
        // The 重庆联通 report tried the Kodi documented "?time={utc}". SrcBox left "{utc}" in the url, so
        // the portal answered with garbage and catchup never played.

        static long Unix(DateTime local) => new DateTimeOffset(local.ToUniversalTime()).ToUnixTimeSeconds();

        [TestMethod]
        public void Expand_KodiUtcTokens_UseUnixSeconds()
        {
            var url = UrlPlaceholderExpander.Expand("http://h/x?time={utc}&utcend={utcend}", Start, End);

            Assert.AreEqual($"http://h/x?time={Unix(Start)}&utcend={Unix(End)}", url);
            Assert.IsFalse(url.Contains('{'), url);
            Assert.IsFalse(url.Contains('$'), url);
        }

        [TestMethod]
        public void Expand_KodiStartEndAliases_UseUnixSeconds()
        {
            var url = UrlPlaceholderExpander.Expand("http://h/x?start=${start}&end=${end}", Start, End);

            Assert.AreEqual($"http://h/x?start={Unix(Start)}&end={Unix(End)}", url);
        }

        // The plain "{start}"/"{end}" pair keeps its documented local "yyyyMMddHHmmss" meaning.
        [TestMethod]
        public void Expand_FixedStartEnd_StayLocalFormatted()
        {
            var url = UrlPlaceholderExpander.Expand("http://h/x?start={start}&end={end}", Start, End);

            Assert.AreEqual("http://h/x?start=20260319205300&end=20260319212300", url);
        }

        [TestMethod]
        public void Expand_KodiLutcAndNow_AreTheCurrentInstant()
        {
            var before = Unix(DateTime.UtcNow);
            var url = UrlPlaceholderExpander.Expand("http://h/x?a={lutc}&b=${now}", Start, End);
            var after = Unix(DateTime.UtcNow);

            var parts = System.Text.RegularExpressions.Regex.Matches(url, @"\d+")
                            .Select(m => long.Parse(m.Value)).ToList();
            Assert.AreEqual(2, parts.Count, url);
            foreach (var value in parts)
                Assert.IsTrue(value >= before - 2 && value <= after + 2, $"not the current instant: {url}");
        }

        [TestMethod]
        public void Expand_KodiComponents_UseTheWindowStart()
        {
            var url = UrlPlaceholderExpander.Expand("http://h/x?t={Y}-{m}-{d}T{H}:{M}:{S}", Start, End);

            Assert.AreEqual("http://h/x?t=2026-03-19T20:53:00", url);
        }

        [TestMethod]
        public void Expand_KodiScaledDurationAndOffset_AreDivided()
        {
            var duration = UrlPlaceholderExpander.Expand("http://h/x?d={duration:60}", Start, End);
            Assert.AreEqual("http://h/x?d=30", duration);

            var expectedHours = (long)(DateTime.Now - Start).TotalSeconds / 3600;
            var offset = UrlPlaceholderExpander.Expand("http://h/x?o={offset:3600}", Start, End);
            var actualHours = long.Parse(offset.Substring("http://h/x?o=".Length));
            Assert.IsTrue(Math.Abs(actualHours - expectedHours) <= 1, offset);
        }

        [TestMethod]
        public void Expand_KodiUtcFormat_IsTranslatedComponentWise()
        {
            var url = UrlPlaceholderExpander.Expand("http://h/x?start={utc:YmdHM}&end=${end:Ymd-H-M}", Start, End);
            var utcStart = Start.ToUniversalTime();
            var utcEnd = End.ToUniversalTime();

            Assert.AreEqual(
                "http://h/x?start=" + utcStart.ToString("yyyyMMddHHmm")
                + "&end=" + utcEnd.ToString("yyyyMMdd-HH-mm"),
                url);
        }

        // The long documented .NET spelling must keep working next to the Kodi dialect.
        [TestMethod]
        public void Expand_UtcMacroWithDotNetFormat_IsNotTranslated()
        {
            var url = UrlPlaceholderExpander.Expand("http://h/x?p=${utc:yyyyMMddHHmmss}", Start, End);

            Assert.AreEqual("http://h/x?p=" + Start.ToUniversalTime().ToString("yyyyMMddHHmmss"), url);
        }
    }
}
