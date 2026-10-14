using System.Linq;
using LibmpvIptvClient.Services.WebRemote.Voice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The grammar pack: the file a user (or the updater) drops in to change the words without a new build.
    /// </summary>
    [TestClass]
    public class VoiceGrammarTests
    {
        private const string SmallPack = """
        {
          "schema": 1,
          "lang": "zh-CN",
          "version": "2026.10.14",
          "notes": "two extra spellings",
          "noiseWords": [ "回看", "节目" ],
          "phrases": [
            { "name": "switch_channel", "verb": "SwitchChannel", "pattern": "(?<num>[0-9]{1,3})" }
          ]
        }
        """;

        [TestMethod]
        public void Parse_ThenToJson_KeepsThePack()
        {
            var pack = VoiceGrammarPack.Parse(SmallPack, out var error);
            Assert.IsNotNull(pack, error);
            Assert.AreEqual(0, pack!.Validate().Count);

            var again = VoiceGrammarPack.Parse(pack.ToJson(), out var againError);
            Assert.IsNotNull(again, againError);
            Assert.AreEqual("2026.10.14", again!.Version);
            Assert.AreEqual(2, again.NoiseWords.Count);
            Assert.AreEqual(1, again.Phrases.Count);
            Assert.AreEqual("(?<num>[0-9]{1,3})", again.Phrases[0].Pattern);
        }

        [TestMethod]
        public void Parse_RejectsAnEmptyFile()
        {
            Assert.IsNull(VoiceGrammarPack.Parse("   ", out var error));
            Assert.AreEqual("empty", error);
        }

        [TestMethod]
        public void Parse_RejectsATooLargePack()
        {
            var huge = new string('x', VoiceGrammarPack.MaxBytes + 1);
            Assert.IsNull(VoiceGrammarPack.Parse(huge, out var error));
            Assert.AreEqual("too large", error);
        }

        [TestMethod]
        public void Validate_RejectsAForeignSchema()
        {
            var pack = Fine();
            pack.Schema = VoiceGrammarPack.CurrentSchema + 1;
            CollectionAssert.Contains(pack.Validate().ToList(), $"schema {pack.Schema} is not {VoiceGrammarPack.CurrentSchema}");
        }

        [TestMethod]
        public void Validate_RejectsAnUnknownVerb()
        {
            var pack = Fine();
            pack.Phrases[0].Verb = "Teleport";
            Assert.IsTrue(pack.Validate().Any(p => p.Contains("unknown verb Teleport")));
        }

        [TestMethod]
        public void Validate_RejectsAPatternThatDoesNotCompile()
        {
            var pack = Fine();
            pack.Phrases[0].Pattern = "([0-9";
            Assert.IsTrue(pack.Validate().Any(p => p.Contains("switch_channel")));
        }

        [TestMethod]
        public void Validate_RejectsADuplicatePhraseName()
        {
            var pack = Fine();
            pack.Phrases.Add(new VoiceGrammarPhrase
            {
                Name = pack.Phrases[0].Name,
                Verb = "SwitchChannel",
                Pattern = "(?<num>[0-9]{2})"
            });
            Assert.IsTrue(pack.Validate().Any(p => p.Contains("duplicate phrase")));
        }

        [TestMethod]
        public void Validate_RejectsImpossibleHours()
        {
            var pack = Fine();
            pack.TimeWords = new VoiceGrammarTimeTable
            {
                Parts = { new VoiceGrammarPart { Words = { "凌晨" }, From = 6, To = 0, Label = "凌晨" } }
            };
            Assert.IsTrue(pack.Validate().Any(p => p.Contains("part 凌晨 has the hours 6-0")));
        }

        [TestMethod]
        public void Validate_RejectsAWeekdayThatIsNotADayOfWeek()
        {
            var pack = Fine();
            pack.TimeWords = new VoiceGrammarTimeTable
            {
                Weekdays = { new VoiceGrammarWeekday { Words = { "星期一" }, Day = 7 } }
            };
            Assert.IsTrue(pack.Validate().Any(p => p.Contains("weekday 7")));
        }

        [TestMethod]
        public void Merge_KeepsTheBuiltInTablesThePackLeavesOut()
        {
            var pack = Fine();
            var merged = VoiceGrammar.Merge(VoiceGrammar.Default, pack, "test.json");

            // The pack carries noise words and phrases only; the day, week and part-of-day tables must
            // still come from the built-in grammar, otherwise a small delta pack would blank the parser.
            Assert.AreEqual(pack.NoiseWords.Count, merged.NoiseWords.Count);
            Assert.AreEqual(1, merged.Patterns.Count);
            Assert.AreEqual(VoiceGrammar.Default.Day.Count, merged.Day.Count);
            Assert.AreEqual(VoiceGrammar.Default.Weekdays.Count, merged.Weekdays.Count);
            Assert.AreEqual(VoiceGrammar.Default.Parts.Count, merged.Parts.Count);
            Assert.AreEqual(VoiceGrammar.Default.PartWords.Count, merged.PartWords.Count);
            Assert.AreEqual("2026.10.14", merged.Version);
            Assert.AreEqual("zh-CN", merged.Lang);
            Assert.AreEqual("test.json", merged.Source);
            Assert.IsFalse(merged.IsBuiltIn);
        }

        [TestMethod]
        public void Merge_SkipsAPhraseWhoseVerbIsUnknown()
        {
            var pack = Fine();
            pack.Phrases[0].Verb = "Teleport";
            var merged = VoiceGrammar.Merge(VoiceGrammar.Default, pack, "test.json");

            // Nothing compiles, so the table is empty; an installed pack is validated before it gets here,
            // and this is the second line of defence.
            Assert.AreEqual(0, merged.Patterns.Count);
        }

        private static VoiceGrammarPack Fine()
        {
            var pack = VoiceGrammarPack.Parse(SmallPack, out var error);
            Assert.IsNotNull(pack, error);
            Assert.AreEqual(0, pack!.Validate().Count, "the fixture must be valid");
            return pack;
        }
    }
}
