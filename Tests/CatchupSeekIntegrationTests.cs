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
        sealed class FakeEngine : IPlayerEngine
        {
            public List<string> Played { get; } = new List<string>();
            public List<double> RelativeSeeks { get; } = new List<double>();
            public double TimePos { get; set; }

            public void Play(string url) => Played.Add(url);
            public void Stop() { }
            public void Pause(bool paused) { }
            public void SeekAbsolute(double seconds) { }
            public void SeekRelative(double seconds) => RelativeSeeks.Add(seconds);
            public void SetVolume(double volume) { }
            public void SetMute(bool muted) { }
            public void SetSpeed(double speed) { }
            public void SetAspectRatio(string ratio) { }
            public void SetDeinterlace(string mode, string? fieldParity = null, string? algorithm = null) { }
            public double? GetTimePos() => TimePos;
            public double? GetDuration() => null;
            public void EnsureReadyForLoad() { }
            public bool IsEofReached() => false;
            public void LoadWithPrefetch(string url, System.Collections.Generic.IEnumerable<string> nextUrls) => Played.Add(url);
            public bool SwitchToPrefetchedNext(string url) => false;
            public void AnchorPrefetch(string? nextUrl) { }
            public void SetPropertyString(string name, string value) { }
            public void SetRecordingMode(bool recording) { }
            public string? GetPropertyString(string name) => null;
            public double? GetPropertyDouble(string name) => null;
            public long? GetPropertyLong(string name) => null;
            public bool? GetPropertyBool(string name) => null;
        }

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

        static MainShellViewModel MakeShell(FakeEngine engine, out Channel channel)
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
            var engine = new FakeEngine();
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
            var engine = new FakeEngine();
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
            var engine = new FakeEngine();
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
            var engine = new FakeEngine();
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
            var engine = new FakeEngine();
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
    }
}
