using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.ReferenceSchema;
using Operation.HierarchyMethod.Nodes.Relations;
using Operation.HierarchyMethod.Nodes.Workflows;
using Plate.DslMethod.Parsing;
using Plate.DslMethod.Schema;

namespace Agent.ReferenceSchema.Tests;

/// <summary>
/// WorkflowCompiler（② 静态编译检查器）单元测试 —— 全部基于内联 DSL 内存构造（复用 ReferenceSchemaRegistry 校验逻辑，
/// 不依赖 committed 产物 / RepoRoot）。覆盖：DSL 端口类型签名解析、TryGetPortType、图完整性/环/诊断、端口类型不匹配（CS0029）、可空守卫。
/// </summary>
[TestClass]
public class WorkflowCompilerTests
{
    private const string ValidDsl = """
        # sample
        [Node] workflow: 工作流 | display=工作流 | version=1
        [Node] node: 步骤节点 | display=节点 | version=2
            | inports=user_input:string,data:string | outports=data:string,result:json
        [Edge] Calls: 控制流 | src=any | tgt=any | strength=1.0
        [Edge] Owns: 组合 | src=any | tgt=any | strength=1.0
        [Constraint] Calls: 控制流约束 | srcType=any | tgtTypes=any | max=unbounded
        [Constraint] Owns: 组合约束 | srcType=any | tgtTypes=any | max=unbounded
        """;

    private const string MismatchDsl = """
        # sample
        [Node] workflow: 工作流 | display=工作流 | version=1
        [Node] node: 步骤节点 | display=节点 | version=2
            | inports=user_input:string,data:string | outports=data:int,result:json
        [Edge] Calls: 控制流 | src=any | tgt=any | strength=1.0
        [Edge] Owns: 组合 | src=any | tgt=any | strength=1.0
        [Constraint] Calls: 控制流约束 | srcType=any | tgtTypes=any | max=unbounded
        [Constraint] Owns: 组合约束 | srcType=any | tgtTypes=any | max=unbounded
        """;

    private const string NoPortsDsl = """
        # sample
        [Node] workflow: 工作流 | display=工作流 | version=1
        [Node] node: 步骤节点 | display=节点 | version=2
        [Edge] Calls: 控制流 | src=any | tgt=any | strength=1.0
        [Edge] Owns: 组合 | src=any | tgt=any | strength=1.0
        [Constraint] Calls: 控制流约束 | srcType=any | tgtTypes=any | max=unbounded
        [Constraint] Owns: 组合约束 | srcType=any | tgtTypes=any | max=unbounded
        """;

    private static ReferenceSchemaRegistry LoadRegistry(string text)
    {
        var reg = new ReferenceSchemaRegistry();
        reg.Load(new[] { DslParser.Parse(text, "sample.dsl.md", "sample") });
        return reg;
    }

    private static ReferenceTypeRegistry ToTypeRegistry(ReferenceSchemaRegistry reg)
        => new(reg.ToSchema());

    private static RoundSpec ChainSpec() => new("wf", new RoundStep[]
    {
        new("collect", new[] { "user_input" }, new[] { "data" }),
        new("act", new[] { "data" }, new[] { "result" }),
    });

    // ---------- ① 端口类型签名（DSL 声明 → Schema 节点属性） ----------

    [TestMethod]
    public void ResourceNodeType_解析端口类型签名_输入输出分离()
    {
        var reg = LoadRegistry(ValidDsl);
        var node = reg.Nodes.First(n => n.Name == "node");

        Assert.AreEqual("string", node.InputPorts["user_input"]);
        Assert.AreEqual("string", node.InputPorts["data"]);
        Assert.AreEqual("string", node.OutputPorts["data"]);
        Assert.AreEqual("json", node.OutputPorts["result"]);
        Assert.IsFalse(node.InputPorts.ContainsKey("result"), "result 仅输出端口，不应出现在输入侧");
    }

    [TestMethod]
    public void TryGetPortType_输入输出侧区分_未知端口返回false()
    {
        var registry = ToTypeRegistry(LoadRegistry(ValidDsl));

        Assert.IsTrue(registry.TryGetPortType("node", "data", isInput: true, out var inType));
        Assert.AreEqual("string", inType);
        Assert.IsTrue(registry.TryGetPortType("node", "data", isInput: false, out var outType));
        Assert.AreEqual("string", outType);
        Assert.IsTrue(registry.TryGetPortType("node", "result", isInput: false, out var rType));
        Assert.AreEqual("json", rType);
        Assert.IsFalse(registry.TryGetPortType("node", "result", isInput: true, out _), "result 无输入声明");
        Assert.IsFalse(registry.TryGetPortType("node", "unknown", isInput: false, out _), "未知端口");
        Assert.IsFalse(registry.TryGetPortType("missing_type", "data", isInput: false, out _), "未知节点类型");
    }

    // ---------- ② WorkflowCompiler（图完整性 / 环 / 诊断 / 类型不匹配 / 可空守卫） ----------

    [TestMethod]
    public void Compile_合法线性链_通过()
    {
        var dsl = LoadRegistry(ValidDsl);
        var result = WorkflowCompiler.Compile(ChainSpec(), ToTypeRegistry(dsl), dsl);

        Assert.IsTrue(result.IsValid, "合法链不应有错误");
        Assert.AreEqual(0, result.ErrorCount);
    }

    [TestMethod]
    public void Compile_端口类型不匹配_报CS0029()
    {
        var dsl = LoadRegistry(MismatchDsl); // 输出 data:int，输入 data:string
        var result = WorkflowCompiler.Compile(ChainSpec(), ToTypeRegistry(dsl), dsl);

        Assert.IsFalse(result.IsValid, "类型不匹配应编译失败");
        Assert.IsTrue(result.Errors.Any(e => e.Contains("CS0029") && e.Contains("data")),
            $"应报 CS0029 类型不匹配，实际: {string.Join(" | ", result.Errors)}");
    }

    [TestMethod]
    public void Compile_未声明端口类型_跳过并Info()
    {
        var dsl = LoadRegistry(NoPortsDsl); // node 未声明 inports/outports
        var result = WorkflowCompiler.Compile(ChainSpec(), ToTypeRegistry(dsl), dsl);

        Assert.IsTrue(result.IsValid, "无类型声明不应报错");
        Assert.IsTrue(result.Info.Any(i => i.Contains("未声明类型签名")),
            $"应提示端口未声明类型签名（可空守卫），实际: {string.Join(" | ", result.Info)}");
    }

    [TestMethod]
    public void Compile_未装配registry_跳过类型检查()
    {
        var dsl = LoadRegistry(ValidDsl);
        var result = WorkflowCompiler.Compile(ChainSpec(), registry: null, dsl);

        Assert.IsTrue(result.IsValid, "registry 未装配不应报错");
        Assert.IsTrue(result.Info.Any(i => i.Contains("跳过端口类型检查")),
            $"应提示跳过端口类型检查，实际: {string.Join(" | ", result.Info)}");
    }

    [TestMethod]
    public void Compile_空Spec_返回Info诊断()
    {
        var result = WorkflowCompiler.Compile(new RoundSpec("empty", new List<RoundStep>()), registry: null, dslSchema: null);

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.Info.Any(i => i.Contains("无步骤")), "空 spec 应提示无步骤");
    }
}

