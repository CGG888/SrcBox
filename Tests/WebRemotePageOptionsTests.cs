using LibmpvIptvClient.Services.WebRemote;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Covers the Web Remote settings that only change what the served page renders: the channel
    /// list / EPG switches, the timeshift toggle and the per-programme replay availability.
    /// </summary>
    [TestClass]
    public class WebRemotePageOptionsTests
    {
        [TestMethod]
        public void RemoteHtml_InjectsTheSectionSwitches()
        {
            var both = WebRemoteServer.GetRemoteHtml("en-US", 0, true, true);
            var none = WebRemoteServer.GetRemoteHtml("en-US", 0, false, false);

            StringAssert.Contains(both, "const SHOW_CHANNELS = true;");
            StringAssert.Contains(both, "const SHOW_EPG = true;");
            StringAssert.Contains(none, "const SHOW_CHANNELS = false;");
            StringAssert.Contains(none, "const SHOW_EPG = false;");
            Assert.IsFalse(none.Contains("__SHOW_"), "section switch placeholders were not replaced");
        }

        [TestMethod]
        public void RemoteHtml_HidesTheDisabledSections()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US", 0, false, false);

            StringAssert.Contains(html, "querySelector('.channel-section')");
            StringAssert.Contains(html, "querySelector('.epg-section')");
        }

        [TestMethod]
        public void RemoteHtml_OffersTheTimeshiftToggle()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US");

            foreach (var marker in new[]
                     {
                         "id=\"btnTimeshift\"", "id=\"timeshiftPath\"", "id=\"btnTimeshiftText\"",
                         "onclick=\"toggleTimeshift()\"", "updateTimeshiftBtn();"
                     })
            {
                Assert.IsTrue(html.Contains(marker), $"remote html is missing {marker}");
            }
            Assert.IsFalse(html.Contains("__BTN_TIMESHIFT__"), "timeshift label placeholder was not replaced");
        }

        [TestMethod]
        public void RemoteHtml_ReplayFollowsThePerProgrammeFlag()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US");

            StringAssert.Contains(html, "else if (isPast && p.canReplay) {");
            Assert.IsFalse(html.Contains("else if (isPast && isTimeshiftEnabled)"),
                "replay is still gated on the global timeshift switch");
        }

        [TestMethod]
        public void RemoteHtml_LeavesNoUnresolvedPlaceholder()
        {
            var html = WebRemoteServer.GetRemoteHtml("en-US", 8900, true, true);

            // The page moved to the T.* string table, so a __X__ placeholder that has no matching
            // Replace call would leak into the served html; make sure none of them survives.
            var unresolved = System.Text.RegularExpressions.Regex.Matches(html, "__[A-Z0-9_]+__");
            var names = new System.Collections.Generic.List<string>();
            foreach (System.Text.RegularExpressions.Match match in unresolved)
            {
                if (!names.Contains(match.Value)) names.Add(match.Value);
            }

            Assert.AreEqual(0, unresolved.Count, "unresolved placeholders: " + string.Join(", ", names));
        }
    }
}
