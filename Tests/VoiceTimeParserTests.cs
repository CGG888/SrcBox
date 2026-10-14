using System;
using LibmpvIptvClient.Services.WebRemote.Voice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// "回看" + a day + a clock time is the combination that used to fail: 晚上八点 was read as 08:00, the
    /// spoken hour was treated as a 90 minute range ("20点的新闻联播" starts at 19:00), and none of the
    /// calendar dates a user really says were understood at all.
    /// </summary>
    [TestClass]
    public class VoiceTimeParserTests
    {
        /// <summary>A Wednesday morning, so "今天/昨天/上周五" all have a stable answer.</summary>
        private static readonly DateTime Now = new(2026, 10, 14, 9, 47, 0);

        private static VoiceTimeWindow Parse(string text) => VoiceTimeParser.Parse(text, "zh-CN", Now);

        [TestMethod]
        public void Parse_SpokenHour_OpensAnHourBeforeIt()
        {
            var window = Parse("回看20点的新闻联播");

            Assert.AreEqual(Now.Date.AddHours(19), window.Start, "20点 starts a programme that began at 19:00");
            Assert.AreEqual(Now.Date.AddHours(21).AddMinutes(30), window.End);
        }

        [TestMethod]
        public void Parse_TimeHintMinutes_MoveTheStartOfTheWindow()
        {
            var window = VoiceTimeParser.Parse("回看20点的新闻联播", "zh-CN", Now, 0);

            Assert.AreEqual(Now.Date.AddHours(20), window.Start);

            var clamped = VoiceTimeParser.Parse("回看20点的新闻联播", "zh-CN", Now, 1000);
            Assert.AreEqual(Now.Date.AddHours(16), clamped.Start, "the hint is clamped to four hours");
        }

        [TestMethod]
        public void Parse_EveningHour_FoldsIntoTheTwentyFourHourClock()
        {
            var window = Parse("回看晚上8点的新闻联播");

            Assert.AreEqual(Now.Date.AddHours(19), window.Start, "晚上8点 is 20:00, not 08:00");
            StringAssert.Contains(window.Label, "20:00");
        }

        [TestMethod]
        public void Parse_MorningHour_StaysWhereItWasSaid()
        {
            var window = Parse("回看早上8点的新闻");

            Assert.AreEqual(Now.Date.AddHours(7), window.Start);
        }

        [TestMethod]
        public void Parse_SpokenDigits_AreReadAsANumber()
        {
            var window = Parse("回看八点的新闻联播");
            Assert.AreEqual(Now.Date.AddHours(7), window.Start);

            var half = Parse("回看晚上八点半的新闻联播");
            Assert.AreEqual(Now.Date.AddHours(19).AddMinutes(30), half.Start, "晚上八点半 is 20:30");
            StringAssert.Contains(half.Label, "20:30");
        }

        [TestMethod]
        public void Parse_TenOClock_IsReadAsTen()
        {
            var window = Parse("回看十点的新闻联播");

            Assert.AreEqual(Now.Date.AddHours(9), window.Start);
        }

        [TestMethod]
        public void Parse_MidnightAndNoon_DoNotBothBecomeTwelve()
        {
            Assert.AreEqual(Now.Date.AddHours(-1), Parse("回看凌晨12点的新闻").Start);
            Assert.AreEqual(Now.Date.AddHours(11), Parse("回看中午12点的新闻").Start);
        }

        [TestMethod]
        public void Parse_CalendarDate_SearchesThatDay()
        {
            var previous = Parse("回看10月13号的新闻联播");
            Assert.AreEqual(new DateTime(2026, 10, 13), previous.Start);

            var explicitYear = Parse("回看2026年10月12日的新闻联播");
            Assert.AreEqual(new DateTime(2026, 10, 12), explicitYear.Start);

            var dayOnly = Parse("回看13号的新闻联播");
            Assert.AreEqual(new DateTime(2026, 10, 13), dayOnly.Start);
        }

        [TestMethod]
        public void Parse_CalendarDateWithoutAYear_MeansLastYearWhenStillAhead()
        {
            var window = Parse("回看12月1号的新闻联播");

            Assert.AreEqual(new DateTime(2025, 12, 1), window.Start, "December is still ahead in October");
        }

        [TestMethod]
        public void Parse_CalendarDateCanBeTurnedOff()
        {
            var window = VoiceTimeParser.Parse("回看10月13号的新闻联播", "zh-CN", Now,
                VoiceTimeParser.DefaultTimeHintMinutes, false);

            Assert.AreEqual(Now.Date, window.Start, "without the setting the date is left to the title filter");
        }

        [TestMethod]
        public void Parse_LastFriday_IsTheFridayOfThePreviousWeek()
        {
            var window = Parse("回看上周五的新闻联播");

            Assert.AreEqual(DayOfWeek.Friday, window.Start.DayOfWeek);
            var days = (Now.Date - window.Start).TotalDays;
            Assert.IsTrue(days is >= 8 and <= 14, $"上周五 should be 8-14 days back, was {days}");
        }

        [TestMethod]
        public void Parse_WeekdayName_WithoutLastWeek_IsTheMostRecentOne()
        {
            var window = Parse("回看星期五的新闻联播");

            Assert.AreEqual(DayOfWeek.Friday, window.Start.DayOfWeek);
            var days = (Now.Date - window.Start).TotalDays;
            Assert.IsTrue(days is >= 1 and <= 7, $"星期五 should be 1-7 days back, was {days}");
        }

        [TestMethod]
        public void Parse_YesterdayAndToday_KeepTheirWholeDay()
        {
            var yesterday = Parse("回看昨天的新闻联播");
            Assert.AreEqual(Now.Date.AddDays(-1), yesterday.Start);
            Assert.AreEqual(Now.Date, yesterday.End);

            var today = Parse("回看今天的新闻联播");
            Assert.AreEqual(Now.Date, today.Start);
            Assert.AreEqual(Now.Date.AddDays(1), today.End);
        }

        [TestMethod]
        public void Parse_MorningPart_AheadOfTheCurrentTime_EndsNow()
        {
            var window = Parse("回看今天早上的新闻");

            Assert.AreEqual(Now.Date.AddHours(6), window.Start);
            Assert.AreEqual(Now.AddMinutes(30), window.End, "22:00 must not look for programmes in this morning");
        }

        [TestMethod]
        public void StripTimeHints_LeavesOnlyTheProgrammeName()
        {
            var stripped = VoiceTimeParser.StripTimeHints("回看今天晚上的八点新闻联播");

            Assert.IsFalse(stripped.Contains("八点"), stripped);
            Assert.IsFalse(stripped.Contains("20"), stripped);
            StringAssert.Contains(stripped, "新闻联播");
        }

        [TestMethod]
        public void StripTimeHints_RemovesTheCalendarDateToo()
        {
            var stripped = VoiceTimeParser.StripTimeHints("回看10月13号的新闻联播");

            Assert.IsFalse(stripped.Contains("10"), stripped);
            Assert.IsFalse(stripped.Contains("13"), stripped);
            StringAssert.Contains(stripped, "新闻联播");
        }

        [TestMethod]
        public void Profile_Strictness_MovesBothThresholds()
        {
            Assert.AreEqual(0.55, VoiceFuzzy.Profile.Default.Candidate, 0.0001, "标准 keeps today's behaviour");
            Assert.AreEqual(0.85, VoiceFuzzy.Profile.Default.Auto, 0.0001);
            Assert.AreEqual(0.45, VoiceFuzzy.Profile.FromStrictness("loose").Candidate, 0.0001);
            Assert.AreEqual(0.70, VoiceFuzzy.Profile.FromStrictness("strict").Candidate, 0.0001);
            Assert.AreEqual(0.55, VoiceFuzzy.Profile.FromStrictness("nonsense").Candidate, 0.0001);
        }

        [TestMethod]
        public void Profile_SportsNews_IsNoLongerACandidateForTheNews()
        {
            var score = VoiceFuzzy.Score("新闻联播", "体育新闻");

            Assert.IsTrue(score < VoiceFuzzy.Profile.Default.Candidate,
                $"体育新闻 must not be offered for 新闻联播, scored {score}");
            Assert.IsTrue(score >= VoiceFuzzy.Profile.FromStrictness("loose").Candidate,
                "the loose setting is allowed to offer it anyway");
        }
    }
}
