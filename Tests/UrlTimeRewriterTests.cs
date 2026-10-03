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
