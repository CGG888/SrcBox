using System.Collections.Generic;
using System.Linq;
using LibmpvIptvClient;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The replay / timeshift drawers edit "append epg_time", but the settings page never copied the flag
    /// into the form nor wrote it back, so the checkbox showed the wrong state and changes were lost.
    /// </summary>
    [TestClass]
    public class ReplayTimeshiftSettingsTests
    {
        [TestMethod]
        public void ReplayForm_CarriesAppendEpgTime()
        {
            var source = new ReplayConfig { Enabled = true, UrlFormat = "http://x/{start}", DurationHours = 48, AppendEpgTime = true };

            var form = new SettingsReplayDrawerViewModel().BuildTempConfig(source);

            Assert.IsTrue(form.AppendEpgTime);
            Assert.AreEqual(48, form.DurationHours);
            Assert.AreEqual("http://x/{start}", form.UrlFormat);
        }

        [TestMethod]
        public void TimeshiftForm_CarriesAppendEpgTime()
        {
            var source = new TimeshiftConfig { Enabled = false, UrlFormat = "http://y/{start}", DurationHours = 3, AppendEpgTime = true };

            var form = new SettingsTimeshiftDrawerViewModel().BuildTempConfig(source);

            Assert.IsTrue(form.AppendEpgTime);
            Assert.AreEqual(3, form.DurationHours);
            Assert.IsFalse(form.Enabled);
        }

        [TestMethod]
        public void ReplayForm_DefaultIsOff()
        {
            var form = new SettingsReplayDrawerViewModel().BuildTempConfig(new ReplayConfig());

            Assert.IsFalse(form.AppendEpgTime);
        }
    }
}
