using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Covers the catch-up / timeshift url rules. A parameter-only catchup-source has to be appended
    /// to the channel live url (joined with '&amp;' when the live url already carries a query); the view
    /// model used to return the raw value, so a PLTV style '?playseek=...' entry reached the player
    /// without host and the replay screen stayed black (issue #44).
    /// </summary>
    [TestClass]
    public class ArchiveUrlBuilderTests
    {
        private const string PltvLive =
            "http://192.168.50.1:5140/rtsp/10.7.60.172/PLTV/88888888/224/3221225688/723212.smil?rrsip=10.7.227.140&accountinfo=abc";

        private const string Playseek = "?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}";

        private static Channel MakeChannel(string live, string catchupSource = "", string catchup = "")
        {
            var channel = new Channel { Id = "cctv1", Name = "CCTV1", Catchup = catchup, CatchupSource = catchupSource };
            if (!string.IsNullOrEmpty(live))
            {
                var source = new Source { Id = "s1", Name = "live", Url = live };
                channel.Sources.Add(source);
                channel.Tag = source;
            }
            return channel;
        }

        [TestMethod]
        public void CatchupSource_ParameterOnly_IsAppendedToTheLiveUrl()
        {
            var url = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel(PltvLive, Playseek), null, null);

            Assert.AreEqual(
                PltvLive + "&playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}",
                url,
                "a parameter-only catchup-source has to be appended to the live url with '&'");
        }

        [TestMethod]
        public void CatchupSource_ParameterOnly_UsesQuestionMarkWhenTheLiveUrlHasNoQuery()
        {
            var url = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel("http://host/live", "?playseek=1-2"), null, null);

            Assert.AreEqual("http://host/live?playseek=1-2", url);
        }

        [TestMethod]
        public void CatchupSource_FullUrl_IsUsedAsIs()
        {
            const string full = "http://host/replay?starttime=${(b)yyyyMMddHHmmss}&endtime=${(e)yyyyMMddHHmmss}";

            var url = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel(PltvLive, full), null, null);

            Assert.AreEqual(full, url, "a full catchup url must not be joined to the live url");
        }

        [TestMethod]
        public void CatchupAppendMode_AppendsABareSegment()
        {
            var url = ArchiveUrlBuilder.ResolveArchiveUrl(
                MakeChannel(PltvLive, "playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}", "append"),
                null,
                null);

            Assert.AreEqual(PltvLive + "&playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}", url);
        }

        [TestMethod]
        public void CatchupAppendMode_KeepsAFullUrl()
        {
            const string full = "http://host/replay?start=${(b)yyyyMMddHHmmss}";

            var url = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel(PltvLive, full, "append"), null, null);

            Assert.AreEqual(full, url, "catchup=append must not append a value that already carries a host");
        }

        [TestMethod]
        public void Resolve_UsesTheReplayTemplateThenTheTimeshiftTemplate()
        {
            var replay = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel(PltvLive), "?a=${(b)yyyyMMddHHmmss}", "?b=1");
            var timeshift = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel(PltvLive), null, "?b=1");

            Assert.AreEqual(PltvLive + "&a=${(b)yyyyMMddHHmmss}", replay);
            Assert.AreEqual(PltvLive + "&b=1", timeshift);
        }

        [TestMethod]
        public void Resolve_FallsBackToTheTemplateWhenTheChannelSourceCannotBeBuilt()
        {
            var channelWithoutLiveUrl = MakeChannel("", Playseek);
            var withFallback = ArchiveUrlBuilder.ResolveArchiveUrl(channelWithoutLiveUrl, "http://host/template?start=${(b)yyyyMMddHHmmss}", null);
            var withoutFallback = ArchiveUrlBuilder.ResolveArchiveUrl(channelWithoutLiveUrl, null, null);

            Assert.AreEqual("http://host/template?start=${(b)yyyyMMddHHmmss}", withFallback);
            Assert.IsNull(withoutFallback, "no live url and no template means no archive url at all");
        }

        [TestMethod]
        public void BuildFromFormat_ReplacesTheIdPlaceholder()
        {
            var url = ArchiveUrlBuilder.BuildFromFormat(MakeChannel(PltvLive), "http://host/{id}/index.m3u8");

            Assert.AreEqual("http://host/cctv1/index.m3u8", url);
        }

        [TestMethod]
        public void CatchupSource_ParameterOnly_IsAppendedToTheEncodedLiveUrl()
        {
            // rtp2httpd playlists keep the channel name in the path and add a "$quality" suffix.
            // Playback percent-encodes that, so the appended archive request must be built on the
            // encoded url and never on the raw playlist entry.
            var channel = MakeChannel("http://host:5140/央视/CCTV-1$高清", "?playseek=1-2");

            var url = ArchiveUrlBuilder.ResolveArchiveUrl(channel, null, null);

            Assert.AreEqual("http://host:5140/%E5%A4%AE%E8%A7%86/CCTV-1?playseek=1-2", url);
        }

        [TestMethod]
        public void Rtp2httpdLiveUrl_WithoutAnyTemplate_UsesPlayseekWithUtcTimes()
        {
            // Without the "TZ/UTC+N" user agent marker rtp2httpd reads naive times as UTC, so the
            // untouched url has to be seeked with UTC placeholders.
            var url = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel(PltvLive), null, null);

            Assert.AreEqual(
                PltvLive + "&playseek={utc:yyyyMMddHHmmss}-{utcend:yyyyMMddHHmmss}",
                url,
                "an rtp2httpd live url is seekable through playseek even without a catchup-source");
        }

        [TestMethod]
        public void Rtp2httpdLiveUrl_WithTheTimezoneMarker_UsesLocalTimes()
        {
            var url = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel(PltvLive), null, null, rtp2httpdTimeZoneMarker: true);

            Assert.AreEqual(
                PltvLive + "&playseek={start}-{end}",
                url,
                "the TZ/UTC+N marker makes rtp2httpd read naive times in the configured offset");
        }

        [TestMethod]
        public void PlainLiveUrl_WithoutAnyTemplate_StaysUnresolved()
        {
            var url = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel("http://cdn.example.com/live/index.m3u8"), null, null);

            Assert.IsNull(url, "playseek must only be invented for an rtp2httpd url");
        }

        [TestMethod]
        public void UdpMulticastProxyUrl_IsNotTreatedAsRtp2httpd()
        {
            // udpxy ignores an unknown query parameter, so an invented playseek would silently play
            // the live stream instead of telling the user that this channel has no archive.
            var url = ArchiveUrlBuilder.ResolveArchiveUrl(MakeChannel("http://192.168.1.1:4022/rtp/239.1.1.1:5000"), null, null);

            Assert.IsNull(url);
        }
    }
}
