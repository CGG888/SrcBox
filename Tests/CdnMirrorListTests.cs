using System.Linq;
using LibmpvIptvClient.Architecture.Application.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// The update check has to survive api.github.com being blocked or rate limited: the gh-proxy mirrors
    /// must ship inside the assembly (they used to be looked up in a file the published build never
    /// contained, which left the app with no fallback at all) and the request order has to try the API
    /// first, then every mirror.
    /// </summary>
    [TestClass]
    public class CdnMirrorListTests
    {
        [TestMethod]
        public void Load_ReturnsTheShippedMirrors()
        {
            var mirrors = CdnMirrorList.Load();

            Assert.IsTrue(mirrors.Count > 0,
                "the mirror list must ship embedded - an empty list means the update check dies with api.github.com");
            foreach (var mirror in mirrors)
            {
                Assert.IsTrue(mirror.StartsWith("http"), mirror);
                Assert.IsFalse(mirror.EndsWith("/"), mirror);
            }
            CollectionAssert.AllItemsAreUnique(mirrors.ToList());
        }

        [TestMethod]
        public void BuildCandidateUrls_TriesTheApiFirstThenEachMirror()
        {
            const string api = "https://api.github.com/repos/CGG888/SrcBox/releases/latest";
            var urls = GithubUpdateService.BuildCandidateUrls(api, new[] { "https://gh-proxy.org/", "https://hk.gh-proxy.org" });

            Assert.AreEqual(3, urls.Count);
            Assert.AreEqual(api, urls[0], "the API itself must be tried first");
            Assert.AreEqual("https://gh-proxy.org/" + api, urls[1], "mirror prefixes are joined with exactly one slash");
            Assert.AreEqual("https://hk.gh-proxy.org/" + api, urls[2]);
        }

        [TestMethod]
        public void BuildCandidateUrls_WithoutCdn_OnlyUsesTheApi()
        {
            var urls = GithubUpdateService.BuildCandidateUrls("https://api.github.com/x", null);

            Assert.AreEqual(1, urls.Count);
        }
    }
}
