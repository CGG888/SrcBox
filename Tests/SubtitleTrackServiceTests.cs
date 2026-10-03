using System.Collections.Generic;
using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class SubtitleTrackServiceTests
    {
        static FakePlayerEngine ThreeTrackEngine()
        {
            var engine = new FakePlayerEngine();
            engine.Properties["track-list/count"] = 3L;

            engine.Properties["track-list/0/type"] = "video";
            engine.Properties["track-list/0/id"] = 1L;

            engine.Properties["track-list/1/type"] = "sub";
            engine.Properties["track-list/1/id"] = 2L;
            engine.Properties["track-list/1/title"] = "中文";
            engine.Properties["track-list/1/lang"] = "chi";
            engine.Properties["track-list/1/codec"] = "subrip";
            engine.Properties["track-list/1/selected"] = false;

            engine.Properties["track-list/2/type"] = "sub";
            engine.Properties["track-list/2/id"] = 3L;
            engine.Properties["track-list/2/lang"] = "eng";
            engine.Properties["track-list/2/codec"] = "ass";
            engine.Properties["track-list/2/selected"] = true;
            engine.Properties["track-list/2/external"] = true;

            return engine;
        }

        [TestMethod]
        public void Read_ReturnsOnlySubtitleTracksWithTheirMetadata()
        {
            var tracks = SubtitleTrackService.Read(ThreeTrackEngine());

            Assert.AreEqual(2, tracks.Count, "只有 sub 类型的轨才算字幕轨");

            Assert.AreEqual(2, tracks[0].Id);
            Assert.AreEqual("中文", tracks[0].Title);
            Assert.AreEqual("chi", tracks[0].Language);
            Assert.AreEqual("subrip", tracks[0].Codec);
            Assert.IsFalse(tracks[0].Selected);
            Assert.IsFalse(tracks[0].External);

            Assert.AreEqual(3, tracks[1].Id);
            Assert.AreEqual("eng", tracks[1].Language);
            Assert.IsTrue(tracks[1].Selected);
            Assert.IsTrue(tracks[1].External);
        }

        [TestMethod]
        public void Read_HandlesMissingEngineAndMissingProperty()
        {
            Assert.AreEqual(0, SubtitleTrackService.Read(null).Count);
            Assert.AreEqual(0, SubtitleTrackService.Read(new FakePlayerEngine()).Count, "没有 track-list 时不应抛异常");
        }

        [TestMethod]
        public void Select_WritesSidOrTurnsSubtitlesOff()
        {
            var engine = new FakePlayerEngine();

            SubtitleTrackService.Select(engine, 5);
            Assert.AreEqual("5", engine.SetProperties["sid"]);

            SubtitleTrackService.Select(engine, null);
            Assert.AreEqual("no", engine.SetProperties["sid"]);

            SubtitleTrackService.Select(null, 5);   // 不应抛异常
        }

        [TestMethod]
        public void Describe_CombinesTheAvailableFields()
        {
            Assert.AreEqual("中文 · chi · subrip",
                SubtitleTrackService.Describe(new SubtitleTrackInfo(2, "中文", "chi", "subrip", false, false)));

            Assert.AreEqual("eng", SubtitleTrackService.Describe(new SubtitleTrackInfo(3, "", "eng", "", false, false)));

            Assert.AreEqual("#7", SubtitleTrackService.Describe(new SubtitleTrackInfo(7, "  ", "", null!, false, false)));
        }
    }
}
