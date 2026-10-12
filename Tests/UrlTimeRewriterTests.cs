using System;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class UrlTimeRewriterTests
    {
        static readonly DateTime Start = new DateTime(2026, 3, 19, 20, 53, 0, DateTimeKind.Local);
        static readonly DateTime End = new DateTime(2026, 3, 19, 21, 23, 0, DateTimeKind.Local);

        static PlaybackSettings Default() => new PlaybackSettings();

        // 关键回归：时移模式下曾经无条件把时间参数改写成"本地 14 位 starttime/endtime"，
        // 把频道自带模板（常见是 UTC 或 playseek 布局）直接毁掉，导致时移起播即失败。
        [TestMethod]
        public void RewriteIfEnabled_KeepsTemplateTimes_InTimeshift()
        {
            const string url = "http://h/x.m3u8?starttime=20260319T125300&endtime=20260319T132300&token=abc";

            var result = UrlTimeRewriter.RewriteIfEnabled(Default(), url, Start, End, isTimeshift: true);

            Assert.AreEqual(url, result, "模板已经写好时间时不得改写");
        }

        [TestMethod]
        public void RewriteIfEnabled_KeepsPlayseek_InReplay()
        {
            const string url = "http://h/x?playseek=20260319205300-20260319212300";

            var result = UrlTimeRewriter.RewriteIfEnabled(Default(), url, Start, End, isTimeshift: false);

            Assert.AreEqual(url, result);
        }

        // 回源地址里完全没有时间参数时，仍然要补上 starttime/endtime（旧行为，不能丢）。
        [TestMethod]
        public void RewriteIfEnabled_InjectsTimes_WhenUrlHasNone()
        {
            const string url = "http://h/x?channel=cctv1";

            var result = UrlTimeRewriter.RewriteIfEnabled(Default(), url, Start, End, isTimeshift: true);

            StringAssert.Contains(result, "starttime=" + Start.ToString("yyyyMMddHHmmss"));
            StringAssert.Contains(result, "endtime=" + End.ToString("yyyyMMddHHmmss"));
            StringAssert.Contains(result, "channel=cctv1");
        }

        // 用户显式打开"时间覆盖"时，按其配置改写（原有能力保留）。
        [TestMethod]
        public void RewriteIfEnabled_AppliesExplicitOverride()
        {
            var settings = new PlaybackSettings();
            settings.TimeOverride.Enabled = true;
            settings.TimeOverride.Layout = "playseek";
            settings.TimeOverride.Encoding = "utc";

            var result = UrlTimeRewriter.RewriteIfEnabled(settings, "http://h/x?starttime=1&endtime=2", Start, End, isTimeshift: false);

            var expected = Start.ToUniversalTime().ToString("yyyyMMddHHmmss") + "-" + End.ToUniversalTime().ToString("yyyyMMddHHmmss");
            StringAssert.Contains(result, "playseek=" + Uri.EscapeDataString(expected));
        }

        // 关键回归（rtp2httpd）：即使用户打开了"时间覆盖"、布局是 start_end/start_duration，
        // 也绝不能把一个已经是 playseek 的回看请求改写成 starttime/endtime —— rtp2httpd 只认
        // playseek/tvdr，改写后它会当作直播请求，画面就变成"点了回看却在放直播"。
        [TestMethod]
        public void RewriteIfEnabled_KeepsPlayseek_WhenTheOverrideUsesAnotherLayout()
        {
            var settings = new PlaybackSettings();
            settings.TimeOverride.Enabled = true;
            settings.TimeOverride.Mode = "time_only";
            settings.TimeOverride.Layout = "start_end";
            settings.TimeOverride.Encoding = "local";
            const string url =
                "http://192.168.50.1:5140/rtsp/10.7.60.172/PLTV/x.smil?accountinfo=abc&playseek=20260319205300-20260319212300";

            var result = UrlTimeRewriter.RewriteIfEnabled(settings, url, Start, End, isTimeshift: false);

            Assert.AreEqual(url, result, "playseek 请求不得被改写成服务端不认识的 starttime/endtime");
        }

        [TestMethod]
        public void RewriteIfEnabled_KeepsTvdr_WhenTheOverrideUsesAnotherLayout()
        {
            var settings = new PlaybackSettings();
            settings.TimeOverride.Enabled = true;
            settings.TimeOverride.Mode = "time_only";
            settings.TimeOverride.Layout = "start_end";
            const string url = "http://h:5140/live/cctv1?tvdr=20260319205300-20260319212300";

            var result = UrlTimeRewriter.RewriteIfEnabled(settings, url, Start, End, isTimeshift: true);

            Assert.AreEqual(url, result);
        }

        // 但换成 playseek 布局时，覆盖仍然要生效（原有能力保留）。
        [TestMethod]
        public void RewriteIfEnabled_StillRewritesPlayseek_WhenTheLayoutIsPlayseek()
        {
            var settings = new PlaybackSettings();
            settings.TimeOverride.Enabled = true;
            settings.TimeOverride.Mode = "time_only";
            settings.TimeOverride.Layout = "playseek";
            settings.TimeOverride.Encoding = "utc";

            var result = UrlTimeRewriter.RewriteIfEnabled(settings, "http://h:5140/live/cctv1?playseek=1-2", Start, End, isTimeshift: false);

            var expected = Start.ToUniversalTime().ToString("yyyyMMddHHmmss") + "-" + End.ToUniversalTime().ToString("yyyyMMddHHmmss");
            StringAssert.Contains(result, "playseek=" + Uri.EscapeDataString(expected));
        }

        // 没有 playseek/tvdr 的地址仍然按配置注入时间（旧行为不能丢）。
        [TestMethod]
        public void RewriteIfEnabled_StillInjectsTimes_WhenTheUrlHasNoArchiveParam()
        {
            var settings = new PlaybackSettings();
            settings.TimeOverride.Enabled = true;
            settings.TimeOverride.Mode = "time_only";
            settings.TimeOverride.Layout = "start_end";
            settings.TimeOverride.StartKey = "starttime";
            settings.TimeOverride.EndKey = "endtime";

            var result = UrlTimeRewriter.RewriteIfEnabled(settings, "http://h/x?channel=cctv1", Start, End, isTimeshift: false);

            StringAssert.Contains(result, "starttime=" + Start.ToString("yyyyMMddHHmmss"));
            Assert.IsFalse(result.Contains("playseek="));
        }

        [TestMethod]
        public void HasArchiveParam_DetectsPlayseekAndTvdr()
        {
            Assert.IsTrue(UrlTimeRewriter.HasArchiveParam("http://h/x?a=1&playseek=1-2"));
            Assert.IsTrue(UrlTimeRewriter.HasArchiveParam("http://h/x?tvdr=1-2"));
            Assert.IsFalse(UrlTimeRewriter.HasArchiveParam("http://h/x?starttime=1&endtime=2"));
            Assert.IsFalse(UrlTimeRewriter.HasArchiveParam("http://h/x"));
        }

        [TestMethod]
        public void HasTimeParam_DetectsSeekKeys()
        {
            Assert.IsTrue(UrlTimeRewriter.HasTimeParam("http://h/x?playseek=1-2"));
            Assert.IsTrue(UrlTimeRewriter.HasTimeParam("http://h/x?a=1&endtime=20260319T132300"));
            Assert.IsFalse(UrlTimeRewriter.HasTimeParam("http://h/x?a=1&b=2"));
            Assert.IsFalse(UrlTimeRewriter.HasTimeParam("http://h/x"));
        }
    }
}
