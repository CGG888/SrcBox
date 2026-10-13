using System;
using System.Collections.Generic;
using LibmpvIptvClient.Architecture.Application.Player;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// End-to-end (in-process) checks for catchup/timeshift seeking: the shell is driven through the
    /// real shortcut actions and a fake engine records the URL that would be handed to mpv.
    /// </summary>
    [TestClass]
    public class CatchupSeekIntegrationTests
    {
        static readonly DateTime ProgStart = new DateTime(2026, 3, 19, 20, 0, 0, DateTimeKind.Local);
        static readonly DateTime ProgEnd = new DateTime(2026, 3, 19, 21, 0, 0, DateTimeKind.Local);

        const string CatchupTemplate =
            "http://h/x.m3u8?starttime=${(b)yyyyMMdd|UTC}T${(b)HHmmss|UTC}&endtime=${(e)yyyyMMdd|UTC}T${(e)HHmmss|UTC}";

        static Channel MakeChannel() => new Channel
        {
            Id = "cctv1",
            TvgId = "cctv1",
            Name = "CCTV1",
            CatchupSource = CatchupTemplate
        };

        static MainShellViewModel MakeShell(FakePlayerEngine engine, out Channel channel)
        {
            var shell = new MainShellViewModel();
            shell.InjectServices(new EpgService(), engine, null!);
            channel = MakeChannel();
            shell.CurrentChannel = channel;
            shell.CurrentUrl = "http://h/live.m3u8";
            return shell;
        }

        static string ExpectedUtc(DateTime local) =>
            local.ToUniversalTime().ToString("yyyyMMdd") + "T" + local.ToUniversalTime().ToString("HHmmss");

        // 回看：按一次 ← 必须用新的起始时间重新请求回看地址（而不是对 mpv 发相对 seek）。
        [TestMethod]
        public void ReplaySeek_ReRequestsUrlShiftedByTenSeconds()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeShell(engine, out _);
            shell.CurrentPlayingProgram = new EpgProgram { Title = "P", Start = ProgStart, End = ProgEnd };
            shell.PlaybackMode = PlaybackMode.Replay;
            shell.PlaybackFocusTime = ProgStart;
            shell.HandlePlaybackTick(600, null, false);   // 已经播了 10 分钟

            shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.SeekBackward);

            Assert.AreEqual(1, engine.Played.Count, "回看 seek 必须重新请求 URL");
            Assert.AreEqual(0, engine.RelativeSeeks.Count, "回看 seek 不应退化为 mpv 相对 seek");
            StringAssert.Contains(engine.Played[0], "starttime=" + ExpectedUtc(ProgStart.AddSeconds(590)));
        }

        // 时移：按一次 ← 同样要重发 URL，并且不能把时移状态清掉。
        [TestMethod]
        public void TimeshiftSeek_KeepsTimeshiftActiveAndReRequestsUrl()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeShell(engine, out _);
            shell.TimeshiftMin = DateTime.Now.AddHours(-2);
            shell.TimeshiftMax = DateTime.Now;
            shell.TimeshiftCursorSec = 3600;

            shell.IsTimeshiftActive = true;
            Assert.IsTrue(shell.IsTimeshiftActive, "有引擎+回看源时应当允许开启时移");

            // 进入时移会自己起播一次并重算区间，这里以重算后的状态为基准
            var cursorBefore = shell.TimeshiftCursorSec;
            var expected = shell.TimeshiftMin.AddSeconds(Math.Max(0, cursorBefore - 10));
            engine.Played.Clear();

            shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.SeekBackward);

            Assert.IsTrue(shell.IsTimeshiftActive, "时移 seek 之后必须仍在时移模式");
            Assert.AreEqual(1, engine.Played.Count, "时移 seek 必须重新请求 URL");
            StringAssert.Contains(engine.Played[0], "starttime=" + ExpectedUtc(expected));
        }

        // 进入时移必须真正切到回看流（起播一次），否则界面上是时移、画面还是直播。
        [TestMethod]
        public void EnteringTimeshift_StartsArchivePlayback()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeShell(engine, out _);

            shell.IsTimeshiftActive = true;

            Assert.AreEqual(1, engine.Played.Count, "进入时移应当起播时移流");
            StringAssert.Contains(engine.Played[0], "starttime=");
            Assert.IsTrue(shell.IsTimeshiftActive, "起播时移流后仍应处于时移模式");
        }

        // 回看进度条：重发 URL 后 mpv 的 time-pos 从新起点归零，界面必须按"节目区间"显示——
        // 左端（已播）在节目内移动，右端（总长）保持该节目总时长不变。
        [TestMethod]
        public void ReplayTick_ShowsProgramRelativePosition_AndKeepsProgramLength()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeShell(engine, out _);
            shell.CurrentPlayingProgram = new EpgProgram { Title = "P", Start = ProgStart, End = ProgEnd };
            shell.PlaybackMode = PlaybackMode.Replay;
            shell.PlaybackFocusTime = ProgStart.AddMinutes(10);   // 已 seek 到节目内第 10 分钟
            shell.HandlePlaybackTick(120, 2400, false);           // 又播 2 分钟；mpv 报的总长只剩 40 分钟

            Assert.AreEqual(3600, shell.SeekMaximum, 0.01, "右端应为整个节目的时长");
            Assert.AreEqual(720, shell.SeekValue, 0.01, "左端应为节目内位置（10+2 分钟）");
            Assert.AreEqual("12:00", shell.ElapsedTimeText);
            Assert.AreEqual("1:00:00", shell.DurationText);
        }

        // seek 之后界面位置要跟着走，不能回零。
        [TestMethod]
        public void ReplaySeek_MovesDisplayedPositionForward()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeShell(engine, out _);
            shell.CurrentPlayingProgram = new EpgProgram { Title = "P", Start = ProgStart, End = ProgEnd };
            shell.PlaybackMode = PlaybackMode.Replay;
            shell.PlaybackFocusTime = ProgStart;
            shell.HandlePlaybackTick(600, null, false);
            Assert.AreEqual(600, shell.SeekValue, 0.01);

            shell.ShortcutActions.ExecuteAction(MainWindowShortcutAction.SeekForward);

            Assert.AreEqual(1, engine.Played.Count);
            StringAssert.Contains(engine.Played[0], "starttime=" + ExpectedUtc(ProgStart.AddSeconds(610)));

            shell.HandlePlaybackTick(0, null, false);   // 新流从 610s 处开始
            Assert.AreEqual(610, shell.SeekValue, 0.01, "seek 后界面位置应前进而不是回零");
        }

        // 两个进度条（窗口底栏 / 全屏悬浮条）都把量程值交给 SeekToMeterValue：回看时量程是"节目相对"
        // 秒数，必须换算回墙钟再重发回看地址。全屏悬浮条曾经把这个秒数直接交给 mpv 相对 seek。
        [TestMethod]
        public void ReplayMeterSeek_ConvertsProgrammeRelativeSecondsBackToWallClock()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeShell(engine, out _);
            shell.CurrentPlayingProgram = new EpgProgram { Title = "P", Start = ProgStart, End = ProgEnd };
            shell.PlaybackMode = PlaybackMode.Replay;
            shell.PlaybackFocusTime = ProgStart.AddMinutes(10);   // 已 seek 到节目内第 10 分钟
            engine.Played.Clear();

            shell.ChannelPlaybackActions.SeekToMeterValue(15 * 60);   // 量程上第 15 分钟 = 节目内 15:00

            Assert.AreEqual(1, engine.Played.Count, "回看量程 seek 必须重发回看地址");
            Assert.AreEqual(0, engine.RelativeSeeks.Count, "回看量程 seek 不得退化为 mpv 相对 seek");
            StringAssert.Contains(engine.Played[0], "starttime=" + ExpectedUtc(ProgStart.AddMinutes(15)));
        }

        // 时移量程同样来自进度条，但量程右端是已缓存窗口的末端：拖过它不能请求尚未产生的时段。
        [TestMethod]
        public void TimeshiftMeterSeek_ClampsToTheBufferedWindow()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeShell(engine, out _);
            shell.IsTimeshiftActive = true;
            Assert.IsTrue(shell.IsTimeshiftActive, "有引擎+回看源时应当允许开启时移");
            Assert.IsTrue(shell.TimeshiftMax > shell.TimeshiftMin, "进入时移后应有缓存窗口");
            engine.Played.Clear();

            var bufferedSeconds = (shell.TimeshiftMax - shell.TimeshiftMin).TotalSeconds;

            shell.ChannelPlaybackActions.SeekToMeterValue(bufferedSeconds + 600);   // 拖过右端 10 分钟

            Assert.AreEqual(1, engine.Played.Count);
            StringAssert.Contains(engine.Played[0], "starttime=" + ExpectedUtc(shell.TimeshiftMax),
                "越过缓存右端的拖动必须落在右端: " + engine.Played[0]);
        }

        // ---------------- rtp2httpd (stackia/rtp2httpd) ----------------        // 直播地址是 rtp2httpd 的 /rtsp/<上游>:554/... 代理地址，catchup-source 是"纯参数段
        // playseek"，只有把它拼到直播地址上，rtp2httpd 才会返回回看流（否则退回直播）。

        const string Rtp2httpdLive =
            "http://192.168.50.1:5140/rtsp/10.7.60.172/PLTV/88888888/224/3221225688/723212.smil?rrsip=10.7.227.140&accountinfo=abc";

        static Channel MakeRtp2httpdChannel()
        {
            var channel = new Channel
            {
                Id = "cctv1",
                TvgId = "cctv1",
                Name = "CCTV1",
                Catchup = "append",
                CatchupSource = "?playseek=${(b)yyyyMMddHHmmss}-${(e)yyyyMMddHHmmss}"
            };
            channel.Sources.Add(new Source { Id = "live", Name = "live", Url = Rtp2httpdLive });
            return channel;
        }

        static MainShellViewModel MakeRtp2httpdShell(FakePlayerEngine engine)
        {
            var shell = new MainShellViewModel();
            shell.InjectServices(new EpgService(), engine, null!);
            var channel = MakeRtp2httpdChannel();
            shell.CurrentChannel = channel;
            shell.CurrentUrl = Rtp2httpdLive;
            return shell;
        }

        static void AssertPlayseekUrl(string url, string expectedValuePrefix)
        {
            Assert.IsTrue(url.StartsWith(Rtp2httpdLive + "&playseek=", StringComparison.Ordinal),
                "必须把 playseek 拼到直播地址上: " + url);
            StringAssert.StartsWith(url.Substring((Rtp2httpdLive + "&playseek=").Length), expectedValuePrefix);
        }

        // 回看：点节目单里的过去节目 → 交给 mpv 的必须是"直播地址 + playseek"。
        [TestMethod]
        public void Rtp2httpdReplay_ClickingAPastProgram_RequestsPlayseekOnTheLiveUrl()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeRtp2httpdShell(engine);
            var program = new EpgProgram { Title = "P", Start = ProgStart, End = ProgEnd };

            shell.ChannelPlaybackActions.PlayCatchup(shell.CurrentChannel, program);

            Assert.AreEqual(1, engine.Played.Count);
            AssertPlayseekUrl(engine.Played[0], ProgStart.ToString("yyyyMMddHHmmss") + "-");
        }

        // 时移：进入时移必须发出 playseek 请求（同一地址换时间段，rtp2httpd 就是这么做的）。
        [TestMethod]
        public void Rtp2httpdTimeshift_EnteringTimeshift_RequestsPlayseek()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeRtp2httpdShell(engine);

            shell.IsTimeshiftActive = true;

            Assert.AreEqual(1, engine.Played.Count, "进入时移应当起播时移流");
            StringAssert.Contains(engine.Played[0], "&playseek=");
            StringAssert.Contains(engine.Played[0], Rtp2httpdLive);
            Assert.IsFalse(engine.Played[0].Contains("starttime="), "rtp2httpd 不认 starttime: " + engine.Played[0]);
        }

        // 频道完全没有 catchup 标签时，rtp2httpd 地址要靠自动回退生成 playseek。
        // 没有 UA 的 TZ/UTC+N 标记时时间必须用 UTC 表达（rtp2httpd 默认按 UTC 解析）。
        [TestMethod]
        public void Rtp2httpdTimeshift_WithoutAnyCatchupTag_FallsBackToUtcPlayseek()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeRtp2httpdShell(engine);
            var channel = shell.CurrentChannel;
            channel.CatchupSource = "";
            var headers = AppSettings.Current.HttpHeaders;
            var zone = headers.Rtp2httpdTimezoneEnabled;
            try
            {
                headers.Rtp2httpdTimezoneEnabled = false;
                shell.ChannelPlaybackActions.PlayCatchupAt(channel, ProgStart);
            }
            finally
            {
                headers.Rtp2httpdTimezoneEnabled = zone;
            }

            Assert.AreEqual(1, engine.Played.Count, "没有任何模板时也应当能回看 rtp2httpd 地址");
            AssertPlayseekUrl(engine.Played[0], ProgStart.ToUniversalTime().ToString("yyyyMMddHHmmss") + "-");
        }

        // 打开 UA 的 TZ/UTC+N 标记后，可以（也应该）直接发本地时间，由 rtp2httpd 按标记换算。
        [TestMethod]
        public void Rtp2httpdTimeshift_WithTimeZoneMarker_FallsBackToLocalPlayseek()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeRtp2httpdShell(engine);
            var channel = shell.CurrentChannel;
            channel.CatchupSource = "";
            var headers = AppSettings.Current.HttpHeaders;
            var zone = headers.Rtp2httpdTimezoneEnabled;
            try
            {
                headers.Rtp2httpdTimezoneEnabled = true;
                shell.ChannelPlaybackActions.PlayCatchupAt(channel, ProgStart);
            }
            finally
            {
                headers.Rtp2httpdTimezoneEnabled = zone;
            }

            Assert.AreEqual(1, engine.Played.Count);
            AssertPlayseekUrl(engine.Played[0], ProgStart.ToString("yyyyMMddHHmmss") + "-");
        }

        // 关键回归：用户打开"时间覆盖"（start_end 布局）后，playseek 请求不得被改写成
        // starttime/endtime —— 否则 rtp2httpd 会静默回落到直播。
        [TestMethod]
        public void Rtp2httpdTimeshift_WithTimeOverrideEnabled_KeepsPlayseek()
        {
            var engine = new FakePlayerEngine();
            var shell = MakeRtp2httpdShell(engine);
            var cfg = AppSettings.Current.TimeOverride;
            var oldEnabled = cfg.Enabled;
            var oldMode = cfg.Mode;
            var oldLayout = cfg.Layout;
            var oldEncoding = cfg.Encoding;
            try
            {
                cfg.Enabled = true;
                cfg.Mode = "time_only";
                cfg.Layout = "start_end";
                cfg.Encoding = "local";

                shell.ChannelPlaybackActions.PlayCatchupAt(shell.CurrentChannel, ProgStart);
            }
            finally
            {
                cfg.Enabled = oldEnabled;
                cfg.Mode = oldMode;
                cfg.Layout = oldLayout;
                cfg.Encoding = oldEncoding;
            }

            Assert.AreEqual(1, engine.Played.Count);
            AssertPlayseekUrl(engine.Played[0], ProgStart.ToString("yyyyMMddHHmmss") + "-");
            Assert.IsFalse(engine.Played[0].Contains("starttime="),
                "时间覆盖不得把 playseek 改写成 starttime: " + engine.Played[0]);
        }
    }
}
