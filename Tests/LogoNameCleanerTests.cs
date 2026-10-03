using System.Linq;
using LibmpvIptvClient.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class LogoNameCleanerTests
    {
        [TestMethod]
        public void Clean_RemovesQualityTagsAndSpaces()
        {
            Assert.AreEqual("CCTV1", LogoNameCleaner.Clean("CCTV-1 HD"));
            Assert.AreEqual("CCTV1", LogoNameCleaner.Clean("（HD）CCTV-1"));
            Assert.AreEqual("湖南卫视", LogoNameCleaner.Clean("湖南卫视 高清"));
            Assert.AreEqual("东方卫视", LogoNameCleaner.Clean("东方卫视4K"));
            Assert.AreEqual("CCTV5+", LogoNameCleaner.Clean("CCTV5 PLUS"));
            Assert.AreEqual("CCTV5+", LogoNameCleaner.Clean("CCTV5+"));
        }

        [TestMethod]
        public void Candidates_TryCleanedNameFirst_ThenRawName()
        {
            var candidates = LogoNameCleaner.Candidates("CCTV-1 HD").ToList();

            CollectionAssert.AreEqual(new[] { "CCTV1", "CCTV-1 HD" }, candidates);
        }

        [TestMethod]
        public void Candidates_DeduplicateWhenNothingToClean()
        {
            var candidates = LogoNameCleaner.Candidates("CCTV1");

            Assert.AreEqual(1, candidates.Count);
            Assert.AreEqual("CCTV1", candidates[0]);
        }

        [TestMethod]
        public void Candidates_IgnoreBlankNames()
        {
            Assert.AreEqual(0, LogoNameCleaner.Candidates("   ").Count);
            Assert.AreEqual(0, LogoNameCleaner.Candidates(null).Count);
        }
    }
}
