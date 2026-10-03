using System;
using LibmpvIptvClient;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The shared http client used to accept every TLS certificate, so playlists, EPG data, logos and
    /// update checks could be replaced by a man in the middle. Validation is now on by default and only
    /// disabled when the user asks for it.
    /// </summary>
    [TestClass]
    public class CertificatePolicyTests
    {
        [TestMethod]
        public void ValidationIsOnByDefault()
        {
            Assert.IsFalse(new HttpHeaderConfig().AllowInvalidCertificates);
            Assert.IsFalse(HttpClientService.ShouldDisableCertificateValidation(new HttpHeaderConfig()));
            Assert.IsFalse(HttpClientService.ShouldDisableCertificateValidation(null));
        }

        [TestMethod]
        public void ValidationIsDisabledOnlyWhenExplicitlyRequested()
        {
            var headers = new HttpHeaderConfig { AllowInvalidCertificates = true };

            Assert.IsTrue(HttpClientService.ShouldDisableCertificateValidation(headers));
        }

        [TestMethod]
        public void InvalidateClient_RebuildsTheSharedClient()
        {
            var before = HttpClientService.Instance.Client;

            HttpClientService.Instance.InvalidateClient();
            var after = HttpClientService.Instance.Client;

            Assert.IsNotNull(after);
            Assert.AreNotSame(before, after, "失效后应按当前设置重建 HttpClient");
        }
    }
}
