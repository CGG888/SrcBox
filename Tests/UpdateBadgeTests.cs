using System;
using LibmpvIptvClient.Architecture.Application.Settings;
using LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The new-version badge on the title bar / fullscreen top overlay: it lights up only for a real
    /// newer release. The background check runs on every launch (it used to be throttled to one check
    /// per day, which hid a release that appeared right after the previous check), so the badge rules
    /// here are the whole contract.
    /// </summary>
    [TestClass]
    public class UpdateBadgeTests
    {
        static UpdateCheckResult Newer(string version = "2.0.0")
        {
            return new UpdateCheckResult(
                true,
                "1.0.0",
                new ReleaseInfo { Version = version, DownloadUrl = "https://example.com/setup.exe" },
                "");
        }

        static UpdateCheckResult NotNewer()
        {
            return new UpdateCheckResult(false, "2.0.0", null, "");
        }

        [TestMethod]
        public void NewerRelease_LightsTheBadge()
        {
            var vm = new MainWindowUpdateBadgeActionsViewModel();
            var changed = 0;
            vm.Changed += () => changed++;

            Assert.IsTrue(vm.Apply(Newer()));
            Assert.IsTrue(vm.HasNewVersion);
            Assert.AreEqual("2.0.0", vm.LatestVersion);
            Assert.AreEqual("https://example.com/setup.exe", vm.DownloadUrl);
            Assert.AreEqual(1, changed);
        }

        [TestMethod]
        public void NotNewer_KeepsTheBadgeHidden()
        {
            var vm = new MainWindowUpdateBadgeActionsViewModel();
            var changed = 0;
            vm.Changed += () => changed++;

            Assert.IsFalse(vm.Apply(NotNewer()));
            Assert.IsFalse(vm.HasNewVersion);
            Assert.AreEqual(0, changed);
        }

        [TestMethod]
        public void NewerWithoutReleaseInfo_KeepsTheBadgeHidden()
        {
            var vm = new MainWindowUpdateBadgeActionsViewModel();
            var changed = 0;
            vm.Changed += () => changed++;

            Assert.IsFalse(vm.Apply(new UpdateCheckResult(true, "1.0.0", null, "")));
            Assert.IsFalse(vm.HasNewVersion);
            Assert.AreEqual(0, changed);
        }

        [TestMethod]
        public void KnownVersionFromAnEarlierSession_LightsTheBadge()
        {
            var vm = new MainWindowUpdateBadgeActionsViewModel();
            var changed = 0;
            vm.Changed += () => changed++;

            Assert.IsTrue(vm.ApplyKnownVersion("1.1.16"));
            Assert.IsTrue(vm.HasNewVersion);
            Assert.AreEqual("1.1.16", vm.LatestVersion);
            Assert.AreEqual(1, changed);

            Assert.IsFalse(vm.ApplyKnownVersion(""));
            Assert.IsFalse(vm.ApplyKnownVersion(null));
            Assert.AreEqual(1, changed);
        }

        [TestMethod]
        public void NewerVersion_IsDecidedSemantically()
        {
            Assert.IsTrue(GithubUpdateService.IsNewerVersion("1.1.16", "1.1.15"));
            Assert.IsTrue(GithubUpdateService.IsNewerVersion("v1.2", "1.1.15.0"));
            Assert.IsFalse(GithubUpdateService.IsNewerVersion("1.1.16", "1.1.16"));
            Assert.IsFalse(GithubUpdateService.IsNewerVersion("1.1.15", "1.1.16"));
            Assert.IsFalse(GithubUpdateService.IsNewerVersion("", "1.1.15"));
            Assert.IsFalse(GithubUpdateService.IsNewerVersion("1.1.16", ""));
        }

        [TestMethod]
        public void OpenUpdateFlow_RaisesTheRequest()
        {
            var vm = new MainWindowUpdateBadgeActionsViewModel();
            var opened = 0;
            vm.RequestOpenUpdateFlow += () => opened++;

            vm.OpenUpdateFlow();

            Assert.AreEqual(1, opened);
        }
    }
}
