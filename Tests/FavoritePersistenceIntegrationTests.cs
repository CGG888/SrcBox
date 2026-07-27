using Microsoft.VisualStudio.TestTools.UnitTesting;
using LibmpvIptvClient.Services;
using LibmpvIptvClient.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LibmpvIptvClient.Tests;

/// <summary>
/// Bug #1 修复验证：模拟"关闭播放器 → 重新打开"的完整流程，
/// 验证 UserDataStore 的收藏列表在重启后能被正确还原到 Channel.Favorite 标记。
///
/// 之前的 Bug：应用重启后，Channel.Favorite 永远为 false，导致收藏目录为空。
/// 修复：在 LoadChannels 之后、UpdateFavorites 之前调用 ApplyFavoritesFromStore()。
/// </summary>
[TestClass]
public class FavoritePersistenceIntegrationTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SrcBoxTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 模拟"启动 → 收藏 → 关闭 → 重启"的标准流程
    /// 验证：重启后，UserDataStore 中保存的收藏能还原到新的 Channel 对象
    /// </summary>
    [TestMethod]
    public void Simulate_CloseAndReopen_FavoritesAreRestored()
    {
        var tempDir = CreateTempDir();
        try
        {
            // === 阶段 1：用户首次启动并收藏 2 个频道 ===
            var store1 = new UserDataStore(tempDir);
            var channelsInitial = new List<Channel>
            {
                new Channel { Name = "CCTV-1", TvgId = "cctv1" },
                new Channel { Name = "CCTV-2", TvgId = "cctv2" },
                new Channel { Name = "CCTV-3", TvgId = "cctv3" },
            };

            store1.SetFavorite(UserDataStore.ComputeKey(channelsInitial[0]), true);  // 收藏 CCTV-1
            store1.SetFavorite(UserDataStore.ComputeKey(channelsInitial[2]), true);  // 收藏 CCTV-3
            store1.Save();

            // === 阶段 2：模拟"关闭播放器" ===
            store1 = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // === 阶段 3：模拟"重新打开播放器" ===
            // 新建 UserDataStore 实例（应自动从 user_data.json 加载）
            var store2 = new UserDataStore(tempDir);
            var favorites = store2.GetFavorites();
            Assert.AreEqual(2, favorites.Count, "重启后应加载 2 个收藏");

            // 模拟 M3U 重新加载（新 Channel 对象，Favorite 默认 false）
            var channelsAfter = new List<Channel>
            {
                new Channel { Name = "CCTV-1", TvgId = "cctv1" },
                new Channel { Name = "CCTV-2", TvgId = "cctv2" },
                new Channel { Name = "CCTV-3", TvgId = "cctv3" },
            };

            // === 阶段 4：调用修复后的还原逻辑 ===
            ApplyFavoritesFromStore(channelsAfter, store2);

            // === 阶段 5：验证 ===
            Assert.IsTrue(channelsAfter[0].Favorite, "CCTV-1 应该是收藏");
            Assert.IsFalse(channelsAfter[1].Favorite, "CCTV-2 不应该是收藏");
            Assert.IsTrue(channelsAfter[2].Favorite, "CCTV-3 应该是收藏");

            // 验证 BuildFavoriteList 行为
            var favList = channelsAfter.Where(c => c.Favorite).ToList();
            Assert.AreEqual(2, favList.Count, "BuildFavoriteList 应返回 2 个频道");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// 验证空收藏列表不会影响任何频道
    /// </summary>
    [TestMethod]
    public void Simulate_CloseAndReopen_NoFavorites_ChannelsRemainUnfavorited()
    {
        var tempDir = CreateTempDir();
        try
        {
            var store = new UserDataStore(tempDir);  // 全新，无收藏
            var channels = new List<Channel>
            {
                new Channel { Name = "Test1", TvgId = "t1" },
                new Channel { Name = "Test2", TvgId = "t2" },
            };

            ApplyFavoritesFromStore(channels, store);

            Assert.IsFalse(channels[0].Favorite, "无收藏时 Test1 不应被标为收藏");
            Assert.IsFalse(channels[1].Favorite, "无收藏时 Test2 不应被标为收藏");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// 验证取消收藏后重启，频道不会被错误地还原为收藏
    /// </summary>
    [TestMethod]
    public void Simulate_CloseAndReopen_AfterUnfavoriting_RemainsUnfavorited()
    {
        var tempDir = CreateTempDir();
        try
        {
            // 阶段 1：先收藏
            var store1 = new UserDataStore(tempDir);
            var ch = new Channel { Name = "Test", TvgId = "t1" };
            var key = UserDataStore.ComputeKey(ch);
            store1.SetFavorite(key, true);
            store1.Save();

            // 阶段 2：取消收藏
            store1.SetFavorite(key, false);
            store1.Save();
            store1 = null;
            GC.Collect();

            // 阶段 3：重启
            var store2 = new UserDataStore(tempDir);
            var favorites = store2.GetFavorites();
            Assert.AreEqual(0, favorites.Count, "取消收藏后重启，收藏列表应为空");

            var ch2 = new Channel { Name = "Test", TvgId = "t1" };
            ApplyFavoritesFromStore(new List<Channel> { ch2 }, store2);

            Assert.IsFalse(ch2.Favorite, "取消收藏后重启，该频道不应被还原为收藏");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// 验证多个频道场景，模拟真实 M3U 加载后批量还原
    /// </summary>
    [TestMethod]
    public void Simulate_RealM3uScenario_AllFavoritesRestored()
    {
        var tempDir = CreateTempDir();
        try
        {
            // 模拟首次启动，收藏 5 个频道
            var store1 = new UserDataStore(tempDir);
            var initialChannels = new List<Channel>();
            for (int i = 0; i < 20; i++)
            {
                initialChannels.Add(new Channel { Name = $"Channel{i:D2}", TvgId = $"ch{i:D2}" });
            }
            // 收藏 #2, #5, #10, #15, #19
            int[] favoriteIndices = { 2, 5, 10, 15, 19 };
            foreach (var idx in favoriteIndices)
            {
                store1.SetFavorite(UserDataStore.ComputeKey(initialChannels[idx]), true);
            }
            store1.Save();
            store1 = null;
            GC.Collect();

            // 重启
            var store2 = new UserDataStore(tempDir);
            Assert.AreEqual(5, store2.GetFavorites().Count);

            // 重新加载 M3U（20 个新对象）
            var reloadedChannels = new List<Channel>();
            for (int i = 0; i < 20; i++)
            {
                reloadedChannels.Add(new Channel { Name = $"Channel{i:D2}", TvgId = $"ch{i:D2}" });
            }
            ApplyFavoritesFromStore(reloadedChannels, store2);

            // 验证：仅指定 5 个频道被标为收藏
            for (int i = 0; i < 20; i++)
            {
                bool expected = favoriteIndices.Contains(i);
                Assert.AreEqual(expected, reloadedChannels[i].Favorite,
                    $"Channel{i:D2} 的 Favorite 状态应为 {expected}");
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// 提取的还原逻辑（与 MainShellViewModel.ApplyFavoritesFromStore 一致）
    /// </summary>
    private static void ApplyFavoritesFromStore(IEnumerable<Channel> channels, UserDataStore store)
    {
        if (channels == null || store == null) return;
        foreach (var c in channels)
        {
            if (c == null) continue;
            try
            {
                var key = UserDataStore.ComputeKey(c);
                c.Favorite = store.IsFavorite(key);
            }
            catch { /* 单个频道失败不影响其他 */ }
        }
    }
}
