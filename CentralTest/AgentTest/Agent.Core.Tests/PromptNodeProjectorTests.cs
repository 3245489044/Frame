using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Core.Prompts;
using Operation.LoopFoldMethod.Node.Model;

namespace Agent.Core.Tests;

/// <summary>
/// 提示词域投影器（PromptNodeProjector）纯函数测试 —— 基于通用节点框架（Node/）的 L0 头 / skill 尾 缓存友好排布。
/// 覆盖：稳定前缀置头、多变 skill 置尾、动态居中、空集退化、无 skill 退化、显式槽位覆盖。
/// </summary>
[TestClass]
public class PromptNodeProjectorTests
{
    private static NodeItem N(NodeCategory c, int order, string text)
        => new(c, order, "test", text);

    [TestMethod]
    public void Project_L0Head_SkillTail_DynamicMiddle()
    {
        var r = PromptNodeProjector.Build(new[]
        {
            N(NodeCategory.Skill, 0, "## skill body"),
            N(NodeCategory.Base, 0, "L0 base"),
            N(NodeCategory.Dynamic, 0, "step content"),
        });

        // System = Head(L0 稳定底座)，不含 skill。
        Assert.AreEqual("L0 base", r.System);
        // UserContents = [Body(动态) , Tail(skill)] —— skill 置末条。
        CollectionAssert.AreEqual(
            new[] { "step content", "## skill body" },
            r.UserContents.ToArray());
    }

    [TestMethod]
    public void Project_NoSkill_TailEmpty()
    {
        var r = PromptNodeProjector.Build(new[]
        {
            N(NodeCategory.Base, 0, "L0 base"),
            N(NodeCategory.Dynamic, 0, "step content"),
        });

        Assert.AreEqual("L0 base", r.System);
        CollectionAssert.AreEqual(
            new[] { "step content" },
            r.UserContents.ToArray());
    }

    [TestMethod]
    public void Project_EmptyInput_EmptySystemAndUsers()
    {
        var r = PromptNodeProjector.Build(null);

        Assert.AreEqual(string.Empty, r.System);
        Assert.AreEqual(0, r.UserContents.Count);
    }

    [TestMethod]
    public void Project_EmptyBands_DroppedFromUsers()
    {
        // 仅 skill（Tail）与 L0（Head）：Body 空 → user 只有 skill 一条。
        var r = PromptNodeProjector.Build(new[]
        {
            N(NodeCategory.Base, 0, "L0 base"),
            N(NodeCategory.Skill, 0, "## skill body"),
        });

        Assert.AreEqual("L0 base", r.System);
        CollectionAssert.AreEqual(
            new[] { "## skill body" },
            r.UserContents.ToArray());
    }

    [TestMethod]
    public void Project_ExplicitSlot_OverridesDefault()
    {
        // Base 默认 Head；显式声明 Tail → 落到 user 尾（消费域可局部覆盖稳定前缀策略）。
        var r = PromptNodeProjector.Build(new[]
        {
            new NodeItem(NodeCategory.Base, 0, "test", "moved to tail", Slot: NodeSlot.Tail),
        });

        Assert.AreEqual(string.Empty, r.System);
        CollectionAssert.AreEqual(
            new[] { "moved to tail" },
            r.UserContents.ToArray());
    }
}
