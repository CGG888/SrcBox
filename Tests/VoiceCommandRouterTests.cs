using System;
using System.Collections.Generic;
using System.Linq;
using LibmpvIptvClient.Services.WebRemote.Voice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Covers one full spoken sentence at a time: the router must either run the matching callback or
    /// come back with the candidates the page should offer, never guess between two equal readings.
    /// </summary>
    [TestClass]
    public class VoiceCommandRouterTests
    {
        private static readonly DateTime Today = DateTime.Today;

        private static List<VoiceChannel> Channels() => new()
        {
            new VoiceChannel { Id = "cctv1", Name = "CCTV-1 综合", Number = 1, Group = "央视" },
            new VoiceChannel { Id = "cctv2", Name = "CCTV-2 财经", Number = 2, Group = "央视" },
            new VoiceChannel { Id = "cctv5", Name = "CCTV-5 体育", Number = 5, Group = "央视" },
            new VoiceChannel { Id = "hunan", Name = "湖南卫视", Number = 7, Group = "卫视" },
            new VoiceChannel { Id = "dongfang", Name = "东方卫视", Number = 8, Group = "卫视" }
        };

        private sealed class Player
        {
            public readonly List<string> Switched = new();
            public readonly List<string> Loaded = new();
            public readonly List<string> SelectedStreams = new();
            public readonly List<int> StreamSteps = new();
            public readonly List<(string Channel, string Title, string Start, string End)> Replays = new();
            public readonly List<double> Seeks = new();
            public readonly List<string> Spoken = new();

            public bool Muted;
            public double Volume = 50;
            public string CurrentId = "cctv1";
            public int Plays, Pauses, Stops, Fullscreens, Exits;
            public List<VoiceStream> Streams = new();
            public List<VoicePlaylist> Playlists = new();
            public List<VoiceRule> Rules = new();
            public VoiceProgram? Now;
            public Dictionary<string, List<VoiceProgram>> Catchup = new();

            public VoiceCommandContext Context(string text, string lang = "zh-CN", bool dryRun = false)
            {
                var theChannels = Channels();
                return new VoiceCommandContext
                {
                    Text = text,
                    Lang = lang,
                    DryRun = dryRun,
                    Channels = () => theChannels,
                    CurrentStreams = () => Streams,
                    Playlists = () => Playlists,
                    CurrentChannel = () => theChannels.FirstOrDefault(c => c.Id == CurrentId),
                    IsMuted = () => Muted,
                    Volume = () => Volume,
                    EpgNow = _ => Now,
                    CatchupPrograms = (id, start, end) => Catchup.TryGetValue(id, out var list)
                        ? list.Where(p => p.Start >= start && p.Start < end).ToList()
                        : new List<VoiceProgram>(),
                    UserRules = () => Rules,
                    OnSwitchChannel = id => Switched.Add(id),
                    OnSelectedStream = name => SelectedStreams.Add(name),
                    OnNextStream = delta => StreamSteps.Add(delta),
                    OnLoadPlaylist = id => Loaded.Add(id),
                    OnReplayProgram = (c, t, s, e) => Replays.Add((c, t, s, e)),
                    OnVolume = v => Volume = v,
                    OnToggleMute = () => Muted = !Muted,
                    OnSeek = s => Seeks.Add(s),
                    OnPlay = () => Plays++,
                    OnPause = () => Pauses++,
                    OnStop = () => Stops++,
                    OnFullscreen = () => Fullscreens++,
                    OnExit = () => Exits++
                };
            }

            public VoiceCommandReply Say(string text, string lang = "zh-CN", bool dryRun = false)
            {
                var reply = VoiceCommandRouter.Route(Context(text, lang, dryRun));
                Spoken.Add(reply.Spoken);
                return reply;
            }
        }

        // ------------------------------------------------------------ channels

        [DataTestMethod]
        [DataRow("换到1台", "cctv1")]
        [DataRow("切换到2台", "cctv2")]
        [DataRow("换到5频道", "cctv5")]
        [DataRow("切到 CCTV-1", "cctv1")]   // the compacted sentence still carries the space-free name
        [DataRow("看湖南卫视", "hunan")]
        [DataRow("调到东方卫视", "dongfang")]
        [DataRow("换到五台", "cctv5")]      // spoken number
        public void Route_ChannelSentence_SwitchesToTheChannel(string text, string expected)
        {
            var player = new Player();

            var reply = player.Say(text);

            Assert.IsTrue(reply.Success, $"not understood: {text}");
            Assert.IsTrue(reply.Executed, $"did not execute: {text} -> {reply.Spoken}");
            CollectionAssert.AreEqual(new[] { expected }, player.Switched);
        }

        [TestMethod]
        public void Route_NextAndPreviousChannel_StepThroughTheList()
        {
            var player = new Player { CurrentId = "cctv2" };

            player.Say("下一个频道");
            player.Say("上一个频道");

            CollectionAssert.AreEqual(new[] { "cctv5", "cctv1" }, player.Switched);
        }

        [TestMethod]
        public void Route_FirstAndLastChannel_JumpToTheEnds()
        {
            var player = new Player { CurrentId = "hunan" };

            player.Say("第一个频道");
            player.Say("最后一个频道");

            CollectionAssert.AreEqual(new[] { "cctv1", "dongfang" }, player.Switched);
        }

        [TestMethod]
        public void Route_AmbiguousChannelName_AsksInsteadOfGuessing()
        {
            var player = new Player();

            var reply = player.Say("看卫视");

            Assert.IsFalse(reply.Executed, "an ambiguous name must never switch by itself");
            Assert.AreEqual(0, player.Switched.Count);
            Assert.AreEqual(2, reply.Options.Count, "expected the two 卫视 channels");
            Assert.IsTrue(reply.Options.All(o => o.Action == "channel"));
            Assert.IsTrue(reply.Options.All(o => o.Payload.ContainsKey("channelId")));
        }

        [TestMethod]
        public void Route_UnknownChannel_ReportsInsteadOfSwitching()
        {
            var player = new Player();

            var reply = player.Say("换到不存在的台");

            Assert.IsFalse(reply.Executed);
            Assert.AreEqual(0, player.Switched.Count);
            Assert.IsTrue(reply.Spoken.Length > 0);
        }

        // ------------------------------------------------------------ stream ("源") and playlist

        [TestMethod]
        public void Route_ChangeSource_WalksTheCurrentChannelStreams()
        {
            var player = new Player();

            var reply = player.Say("换个源");

            Assert.IsTrue(reply.Executed);
            CollectionAssert.AreEqual(new[] { 1 }, player.StreamSteps);
            CollectionAssert.AreEqual(Array.Empty<string>(), player.Switched);
        }

        [TestMethod]
        public void Route_NamedStream_SelectsThatStream()
        {
            var player = new Player
            {
                Streams = new List<VoiceStream>
                {
                    new VoiceStream { Id = "1", Name = "线路一" },
                    new VoiceStream { Id = "2", Name = "线路二" }
                }
            };

            var reply = player.Say("换到线路二");

            Assert.IsTrue(reply.Executed);
            CollectionAssert.AreEqual(new[] { "线路二" }, player.SelectedStreams);
        }

        [TestMethod]
        public void Route_PlaylistSentence_LoadsThePlaylistNotTheStream()
        {
            var player = new Player
            {
                Playlists = new List<VoicePlaylist> { new VoicePlaylist { Id = "http://a/x.m3u", Name = "移动源" } }
            };

            var reply = player.Say("切换到移动源播放列表");

            Assert.IsTrue(reply.Executed);
            CollectionAssert.AreEqual(new[] { "http://a/x.m3u" }, player.Loaded);
            CollectionAssert.AreEqual(Array.Empty<int>(), player.StreamSteps);
        }

        // ------------------------------------------------------------ player transport

        [DataTestMethod]
        [DataRow("暂停", "pause")]
        [DataRow("继续播放", "play")]
        [DataRow("停止", "stop")]
        [DataRow("静音", "mute")]
        [DataRow("全屏", "fullscreen")]
        public void Route_TransportSentence_CallsTheMatchingCallback(string text, string expected)
        {
            var player = new Player();

            var reply = player.Say(text);

            Assert.IsTrue(reply.Executed, $"did not execute: {text}");
            switch (expected)
            {
                case "pause": Assert.AreEqual(1, player.Pauses); break;
                case "play": Assert.AreEqual(1, player.Plays); break;
                case "stop": Assert.AreEqual(1, player.Stops); break;
                case "mute": Assert.IsTrue(player.Muted); break;
                case "fullscreen": Assert.AreEqual(1, player.Fullscreens); break;
            }
        }

        [TestMethod]
        public void Route_VolumeSentence_SetsTheLevelAndSeeks()
        {
            var player = new Player();

            player.Say("音量调到 35");
            Assert.AreEqual(35, player.Volume, 0.001);

            player.Say("快进30秒");
            CollectionAssert.AreEqual(new[] { 30d }, player.Seeks);
        }

        // ------------------------------------------------------------ EPG

        [TestMethod]
        public void Route_WhatIsOnNow_ReadsTheCurrentProgramme()
        {
            var player = new Player
            {
                Now = new VoiceProgram { Title = "新闻联播", Start = Today.AddHours(19), End = Today.AddHours(19.5) }
            };

            var reply = player.Say("现在在播什么");

            Assert.IsTrue(reply.Success);
            StringAssert.Contains(reply.Spoken, "新闻联播");
        }

        [TestMethod]
        public void Route_ReplayWithoutTitleAcrossChannels_OffersEveryProgramme()
        {
            var player = new Player
            {
                Catchup = new Dictionary<string, List<VoiceProgram>>
                {
                    ["cctv1"] = new() { new VoiceProgram { Title = "新闻联播", Start = Today.AddHours(19) } },
                    ["hunan"] = new() { new VoiceProgram { Title = "快乐大本营", Start = Today.AddHours(20) } }
                }
            };

            var reply = player.Say("回看今天的节目");

            Assert.IsFalse(reply.Executed, "two channels matched, so the user has to choose");
            Assert.AreEqual(2, reply.Options.Count);
            Assert.IsTrue(reply.Options.All(o => o.Action == "replayProgram"));
            Assert.IsTrue(reply.Options.All(o => o.Payload.ContainsKey("start") && o.Payload.ContainsKey("end")));
            // Newest first, so the top row is the programme that just started.
            Assert.AreEqual("快乐大本营", reply.Options[0].Title);
        }

        [TestMethod]
        public void Route_ReplayWithUniqueTitle_StartsItDirectly()
        {
            var player = new Player
            {
                Catchup = new Dictionary<string, List<VoiceProgram>>
                {
                    ["cctv1"] = new() { new VoiceProgram { Title = "新闻联播", Start = Today.AddHours(19) } },
                    ["hunan"] = new() { new VoiceProgram { Title = "快乐大本营", Start = Today.AddHours(20) } }
                }
            };

            var reply = player.Say("回看今天的快乐大本营");

            Assert.IsTrue(reply.Executed, $"expected a direct replay, got: {reply.Spoken}");
            Assert.AreEqual(1, player.Replays.Count);
            Assert.AreEqual("hunan", player.Replays[0].Channel);
        }

        [TestMethod]
        public void Route_ReplayNamedChannel_SearchesOnlyThatChannel()
        {
            var player = new Player
            {
                Catchup = new Dictionary<string, List<VoiceProgram>>
                {
                    ["cctv1"] = new() { new VoiceProgram { Title = "新闻联播", Start = Today.AddHours(19) } },
                    ["hunan"] = new() { new VoiceProgram { Title = "快乐大本营", Start = Today.AddHours(20) } }
                }
            };

            var reply = player.Say("在 CCTV-1 回看今天的节目");

            Assert.AreEqual(1, reply.Options.Count, "only the named channel may answer");
            Assert.AreEqual("新闻联播", reply.Options[0].Title);
            Assert.AreEqual("cctv1", reply.Options[0].ChannelId);
        }

        [TestMethod]
        public void Route_ReplayWithNoChannelData_AsksForAChannel()
        {
            var reply = VoiceCommandRouter.Route(new VoiceCommandContext { Text = "回看今天的节目", Lang = "zh-CN" });

            Assert.IsFalse(reply.Executed);
            Assert.IsTrue(reply.Spoken.Length > 0);
        }

        // ------------------------------------------------------------ user rules and fallbacks

        [TestMethod]
        public void Route_UserRule_WinsOverTheBuiltInTable()
        {
            var player = new Player
            {
                Rules = new List<VoiceRule>
                {
                    new VoiceRule { Kind = "contains", Phrases = new List<string> { "看新闻" }, Action = "channel", Target = "CCTV-2 财经" }
                }
            };

            var reply = player.Say("给我看新闻");

            Assert.IsTrue(reply.Executed);
            CollectionAssert.AreEqual(new[] { "cctv2" }, player.Switched);
        }

        [TestMethod]
        public void Route_NotUnderstood_SaysSoInsteadOfActing()
        {
            var player = new Player();

            var reply = player.Say("嗯嗯嗯");

            Assert.IsFalse(reply.Success);
            Assert.IsFalse(reply.Executed);
            Assert.IsTrue(reply.Spoken.Length > 0);
            Assert.AreEqual(0, player.Switched.Count);
        }

        [TestMethod]
        public void Route_DryRun_ReturnsTheActionWithoutTouchingThePlayer()
        {
            var player = new Player();

            var reply = player.Say("换到2台", dryRun: true);

            Assert.IsTrue(reply.Success);
            Assert.IsFalse(reply.Executed, "dry run must not execute");
            Assert.AreEqual(0, player.Switched.Count);
            Assert.AreEqual(1, reply.Options.Count);
            Assert.AreEqual("cctv2", reply.Options[0].Payload["channelId"]);
        }

        // ------------------------------------------------------------ Execute (shortcut / endpoint form)

        [TestMethod]
        public void Execute_ActionWithoutNaturalLanguage_RunsTheSamePath()
        {
            var player = new Player();

            var reply = VoiceCommandRouter.Execute(player.Context(""), "channel", "cctv5");

            Assert.IsTrue(reply.Executed);
            CollectionAssert.AreEqual(new[] { "cctv5" }, player.Switched);
        }

        [TestMethod]
        public void Execute_UnknownAction_ReportsFailure()
        {
            var player = new Player();

            var reply = VoiceCommandRouter.Execute(player.Context(""), "nonsense", "x");

            Assert.IsFalse(reply.Success);
            Assert.AreEqual(0, player.Switched.Count);
        }

        [TestMethod]
        public void Route_ReplyCarriesTheTransportMarker()
        {
            var player = new Player();

            var reply = player.Say("换到1台");

            Assert.AreEqual("voice", reply.Type, "the page routes messages on this marker");
        }
    }
}
