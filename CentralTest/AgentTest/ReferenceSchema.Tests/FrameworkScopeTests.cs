using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Relations;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Agent.ReferenceSchema.Tests;

/// <summary>
/// 框架完善（Phase 2 作用域可见性 + Phase 3 继承词汇表）单元测试 —— 全部内存构造，无产物依赖：
/// OutputScopeResolver 判定、NodeOutputStore 可见性持久化与作用域过滤、WorkflowTemplateResolver 成员查找链、NodeRelationDeriver 新增边。
/// </summary>
[TestClass]
public class FrameworkScopeTests
{
    // ---------- Phase 2：作用域可见性（OutputScopeResolver / NodeOutputStore） ----------

    [TestMethod]
    public void OutputScopeResolver_同工作流恒可见()
    {
        Assert.IsTrue(OutputScopeResolver.IsVisibleTo("wfA", OutputVisibility.WorkflowLocal, "wfA"));
        Assert.IsTrue(OutputScopeResolver.IsVisibleTo("wfA", OutputVisibility.Exported, "wfA"));
    }

    [TestMethod]
    public void OutputScopeResolver_跨工作流仅Exported可见()
    {
        Assert.IsFalse(OutputScopeResolver.IsVisibleTo("wfA", OutputVisibility.WorkflowLocal, "wfB"), "局部变量跨工作流不可见");
        Assert.IsTrue(OutputScopeResolver.IsVisibleTo("wfA", OutputVisibility.Exported, "wfB"), "public（Exported）跨工作流可见");
        Assert.IsFalse(OutputScopeResolver.IsVisibleTo("wfA", OutputVisibility.Exported, ""), "空请求工作流不可见");
    }

    [TestMethod]
    public async Task NodeOutputStore_作用域过滤_跨工作流仅导出节点可见()
    {
        var output = new NodeOutputStore(new InMemoryNodeStore());
        await output.WriteOutputAsync("wfA", "pub", "result", "hello", OutputVisibility.Exported, CancellationToken.None);
        await output.WriteOutputAsync("wfA", "priv", "result", "secret", OutputVisibility.WorkflowLocal, CancellationToken.None);

        var sameWf = await output.EnumerateOutputsScopedAsync("wfA", "wfA", CancellationToken.None);
        Assert.AreEqual(2, sameWf.Count, "同工作流局部 + 导出均可见");

        var otherWf = await output.EnumerateOutputsScopedAsync("wfA", "wfB", CancellationToken.None);
        Assert.AreEqual(1, otherWf.Count, "跨工作流仅 Exported 可见");
        Assert.AreEqual("pub", otherWf[0].NodeKey);

        Assert.IsNotNull(await output.TryGetNodeOutputScopedAsync("wfA", "pub", "wfB", CancellationToken.None), "导出节点跨工作流可读");
        Assert.IsNull(await output.TryGetNodeOutputScopedAsync("wfA", "priv", "wfB", CancellationToken.None), "局部节点跨工作流不可读");
    }

    [TestMethod]
    public async Task NodeOutputStore_可见性持久化往返()
    {
        var output = new NodeOutputStore(new InMemoryNodeStore());
        await output.WriteOutputAsync("wfA", "pub", "result", "hello", OutputVisibility.Exported, CancellationToken.None);

        var got = await output.TryGetNodeOutputScopedAsync("wfA", "pub", "wfA", CancellationToken.None);
        Assert.IsNotNull(got);
        Assert.AreEqual(OutputVisibility.Exported, got!.Visibility, "可见性应经 NodeOutputDoc 持久化并往返还原");
    }

    // ---------- Phase 3：模板继承（WorkflowTemplateResolver 成员查找链） ----------

    [TestMethod]
    public void WorkflowTemplateResolver_成员查找链_派生覆写并补足基类()
    {
        var derived = new RoundSpec("derived", new RoundStep[]
        {
            new("step_override", new[] { "a" }, new[] { "a_out" }),
            new("derived_only", new[] { "b" }, new[] { "b_out" }),
        });
        var baseSpec = new RoundSpec("base", new RoundStep[]
        {
            new("step_override", new[] { "x" }, new[] { "x_out" }), // 同名 EffectiveNodeId → 被派生覆写
            new("base_only", new[] { "c" }, new[] { "c_out" }),
        });

        var merged = WorkflowTemplateResolver.MergeSteps(derived, baseSpec);

        Assert.AreEqual(3, merged.Count, "派生自有(2) + 基类未覆写(1)");
        CollectionAssert.AreEqual(
            new[] { "step_override", "derived_only", "base_only" },
            merged.Select(s => s.EffectiveNodeId).ToList(),
            "派生步骤在前（含覆写），基类未覆写补足");
        Assert.AreEqual("a_out", merged[0].ResultKeys[0], "step_override 应采用派生版本（覆写生效）");
    }

    [TestMethod]
    public void WorkflowTemplateResolver_无基类_返回派生步骤()
    {
        var derived = new RoundSpec("d", new RoundStep[] { new("s", new[] { "a" }, new[] { "b" }) });

        Assert.AreEqual(1, WorkflowTemplateResolver.MergeSteps(derived, null).Count);
        Assert.AreEqual(0, WorkflowTemplateResolver.MergeSteps(null, null).Count);
        Assert.AreEqual(1, WorkflowTemplateResolver.MergeSteps(null, derived).Count, "仅基类时返回基类步骤");
    }

    // ---------- Phase 3：新增边推导（NodeRelationDeriver） ----------

    [TestMethod]
    public void NodeRelationDeriver_导出与继承实现依赖边_类型与端点正确()
    {
        var ex = NodeRelationDeriver.DeriveExportsEdge("wfA", "pub");
        Assert.AreEqual(NodeResourceTypes.Workflow, ex.SourceResourceType);
        Assert.AreEqual("wfA", ex.SourceId);
        Assert.AreEqual(NodeResourceTypes.Node, ex.TargetResourceType);
        Assert.AreEqual("wfA.pub", ex.TargetId);
        Assert.AreEqual(WorkflowReferenceTypes.Exports, ex.RefType);

        var ih = NodeRelationDeriver.DeriveInheritsEdge("derived", "base");
        Assert.AreEqual("derived", ih.SourceId);
        Assert.AreEqual("base", ih.TargetId);
        Assert.AreEqual(WorkflowReferenceTypes.Inherits, ih.RefType);

        Assert.AreEqual(WorkflowReferenceTypes.Implements, NodeRelationDeriver.DeriveImplementsEdge("wfA", "IAnswerWorkflow").RefType);
        Assert.AreEqual(WorkflowReferenceTypes.DependsOn, NodeRelationDeriver.DeriveDependsOnEdge("wfA", "res1").RefType);
    }
}
