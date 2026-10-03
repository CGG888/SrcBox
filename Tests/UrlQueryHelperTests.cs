using LibmpvIptvClient.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class UrlQueryHelperTests
    {
        [TestMethod]
        public void AppendQuery_UsesQuestionMarkOrAmpersand()
        {
            Assert.AreEqual("http://h/live?r2h-seek-mode=range(UTC+8/3600)",
                UrlQueryHelper.AppendQuery("http://h/live", "r2h-seek-mode=range(UTC+8/3600)"));

            Assert.AreEqual("http://h/live?a=1&b=2",
                UrlQueryHelper.AppendQuery("http://h/live?a=1", "b=2"));
        }

        [TestMethod]
        public void AppendQuery_ToleratesLeadingSeparatorAndWhitespace()
        {
            Assert.AreEqual("http://h/live?b=2", UrlQueryHelper.AppendQuery("http://h/live", " &b=2 "));
            Assert.AreEqual("http://h/live?a=1&b=2", UrlQueryHelper.AppendQuery("http://h/live?a=1", "?b=2"));
        }

        [TestMethod]
        public void AppendQuery_IsANoOpForBlankInput()
        {
            Assert.AreEqual("http://h/live", UrlQueryHelper.AppendQuery("http://h/live", ""));
            Assert.AreEqual("http://h/live", UrlQueryHelper.AppendQuery("http://h/live", "   "));
            Assert.AreEqual("http://h/live", UrlQueryHelper.AppendQuery("http://h/live", "&&"));
            Assert.AreEqual("", UrlQueryHelper.AppendQuery("", "b=2"));
            Assert.AreEqual("", UrlQueryHelper.AppendQuery(null, "b=2"));
        }
    }
}
