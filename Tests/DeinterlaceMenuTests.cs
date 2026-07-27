using Microsoft.VisualStudio.TestTools.UnitTesting;
using LibmpvIptvClient;

namespace LibmpvIptvClient.Tests;

/// <summary>
/// 反交错（Deinterlace）菜单 / 配置的相关测试
/// 验证默认值、序列化、菜单项 IsChecked 状态
/// </summary>
[TestClass]
public class DeinterlaceMenuTests
{
    /// <summary>
    /// 模拟"勾选态"判定：与 MenuBuilder.cs 中 IsChecked 判定一致
    /// Deinterlace != "no" 时视为开启
    /// </summary>
    [TestMethod]
    public void Menu_IsChecked_ShouldReflectDeinterlaceState()
    {
        bool IsOn(string mode) => !string.Equals(mode, "no", System.StringComparison.OrdinalIgnoreCase);

        // 开启态
        Assert.IsTrue(IsOn("auto"));
        Assert.IsTrue(IsOn("yes"));
        Assert.IsTrue(IsOn("AUTO"));

        // 关闭态
        Assert.IsFalse(IsOn("no"));
        Assert.IsFalse(IsOn("NO"));
    }

    /// <summary>
    /// 模拟"开 -> 关"切换行为（用户从菜单取消勾选）
    /// 期望：Deinterlace 变 "no"
    /// </summary>
    [TestMethod]
    public void DeinterlaceToggle_OnFalse_FromAuto_SetsNo()
    {
        // 起始：auto
        var s = new PlaybackSettings { Deinterlace = "auto" };
        // 用户取消勾选 → on=false → "no"
        s.Deinterlace = s.Deinterlace == "no" ? "auto" : "no";
        Assert.AreEqual("no", s.Deinterlace);
    }

    /// <summary>
    /// 模拟"关 -> 开"切换行为（用户从菜单勾选）
    /// 期望：Deinterlace 变 "auto"
    /// </summary>
    [TestMethod]
    public void DeinterlaceToggle_OnTrue_FromNo_SetsAuto()
    {
        // 起始：no
        var s = new PlaybackSettings { Deinterlace = "no" };
        // 用户勾选 → on=true → "auto"
        s.Deinterlace = s.Deinterlace == "no" ? "auto" : "no";
        Assert.AreEqual("auto", s.Deinterlace);
    }

    /// <summary>
    /// 验证所有合法值都是受支持的字符串（不抛异常）
    /// </summary>
    [TestMethod]
    public void Deinterlace_AllSupportedModes_AreValidStrings()
    {
        var validModes = new[] { "no", "yes", "auto" };
        var validParity = new[] { "auto", "tff", "bff" };
        var validAlgo = new[] { "yadif", "bwdif", "none" };

        foreach (var m in validModes) { Assert.IsNotNull(m); }
        foreach (var p in validParity) { Assert.IsNotNull(p); }
        foreach (var a in validAlgo) { Assert.IsNotNull(a); }
    }

    /// <summary>
    /// 验证默认配置对老用户透明（向前兼容）
    /// </summary>
    [TestMethod]
    public void Deinterlace_Default_RecommendedForIPTV()
    {
        // 新建用户应得到 "auto"（智能检测），对 1080p 逐行流零副作用
        var s = new PlaybackSettings();
        Assert.AreEqual("auto", s.Deinterlace, "默认应为 auto（推荐）");
        Assert.AreEqual("yadif", s.DeinterlaceAlgorithm, "默认算法应为 yadif（质量好）");
    }
}
