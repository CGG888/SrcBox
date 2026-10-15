using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibmpvIptvClient.Architecture.Application.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The launch check has to end in a real answer whenever any route to GitHub works. The shared client
    /// goes through the system proxy, so a broken local proxy used to fail the check silently; it is now
    /// retried once over a proxy-less connection before the check is reported as failed.
    /// </summary>
    [TestClass]
    public class UpdateCheckFallbackTests
    {
        /// <summary>Answers every request with a valid "latest release" document.</summary>
        sealed class ReleaseHandler : HttpMessageHandler
        {
            public int Calls;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                var json = "{\"tag_name\":\"v9.9.9\",\"body\":\"notes\",\"assets\":[{\"name\":\"SrcBox_Setup_v9.9.9.exe\",\"browser_download_url\":\"https://example.com/setup.exe\"}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
        }

        /// <summary>Fails every request, which is what a blocked or rate limited source looks like.</summary>
        sealed class BlockedHandler : HttpMessageHandler
        {
            public int Calls;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }
        }

        static HttpClient Client(HttpMessageHandler handler)
        {
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SrcBox/Test");
            return client;
        }

        [TestMethod]
        public void PrimarySourceAnswers_DirectRetryIsNotUsed()
        {
            var direct = new ReleaseHandler();
            var svc = new GithubUpdateService(Client(new ReleaseHandler()), () => Client(direct));

            var result = svc.CheckForUpdatesAsync(useCdn: false).GetAwaiter().GetResult();

            Assert.IsTrue(result.IsNewer, result.ErrorMessage);
            Assert.AreEqual("9.9.9", result.ReleaseInfo!.Version);
            Assert.AreEqual(0, direct.Calls, "a working primary source must not trigger the proxy-less retry");
        }

        [TestMethod]
        public void EverySourceBlocked_RetriesWithoutTheSystemProxy()
        {
            var primary = new BlockedHandler();
            var direct = new ReleaseHandler();
            var svc = new GithubUpdateService(Client(primary), () => Client(direct));

            var result = svc.CheckForUpdatesAsync(useCdn: false).GetAwaiter().GetResult();

            Assert.IsTrue(result.IsNewer, "the retry must rescue a check whose every source was blocked: " + result.ErrorMessage);
            Assert.AreEqual("9.9.9", result.ReleaseInfo!.Version);
            Assert.AreEqual("SrcBox_Setup_v9.9.9.exe", result.ReleaseInfo.FileName);
            Assert.AreEqual(1, primary.Calls, "the blocked primary source is asked once per pass");
            Assert.AreEqual(1, direct.Calls);
        }

        [TestMethod]
        public void NothingAnswers_ReportsFailureWithoutARelease()
        {
            var svc = new GithubUpdateService(Client(new BlockedHandler()), () => Client(new BlockedHandler()));

            var result = svc.CheckForUpdatesAsync(useCdn: false).GetAwaiter().GetResult();

            Assert.IsFalse(result.IsNewer);
            Assert.IsNull(result.ReleaseInfo);
            Assert.AreEqual("Failed to fetch release info", result.ErrorMessage);
        }
    }
}
