using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Reference.Projection;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// Reference 投影（Projection / RefHead）正确性测试 —— 折叠头思想在空间维度的投影（LLM-free）。
/// 覆盖：默认投影沿 round_sequence 链折叠、孤立节点兜底、空输入、读侧只投影不写边。
/// </summary>
[TestClass]
public class ProjectionTests
{
    [TestMethod]
    public void RefHead_FoldsRoundSequenceChainIntoOrderedSequence()
    {
        var ctx = new RefHeadContext(
            ScopeKey: "task-1",
            NodeKeys: new[] { "wf.a", "wf.b", "wf.c" },
            RoundSequence: new[] { ("wf.a", "wf.b"), ("wf.b", "wf.c") });

        var view = RefHead.Default.Project(ctx);

        Assert.AreEqual("task-1", view.ScopeKey);
        CollectionAssert.AreEqual(
            new[] { "wf.a", "wf.b", "wf.c" },
            System.Linq.Enumerable.ToArray(view.Sequence));
    }

    [TestMethod]
    public void RefHead_IsolatedNodes_AppendedAfterChain()
    {
        var ctx = new RefHeadContext(
            ScopeKey: "task-2",
            NodeKeys: new[] { "wf.a", "wf.b", "wf.x" },
            RoundSequence: new[] { ("wf.a", "wf.b") });

        var view = RefHead.Default.Project(ctx);

        CollectionAssert.AreEqual(
            new[] { "wf.a", "wf.b", "wf.x" },
            System.Linq.Enumerable.ToArray(view.Sequence));
    }

    [TestMethod]
    public void RefHead_EndpointOutsideNodeKeys_StillIncluded()
    {
        var ctx = new RefHeadContext(
            ScopeKey: "task-3",
            NodeKeys: new[] { "wf.b" },
            RoundSequence: new[] { ("wf.a", "wf.b") });

        var view = RefHead.Default.Project(ctx);

        // 端点 a 未在 NodeKeys 也并入序列（时序边端点属该作用域挂载）。
        CollectionAssert.AreEqual(
            new[] { "wf.b", "wf.a" },
            System.Linq.Enumerable.ToArray(view.Sequence));
    }

    [TestMethod]
    public void RefHead_EmptyInput_EmptyView()
    {
        var view = RefHead.Default.Project(new RefHeadContext("x", System.Array.Empty<string>(), System.Array.Empty<(string, string)>()));

        Assert.AreEqual("x", view.ScopeKey);
        Assert.AreEqual(0, view.NodeKeys.Count);
        Assert.AreEqual(0, view.Sequence.Count);
    }

    [TestMethod]
    public void RefHead_IsPureReadProjection_NoWritePortExposed()
    {
        // RefHead 只消费 RefHeadContext 输入并产出 IRefView，签名不含任何写边端口 —— 防「投影顺手写边」纪律的编译级守护。
        var ctx = new RefHeadContext("t", new[] { "n" }, new[] { ("a", "n") });
        var view = RefHead.Default.Project(ctx);
        Assert.IsNotNull(view);
    }
}
