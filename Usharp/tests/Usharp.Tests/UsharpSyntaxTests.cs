using Microsoft.VisualStudio.TestTools.UnitTesting;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Tests;

/// <summary>
/// 语法层测试：验证「复用既有解析器」成立（四前缀经 DslConstants 注册、DslParser 直接解析）
/// 与四类声明的投影正确性（键型/值型空间、矩阵行、流步骤与子链）。
/// </summary>
[TestClass]
public sealed class UsharpSyntaxTests
{
    private const string Doc = """
        [Space] Step: kind=keys
          start
          guard_input
          cline_execute

        [Space] Param: kind=values
          progress:ordinal(0..3)
          halt:flag

        [Matrix] M: over=Param
          progress = progress + 1
          halt = halt

        [Flow] F: over=Step
          step start
          step cline_execute ~ Sub

        [Flow] Sub: over=Step
          step start

        [Ref] R: Step:cline_execute | src=F | tgt=cline_execute | intent=consume
        """;

    [TestMethod]
    public void Prefixes_AreRegisteredInSharedRegistry_AndReuseDslParser()
    {
        Assert.IsTrue(UsharpParser.PrefixesRegistered(), "四前缀须已注册（复用 Plate.DslMethod 解析器）");

        var doc = UsharpParser.Parse(Doc, "syntax.ushar");
        // 四类声明均由既有 DslParser 解析（零自有行解析器）
        Assert.AreEqual(2, UsharpSyntaxProjector.Spaces(doc).Count);
        Assert.AreEqual(1, UsharpSyntaxProjector.Matrices(doc).Count);
        Assert.AreEqual(2, UsharpSyntaxProjector.Flows(doc).Count);
        Assert.AreEqual(1, UsharpSyntaxProjector.Refs(doc).Count);
    }

    [TestMethod]
    public void Space_KeyKind_And_ValueKind_AreProjected()
    {
        var doc = UsharpParser.Parse(Doc, "syntax.ushar");
        var spaces = UsharpSyntaxProjector.Spaces(doc);

        var step = spaces.Single(s => s.SpaceId == "Step");
        Assert.AreEqual(UsharpSpaceKind.Keys, step.Kind);
        CollectionAssert.AreEquivalent(new[] { "start", "guard_input", "cline_execute" },
            step.Axes.Select(a => a.Name).ToArray());
        Assert.IsTrue(step.Axes.All(a => a.IsKey), "键型空间坐标无值域");

        var param = spaces.Single(s => s.SpaceId == "Param");
        Assert.AreEqual(UsharpSpaceKind.Values, param.Kind);
        var progress = param.Axes.Single(a => a.Name == "progress");
        Assert.AreEqual("ordinal", progress.ValueKind);
        Assert.AreEqual(0d, progress.Min);
        Assert.AreEqual(3d, progress.Max);
        Assert.AreEqual("flag", param.Axes.Single(a => a.Name == "halt").ValueKind);
    }

    [TestMethod]
    public void Matrix_Rows_And_InputRefs_AreProjected()
    {
        var m = UsharpSyntaxProjector.Matrices(UsharpParser.Parse(Doc, "syntax.ushar")).Single();
        Assert.AreEqual("Param", m.OverSpaceId);
        CollectionAssert.AreEquivalent(new[] { "progress", "halt" }, m.Rows.Select(r => r.OutputKey).ToArray());
        var carry = m.Rows.Single(r => r.OutputKey == "halt");
        Assert.AreEqual("halt", carry.Expression);
        CollectionAssert.AreEquivalent(new[] { "halt" }, carry.InputRefs.ToArray());
    }

    [TestMethod]
    public void Flow_Steps_And_SubFlowLink_AreProjected()
    {
        var flows = UsharpSyntaxProjector.Flows(UsharpParser.Parse(Doc, "syntax.ushar"));
        var f = flows.Single(x => x.FlowId == "F");
        Assert.AreEqual("Step", f.OverSpaceId);
        Assert.AreEqual(2, f.Steps.Count);
        Assert.IsNull(f.Steps[0].SubFlow, "普通步骤无子链");
        Assert.AreEqual("Sub", f.Steps[1].SubFlow, "`~` 即工作流连接链");
    }

    [TestMethod]
    public void Ref_ShortForm_SpaceColonKey_IsProjected()
    {
        var r = UsharpSyntaxProjector.Refs(UsharpParser.Parse(Doc, "syntax.ushar")).Single();
        Assert.AreEqual("Step", r.SpaceId);
        Assert.AreEqual("cline_execute", r.Key);
        Assert.AreEqual("F", r.Source);
        Assert.AreEqual("consume", r.Intent);
    }
}
