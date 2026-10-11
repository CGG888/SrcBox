using System;
using System.Collections.Generic;
using LibmpvIptvClient.Services.WebRemote;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// Channel numbering shared by the web remote page and the local channel-number overlay.
    /// </summary>
    [TestClass]
    public class WebRemoteChannelNumberingTests
    {
        [TestMethod]
        public void BuildNumberMap_NumbersAcrossGroupBoundaries()
        {
            var map = WebRemoteChannelNumbering.BuildNumberMap(new[]
            {
                new[] { "a", "b" },
                new[] { "c" }
            });

            Assert.AreEqual(3, map.Count);
            Assert.AreEqual(1, map["a"]);
            Assert.AreEqual(2, map["b"]);
            Assert.AreEqual(3, map["c"]);
        }

        [TestMethod]
        public void BuildNumberMap_FavoritesReuseTheirPlaylistNumber()
        {
            var map = WebRemoteChannelNumbering.BuildNumberMap(new[]
            {
                new[] { "a", "b" },
                new[] { "b", "a" }
            });

            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(1, map["a"]);
            Assert.AreEqual(2, map["b"]);
        }

        [TestMethod]
        public void BuildNumberMap_IgnoresEmptyIdsDuplicatesAndEmptyGroups()
        {
            var map = WebRemoteChannelNumbering.BuildNumberMap(new[]
            {
                new string?[] { "a", null, "", "a" },
                Array.Empty<string?>(),
                new string?[] { "b" }
            });

            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(1, map["a"]);
            Assert.AreEqual(2, map["b"]);
        }

        [TestMethod]
        public void BuildNumberMap_WithoutGroups_IsEmpty()
        {
            Assert.AreEqual(0, WebRemoteChannelNumbering.BuildNumberMap(null).Count);
            Assert.AreEqual(0, WebRemoteChannelNumbering.BuildNumberMap(Array.Empty<IEnumerable<string?>>()).Count);
        }

        [TestMethod]
        public void BuildNumberMap_FavoriteOnlyChannel_FollowsPlaylistGroups()
        {
            var map = WebRemoteChannelNumbering.BuildNumberMap(new[]
            {
                new[] { "a" },
                new[] { "a", "fav" }
            });

            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(1, map["a"]);
            Assert.AreEqual(2, map["fav"]);
        }
    }
}
