using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Node.Assemble;
using Operation.LoopFoldMethod.Node.Model;
using Operation.LoopFoldMethod.Node.Source;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// 通用节点构建框架（Node）纯函数测试 —— 收集→编排→投影 全分支 LLM-free。
/// 覆盖：槽位策略（Base→Head / Skill→Tail / 其余→Body）、显式槽位覆盖、段内稳定排序、
/// 空输入退化、每段上限、全量拼接序、聚合源合并序。
/// </summary>
[TestClass]
public class NodeTests
{
    private static NodeItem N(NodeCategory c, int order, object? payload = null, NodeSlot? slot = null)
        => new(c, order, "test", payload ?? $"node:{c}:{order}", Slot: slot);

    [TestMethod]
    public void Build_EmptyInput_EmptyAssembly()
    {
        var a = NodeBuilder.Build(null);
        Assert.IsTrue(a.IsEmpty);
        Assert.AreEqual(0, a.All.Count());
    }

    [TestMethod]
    public void Build_CacheStableSlotPolicy_BaseHeadSkillTailDynamicBody()
    {
        var a = NodeBuilder.Build(new[]
        {
            N(NodeCategory.Dynamic, 1),
            N(NodeCategory.Base, 1),
            N(NodeCategory.Skill, 1),
        });

        // 稳定前缀策略：Base→Head、Skill→Tail、Dynamic→Body。
        Assert.AreEqual(1, a.Head.Count);
        Assert.AreEqual(NodeCategory.Base, a.Head[0].Category);
        Assert.AreEqual(1, a.Body.Count);
        Assert.AreEqual(NodeCategory.Dynamic, a.Body[0].Category);
        Assert.AreEqual(1, a.Tail.Count);
        Assert.AreEqual(NodeCategory.Skill, a.Tail[0].Category);
    }

    [TestMethod]
    public void Build_DefaultPolicy_InstructionAndReference_GoToBody()
    {
        var a = NodeBuilder.Build(new[]
        {
            N(NodeCategory.Instruction, 1),
            N(NodeCategory.Reference, 2),
        });

        Assert.AreEqual(0, a.Head.Count);
        Assert.AreEqual(2, a.Body.Count);
        Assert.AreEqual(0, a.Tail.Count);
    }

    [TestMethod]
    public void Build_OrderWithinBand_StableAscending()
    {
        var a = NodeBuilder.Build(new[]
        {
            N(NodeCategory.Base, 3),
            N(NodeCategory.Base, 1),
            N(NodeCategory.Base, 2),
        });

        CollectionAssert.AreEqual(
            new[] { 1, 2, 3 },
            a.Head.Select(n => n.Order).ToArray());
    }

    [TestMethod]
    public void Build_EqualOrder_PreservesSourceOrder()
    {
        var a = NodeBuilder.Build(new[]
        {
            N(NodeCategory.Base, 1, "first"),
            N(NodeCategory.Base, 1, "second"),
        });

        Assert.AreEqual("first", (string)a.Head[0].Payload);
        Assert.AreEqual("second", (string)a.Head[1].Payload);
    }

    [TestMethod]
    public void Build_ExplicitSlot_OverridesDefaultPolicy()
    {
        // Skill 默认 → Tail；显式声明 Head 则强制置头。
        var a = NodeBuilder.Build(new[]
        {
            N(NodeCategory.Skill, 1, slot: NodeSlot.Head),
        });

        Assert.AreEqual(1, a.Head.Count);
        Assert.AreEqual(0, a.Tail.Count);
    }

    [TestMethod]
    public void Build_All_ConcatsHeadBodyTail()
    {
        var a = NodeBuilder.Build(new[]
        {
            N(NodeCategory.Skill, 1),
            N(NodeCategory.Base, 1),
            N(NodeCategory.Dynamic, 1),
        });

        var seq = a.All.Select(n => n.Category).ToArray();
        // 稳定前缀序：Head(Base) → Body(Dynamic) → Tail(Skill)。
        CollectionAssert.AreEqual(
            new[] { NodeCategory.Base, NodeCategory.Dynamic, NodeCategory.Skill },
            seq);
    }

    [TestMethod]
    public void Build_MaxPerBand_CapsEachBand()
    {
        var a = NodeBuilder.Build(
            new[]
            {
                N(NodeCategory.Base, 1), N(NodeCategory.Base, 2), N(NodeCategory.Base, 3),
                N(NodeCategory.Skill, 1), N(NodeCategory.Skill, 2),
            },
            maxPerBand: 2);

        Assert.AreEqual(2, a.Head.Count);
        Assert.AreEqual(2, a.Tail.Count);
        Assert.AreEqual(1, a.Head[0].Order); // 保留段内最小 Order（截断保序）
    }

    [TestMethod]
    public void AggregateNodeSource_MergesSourcesInOrder()
    {
        var agg = new AggregateNodeSource<int, string>(new INodeSource<int, string>[]
        {
            new FixedSource(NodeCategory.Base),
            new FixedSource(NodeCategory.Dynamic),
            new FixedSource(NodeCategory.Skill),
        });

        var nodes = agg.CollectAsync(0, new FoldSignal<string>(FoldSignalKind.ExternalInput, "start"), "answer", CancellationToken.None)
            .GetAwaiter().GetResult();

        CollectionAssert.AreEqual(
            new[] { NodeCategory.Base, NodeCategory.Dynamic, NodeCategory.Skill },
            nodes!.Select(n => n.Category).ToArray());
    }

    [TestMethod]
    public void AggregateNodeSource_AllEmpty_ReturnsNull()
    {
        var agg = new AggregateNodeSource<int, string>(new INodeSource<int, string>[]
        {
            new EmptySource(),
            new EmptySource(),
        });

        var nodes = agg.CollectAsync(0, new FoldSignal<string>(FoldSignalKind.ExternalInput, "start"), "answer", CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.IsNull(nodes);
    }

    /// <summary>固定产出单节点（一个 category）的假源。</summary>
    private sealed class FixedSource : INodeSource<int, string>
    {
        private readonly NodeCategory _category;
        public FixedSource(NodeCategory category) => _category = category;
        public Task<IReadOnlyList<NodeItem>?> CollectAsync(
            int state, FoldSignal<string> signal, string stepKey, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NodeItem>?>(new[] { N(_category, 1) });
    }

    /// <summary>恒空产出的假源（模拟某域未装配/未命中）。</summary>
    private sealed class EmptySource : INodeSource<int, string>
    {
        public Task<IReadOnlyList<NodeItem>?> CollectAsync(
            int state, FoldSignal<string> signal, string stepKey, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NodeItem>?>(null);
    }
}
