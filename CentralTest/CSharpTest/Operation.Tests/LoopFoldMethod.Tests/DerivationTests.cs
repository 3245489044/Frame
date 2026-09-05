using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Reference.Derivation;
using Operation.LoopFoldMethod.Reference.Model;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// Reference 纯推演（Derivation）正确性测试 —— 零 AI 额度纯 CI 可跑（LLM-free）。
/// 覆盖：可见性判定（ScopeResolver）、链尾/终答推导（TreeHeadProjector）、控制流边推导（EdgeDeriver）。
/// </summary>
[TestClass]
public class DerivationTests
{
    // ---- ScopeResolver：可见性判定 ----

    [TestMethod]
    public void ScopeResolver_SameWorkflow_AlwaysVisible()
    {
        Assert.IsTrue(ScopeResolver.IsVisibleTo("wf", ReferenceVisibility.WorkflowLocal, "wf"));
        Assert.IsTrue(ScopeResolver.IsVisibleTo("wf", ReferenceVisibility.Exported, "wf"));
    }

    [TestMethod]
    public void ScopeResolver_CrossWorkflow_OnlyExportedVisible()
    {
        Assert.IsTrue(ScopeResolver.IsVisibleTo("wfA", ReferenceVisibility.Exported, "wfB"));
        Assert.IsFalse(ScopeResolver.IsVisibleTo("wfA", ReferenceVisibility.WorkflowLocal, "wfB"));
    }

    [TestMethod]
    public void ScopeResolver_EmptyId_NotVisible()
    {
        Assert.IsFalse(ScopeResolver.IsVisibleTo("", ReferenceVisibility.Exported, "wfB"));
        Assert.IsFalse(ScopeResolver.IsVisibleTo("wfA", ReferenceVisibility.Exported, ""));
    }

    // ---- TreeHeadProjector：链尾终答推导 ----

    [TestMethod]
    public void TreeHeadProjector_LinearChain_LastStepIsFinal()
    {
        var wf = "wf";
        var keys = new[] { "a", "b", "c" };
        var nodeIds = keys.Select(k => EdgeDeriver.NodeId(wf, k)).ToList();
        var edges = EdgeDeriver.DeriveWorkflowControlFlowEdges(wf, keys); // a->b, b->c

        Assert.AreEqual(2, TreeHeadProjector.ResolveFinalStepIndex(nodeIds, edges)); // 链尾 c
    }

    [TestMethod]
    public void TreeHeadProjector_Branching_TerminalOutDegreeZeroWins()
    {
        var nodeIds = new[] { "wf.a", "wf.b", "wf.c" };
        // b 作为 Calls source 出度>0；c 出度 0 为终节点（分支下仍是链尾语义）
        var edges = new[]
        {
            new ReferenceEdge(ReferenceTypes.Calls, "wf.a", "wf.b"),
            new ReferenceEdge(ReferenceTypes.Calls, "wf.b", "wf.c"),
        };

        Assert.AreEqual(2, TreeHeadProjector.ResolveFinalStepIndex(nodeIds, edges));
    }

    [TestMethod]
    public void TreeHeadProjector_Empty_ReturnsMinusOne()
    {
        Assert.AreEqual(-1, TreeHeadProjector.ResolveFinalStepIndex(Array.Empty<string>(), Array.Empty<ReferenceEdge>()));
    }

    [TestMethod]
    public void TreeHeadProjector_FullCycle_FallsBackToLast()
    {
        var nodeIds = new[] { "wf.a", "wf.b" };
        var edges = new[]
        {
            new ReferenceEdge(ReferenceTypes.Calls, "wf.a", "wf.b"),
            new ReferenceEdge(ReferenceTypes.Calls, "wf.b", "wf.a"), // 全环：出度均>0
        };

        Assert.AreEqual(1, TreeHeadProjector.ResolveFinalStepIndex(nodeIds, edges)); // 退化兜底
    }

    // ---- EdgeDeriver：控制流边推导 ----

    [TestMethod]
    public void EdgeDeriver_DeriveWorkflowControlFlowEdges_OwnsAndCalls()
    {
        var edges = EdgeDeriver.DeriveWorkflowControlFlowEdges("wf", new[] { "a", "b", "c" });

        // 组合 Owns ×3（wf→每步）+ 控制流 Calls ×2（a->b, b->c）
        Assert.AreEqual(5, edges.Count);
        Assert.AreEqual(3, edges.Count(e => e.RefType == ReferenceTypes.Owns));
        Assert.AreEqual(2, edges.Count(e => e.RefType == ReferenceTypes.Calls));
        // 源端点：Owns 源 = workflow，Calls 源 = 上一步节点
        Assert.IsTrue(edges.All(e => e.SourceId == "wf" || e.SourceId.StartsWith("wf.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void EdgeDeriver_DeriveParts_KeysAndEdgesAligned()
    {
        var (keys, edges) = EdgeDeriver.DeriveParts("wf", new[] { "a", "b" });

        CollectionAssert.AreEqual(new[] { "a", "b" }, keys.ToArray());
        Assert.AreEqual(3, edges.Count); // Owns×2 + Calls×1
        Assert.AreEqual("wf.b", edges.Last().TargetId); // 链尾
    }

    [TestMethod]
    public void EdgeDeriver_Empty_NoEdges()
    {
        Assert.AreEqual(0, EdgeDeriver.DeriveWorkflowControlFlowEdges("wf", Array.Empty<string>()).Count);
        var (keys, edges) = EdgeDeriver.DeriveParts("wf", Array.Empty<string>());
        Assert.AreEqual(0, keys.Count);
        Assert.AreEqual(0, edges.Count);
    }

    [TestMethod]
    public void EdgeDeriver_NodeId_MatchesNodeRelationContract()
    {
        Assert.AreEqual("wf.myNode", EdgeDeriver.NodeId("wf", "myNode"));
    }
}
