using Microsoft.VisualStudio.TestTools.UnitTesting;
using Usharp.Semantics;

namespace Usharp.Tests;

/// <summary>
/// 闸A（装载期坐标校验）与「引用即边」测试 —— 本方案的可验证收益点：
/// 未声明坐标一律编译报错（U-V3）；原先静默失效的链声明（原 C1）由 US1005 拦下（U-V6）。
/// </summary>
[TestClass]
public sealed class UsharpGateATests
{
    private static UsharpCompilation Compile(string text)
        => UsharpCompilation.Compile(new[] { ("gate-a.ushar", text) });

    private const string ValidSpace = """
        [Space] Step: kind=keys
          start
          cline_execute
        """;

    [TestMethod]
    public void GateA_FlowStepUndeclared_IsError()
    {
        // 原 C1 等价场景：链声明了一个指向「未声明步骤」的目标（现状：静默丢弃 → 本语言：编译错误）
        var c = Compile(ValidSpace + """


            [Flow] F: over=Step
              step start
              step does_not_exist
            """);

        Assert.IsTrue(c.HasErrors);
        Assert.IsTrue(c.Errors.Any(d => d.Code == UsharpCodes.FlowStepUndeclared),
            "未声明步骤键必须报 US1005（闸A）:\n" + c.Report());
    }

    [TestMethod]
    public void GateA_MatrixRowInputUndeclared_IsError()
    {
        var c = Compile("""
            [Space] Param: kind=values
              progress:ordinal(0..3)

            [Matrix] M: over=Param
              progress = progress + missingAxis
            """);

        Assert.IsTrue(c.Errors.Any(d => d.Code == UsharpCodes.MatrixRowInputUndeclared),
            "矩阵行引用未声明坐标必须报 US1004（闸A）:\n" + c.Report());
    }

    [TestMethod]
    public void GateA_MatrixRowOutputUndeclared_IsError()
    {
        var c = Compile("""
            [Space] Param: kind=values
              progress:ordinal(0..3)

            [Matrix] M: over=Param
              notDeclared = progress
            """);

        Assert.IsTrue(c.Errors.Any(d => d.Code == UsharpCodes.MatrixRowOutUndeclared), c.Report());
    }

    [TestMethod]
    public void GateA_RefCoordinateUndeclared_IsError()
    {
        var c = Compile(ValidSpace + """


            [Ref] R: Step:ghost_step | src=F
            """);

        Assert.IsTrue(c.Errors.Any(d => d.Code == UsharpCodes.CoordUndeclared), c.Report());
    }

    [TestMethod]
    public void GateA_UnknownSpace_IsError()
    {
        var c = Compile("""
            [Flow] F: over=NoSuchSpace
              step anything
            """);

        Assert.IsTrue(c.Errors.Any(d => d.Code == UsharpCodes.SpaceUnknown), c.Report());
    }

    [TestMethod]
    public void GateA_SubFlowUnknown_IsError()
    {
        var c = Compile(ValidSpace + """


            [Flow] F: over=Step
              step start ~ GhostFlow
            """);

        Assert.IsTrue(c.Errors.Any(d => d.Code == UsharpCodes.SubFlowUnknown), c.Report());
    }

    [TestMethod]
    public void DuplicateSpace_IsError()
    {
        var c = Compile(ValidSpace + "\n" + ValidSpace);
        Assert.IsTrue(c.Errors.Any(d => d.Code == UsharpCodes.SpaceDup), c.Report());
    }

    [TestMethod]
    public void ReferenceIsEdge_ProducesStepInvokesAndCrEdges()
    {
        var c = Compile("""
            [Space] Step: kind=keys
              start
              cline_execute

            [Flow] Sub: over=Step
              step start

            [Flow] F: over=Step
              step start
              step cline_execute ~ Sub

            [Ref] R: Step:cline_execute | src=F | tgt=cline_execute | intent=consume
            """);

        Assert.IsFalse(c.HasErrors, c.Report());
        Assert.IsTrue(c.Edges.Any(e => e.Type == UsharpGraphBuilder.StepRef && e.Target == "cline_execute"));
        Assert.IsTrue(c.Edges.Any(e => e.Type == UsharpGraphBuilder.Invokes && e.Target == "Sub"),
            "工作流连接链须落为 Invokes 边（引用即边）");
        Assert.IsTrue(c.Edges.Any(e => e.Type == "consume" && e.Key == "cline_execute"),
            "[Ref] 未给 edge= 时边类型取 intent（intent 即边类型）");
    }

    [TestMethod]
    public void Sample_AgentLoop_CompilesWithoutErrors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "agent-loop.ushar");
        Assert.IsTrue(File.Exists(path), $"样例缺失（测试输出未见 samples）：{path}");

        var c = UsharpCompilation.Compile(new[] { (path, File.ReadAllText(path)) });
        Assert.IsFalse(c.HasErrors, c.Report());

        var s = c.Snapshot();
        Assert.AreEqual(4, s.Spaces, "样例声明 4 个空间");
        Assert.AreEqual(16, s.Coordinates, "闸A 覆盖面 = 已声明坐标总数");
        Assert.AreEqual(1, s.Matrices);
        Assert.AreEqual(3, s.Flows);
        Assert.AreEqual(13, s.Edges, "引用即边：1 Ref + 1 Over + 3 RowRef + 8 StepRef/Invokes");
        Assert.IsTrue(c.Edges.Any(e => e.Type == UsharpGraphBuilder.Over && e.Target == "Param"));
        Assert.IsTrue(c.Edges.Any(e => e.Type == UsharpGraphBuilder.RowRef && e.Target == "progress"));
        Assert.IsTrue(c.Edges.Any(e => e.Type == UsharpGraphBuilder.Invokes && e.Target == "SessionCline"),
            "AgentLoop.cline_execute ~ SessionCline：工作流连接链");
        Assert.IsTrue(c.Edges.Any(e => e.Type == UsharpGraphBuilder.RefersTo && e.Key == "task_orchestrate"),
            "edge=RefersTo 显式覆盖边类型");
    }
}
