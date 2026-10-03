using System.IO;
using LibmpvIptvClient.Diagnostics;
using LibmpvIptvClient.Services;
using LibmpvIptvClient.Services.WebRemote;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Security fixes from the code review: the /logo/ endpoint must not serve arbitrary files, and secrets
    /// must not reach the log file in clear text.
    /// </summary>
    [TestClass]
    public class SecurityHardeningTests
    {
        [TestMethod]
        public void LogoEndpoint_RejectsFilesOutsideTheAllowedRoots()
        {
            Assert.IsFalse(WebRemoteServer.IsAllowedLogoPath(null));
            Assert.IsFalse(WebRemoteServer.IsAllowedLogoPath(""));
            Assert.IsFalse(WebRemoteServer.IsAllowedLogoPath(@"C:\Windows\System32\config\SAM"));
            Assert.IsFalse(WebRemoteServer.IsAllowedLogoPath(@"C:\Users\Public\.ssh\id_rsa"));
        }

        [TestMethod]
        public void LogoEndpoint_RejectsNonImageExtensions()
        {
            var baseDir = System.AppContext.BaseDirectory;
            Assert.IsFalse(WebRemoteServer.IsAllowedLogoPath(Path.Combine(baseDir, "user_settings.json")));
            Assert.IsFalse(WebRemoteServer.IsAllowedLogoPath(Path.Combine(baseDir, "libmpv-2.dll")));
        }

        [TestMethod]
        public void LogoEndpoint_AllowsImagesInsideTheApplicationDirectory()
        {
            var baseDir = System.AppContext.BaseDirectory;
            Assert.IsTrue(WebRemoteServer.IsAllowedLogoPath(Path.Combine(baseDir, "srcbox.png")));
            Assert.IsTrue(WebRemoteServer.IsAllowedLogoPath(Path.Combine(baseDir, "sub", "logo.PNG")));
        }

        [TestMethod]
        public void Redactor_MasksKeyValueSecretsOutsideUrls()
        {
            var redacted = LogRedactor.Redact("[WebRemote] Authentication failed with password: hunter2");

            Assert.IsFalse(redacted.Contains("hunter2"), redacted);
            StringAssert.Contains(redacted, "password=***");
        }

        [TestMethod]
        public void Redactor_MasksCommonSecretForms()
        {
            StringAssert.Contains(LogRedactor.Redact("pwd: s3cret"), "pwd=***");
            StringAssert.Contains(LogRedactor.Redact("{\"password\": \"s3cret\"}"), "password=***");
            StringAssert.Contains(LogRedactor.Redact("token=abc123"), "token=***");
            Assert.IsFalse(LogRedactor.Redact("pwd: s3cret").Contains("s3cret"));
        }

        [TestMethod]
        public void Redactor_StillMasksUrls()
        {
            var redacted = LogRedactor.Redact("http://user:pass@example.com/live.m3u8?token=abc&x=1");

            Assert.IsFalse(redacted.Contains("abc"), redacted);
            StringAssert.Contains(redacted, "***");
        }
    }
}
