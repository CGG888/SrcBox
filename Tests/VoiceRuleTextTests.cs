using System.Collections.Generic;
using System.Linq;
using LibmpvIptvClient.Services.WebRemote.Voice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Covers the settings-field syntax for custom phrases: the text the user types has to survive a
    /// round trip and then drive the router exactly like a rule coming from the settings JSON.
    /// </summary>
    [TestClass]
    public class VoiceRuleTextTests
    {
        [TestMethod]
        public void Parse_ReadsAPhraseActionAndTarget()
        {
            var rules = VoiceRuleText.Parse("看新闻=channel:CCTV-13");

            Assert.AreEqual(1, rules.Count);
            Assert.AreEqual("contains", rules[0].Kind);
            CollectionAssert.AreEqual(new List<string> { "看新闻" }, rules[0].Phrases);
            Assert.AreEqual("channel", rules[0].Action);
            Assert.AreEqual("CCTV-13", rules[0].Target);
        }

        [TestMethod]
        public void Parse_ReadsTheKindPrefixesAndSeveralPhrases()
        {
            var rules = VoiceRuleText.Parse("!新闻频道|新闻台=channel:CCTV-13\r\n~第([一二三四五])频道=channel:$1\r\n*看点别的=exit");

            Assert.AreEqual(3, rules.Count);
            Assert.AreEqual("exact", rules[0].Kind);
            CollectionAssert.AreEqual(new List<string> { "新闻频道", "新闻台" }, rules[0].Phrases);
            Assert.AreEqual("regex", rules[1].Kind);
            Assert.AreEqual("$1", rules[1].Target);
            Assert.AreEqual("macro", rules[2].Kind);
            Assert.AreEqual("", rules[2].Target);
        }

        [TestMethod]
        public void Parse_KeepsAUrlTargetWhole()
        {
            var rules = VoiceRuleText.Parse("换列表=selectSource:http://192.168.1.9:8899/list.m3u");

            Assert.AreEqual("selectSource", rules[0].Action);
            Assert.AreEqual("http://192.168.1.9:8899/list.m3u", rules[0].Target);
        }

        [TestMethod]
        public void Parse_SkipsBlankCommentAndBrokenLines()
        {
            var rules = VoiceRuleText.Parse("\r\n# 注释\r\n// another comment\r\n没有等号\r\n=channel\r\n看新闻=\r\n看新闻=channel:CCTV-13\r\n");

            Assert.AreEqual(1, rules.Count);
            Assert.AreEqual("看新闻", rules[0].Phrases[0]);
        }

        [TestMethod]
        public void Format_RoundTripsWhatParseRead()
        {
            const string text = "看新闻=channel:CCTV-13\r\n!新闻频道|新闻台=channel:CCTV-13\r\n换列表=selectSource:http://host/list.m3u";

            Assert.AreEqual(text, VoiceRuleText.Format(VoiceRuleText.Parse(text)));
        }

        [TestMethod]
        public void Parse_EmptyTextYieldsNoRules()
        {
            Assert.AreEqual(0, VoiceRuleText.Parse(null).Count);
            Assert.AreEqual(0, VoiceRuleText.Parse("   ").Count);
        }

        [TestMethod]
        public void ParsedRule_DrivesTheRouterLikeASettingsRule()
        {
            var switched = new List<string>();
            var context = new VoiceCommandContext
            {
                Text = "帮我看看新闻",
                Lang = "zh-CN",
                Channels = () => new List<VoiceChannel> { new() { Id = "cctv13", Name = "CCTV-13 新闻", Number = 13 } },
                UserRules = () => VoiceRuleText.Parse("看新闻=channel:CCTV-13"),
                OnSwitchChannel = id => switched.Add(id)
            };

            var reply = VoiceCommandRouter.Route(context);

            Assert.IsTrue(reply.Success, reply.Spoken);
            Assert.IsTrue(reply.Executed);
            CollectionAssert.AreEqual(new List<string> { "cctv13" }, switched);
        }
    }
}
