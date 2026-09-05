using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.DslMethod.Parsing;
using Plate.DslMethod.Schema;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;
using Operation.DataStoreDriven.ReferenceSchema;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Reference.Models;
using TestInfrastructure;

namespace Agent.ReferenceSchema.Tests;

/// <summary>
/// UniversalReference 引用 Schema DSL 测试（DslMethod.Schema 扩展）：
/// 解析 [Node]/[Edge]/[Constraint] → 注册中心 → 校验（悬空/重复/dep）→ 生成 JSON 产物 → 运行时注册表 → 适配器 fail-fast。
/// </summary>
[TestClass]
[TestCategory("DslMethod")]
public class ReferenceSchemaDslTests
{
    private const string SampleDsl = """
# sample
[Node] workflow: 工作流 | display=工作流 | version=1
[Node] module: 模块 | display=模块 | version=1
[Edge] Dependency: 依赖 | src=workflow | tgt=module | strength=1.0 | display=依赖 | version=1
[Edge] Includes: 包含 | src=any | tgt=any | strength=1.0 | version=1
[Constraint] Dependency: 依赖约束 | srcType=workflow | tgtTypes=module,resource | max=unbounded
""";

    private static string RepoRoot() => RepoRootLocator.Root;

    private static string CommittedSchemaDir()
        => Path.Combine(RepoRoot(), "CsharpPrj", "Operation", "DataStoreDriven", "ReferenceSchema");

    private static ReferenceSchemaRegistry Load(string text)
    {
        var reg = new ReferenceSchemaRegistry();
        reg.Load(new[] { DslParser.Parse(text, "sample.dsl.md", "sample") });
        return reg;
    }

    // ---------- 解析 ----------

    [TestMethod]
    public void Parse_节点边约束_提取正确()
    {
        var reg = Load(SampleDsl);

        Assert.AreEqual(2, reg.Nodes.Count, "节点数");
        Assert.AreEqual(2, reg.Edges.Count, "边数");
        Assert.AreEqual(1, reg.Constraints.Count, "约束数");

        Assert.AreEqual("workflow", reg.Nodes[0].Name);
        Assert.AreEqual("工作流", reg.Nodes[0].Display);

        var dep = reg.Edges.First(e => e.Name == "Dependency");
        Assert.AreEqual("workflow", dep.Source);
        Assert.AreEqual("module", dep.Target);

        var c = reg.Constraints[0];
        Assert.AreEqual("Dependency", c.Edge);
        Assert.AreEqual("module", c.TgtTypes[0]);
        Assert.IsTrue(c.Unbounded);
    }

    // ---------- 编译验证（数据可编译验证） ----------

    [TestMethod]
    public void Validate_边端悬空节点类型_报错()
    {
        var bad = SampleDsl.Replace("src=workflow", "src=does_not_exist");
        var issues = Load(bad).Validate();

        Assert.IsTrue(issues.Any(i => i.Message.Contains("src 悬空节点类型 'does_not_exist'")),
            $"应报边端悬空，实际: {string.Join(" | ", issues)}");
    }

    [TestMethod]
    public void Validate_约束悬空Edge_报错()
    {
        var bad = SampleDsl.Replace("[Edge] Dependency", "[Edge] DependencyX"); // 约束仍引用 Dependency
        var issues = Load(bad).Validate();

        Assert.IsTrue(issues.Any(i => i.Message.Contains("引用未声明的 Edge")), "应报约束引用未声明的 Edge");
    }

    [TestMethod]
    public void Validate_重复节点_报错()
    {
        var bad = SampleDsl + "\n[Node] workflow: 重复 | display=重复 | version=1\n";
        var issues = Load(bad).Validate();

        Assert.IsTrue(issues.Any(i => i.Message.Contains("[Node] 重复定义")), "应报节点重复定义");
    }

    [TestMethod]
    public void Validate_dep目标未声明_报错()
    {
        var bad = SampleDsl + "\n[Edge] Legacy: 旧 | src=any | tgt=any | dep=replaced-by(Missing)\n";
        var issues = Load(bad).Validate();

        Assert.IsTrue(issues.Any(i => i.Message.Contains("dep 目标 'Missing' 未声明")), "应报 dep 目标未声明");
    }

    // ---------- 生成（纯编译产物） ----------

    [TestMethod]
    public void Generator_发射JSON产物与报告()
    {
        var dsl = Path.Combine(CommittedSchemaDir(), "reference-schema.dsl.md");
        var tmp = Path.Combine(Path.GetTempPath(), "refgen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = new ReferenceSchemaCodeGenerator().Generate(dsl, tmp, new ReferenceSchemaCodeGeneratorOptions { Version = "1.0" });

            Assert.IsTrue(File.Exists(result.SchemaFile), "缺少 JSON 产物");
            Assert.IsTrue(File.Exists(result.ReportFile), "缺少映射报告");
            Assert.AreEqual(0, result.Issues.Count, "提交 DSL 应校验通过");

            var schema = System.Text.Json.JsonSerializer.Deserialize<Plate.DslMethod.Schema.ReferenceSchema>(File.ReadAllText(result.SchemaFile));
            Assert.IsNotNull(schema);
            Assert.IsTrue(schema.Edges.Any(e => e.Name == "Dependency"), "产物应含 Dependency 边");
            Assert.AreEqual("1.0", schema.Version);
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }

    // ---------- 运行时注册表（可扩展升级） ----------

    [TestMethod]
    public void ReferenceTypeRegistry_从JSON加载_判定声明与废弃()
    {
        var json = File.ReadAllText(Path.Combine(CommittedSchemaDir(), "reference-schema.generated.json"));
        var registry = ReferenceTypeRegistry.LoadFromJson(json);

        Assert.IsTrue(registry.IsDeclared("Dependency"));
        Assert.IsTrue(registry.IsDeclared("dependency"), "大小写不敏感");
        Assert.IsFalse(registry.IsDeclared("NotDeclared"));
        Assert.IsFalse(registry.IsDeprecated("Dependency"));
    }

    [TestMethod]
    public void 扩展_新增Edge_重生成后接受()
    {
        var dsl = Path.Combine(CommittedSchemaDir(), "reference-schema.dsl.md");
        var tmp = Path.Combine(Path.GetTempPath(), "refgen_ext_" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = File.ReadAllText(dsl);
            var extended = source + "\n[Edge] References: 引用 | src=any | tgt=any | strength=1.0 | display=引用 | version=1\n";
            var reg = Load(extended);

            Assert.IsTrue(reg.Edges.Any(e => e.Name == "References"), "扩展行应被解析为新边");
            Assert.AreEqual(0, reg.Validate().Count, "扩展后应校验通过（文法不变）");

            new ReferenceSchemaCodeGenerator().Generate(dsl, tmp); // 提交 DSL 自身可重生成
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }

    // ---------- 工作流控制流静态化（组合 + 控制流边） ----------

    [TestMethod]
    public void 控制流边_推导Owns组合与Calls调用()
    {
        var edges = NodeRelationDeriver.DeriveWorkflowControlFlowEdges("wf", new[] { "collect", "analyze", "act" });

        // 组合 Owns：workflow → collect/analyze/act（3 条）
        // 控制流 Calls：collect→analyze、analyze→act（2 条）
        Assert.AreEqual(5, edges.Count, "Owns(3) + Calls(2)");
        Assert.AreEqual(3, edges.Count(e => e.RefType == "Owns"));
        Assert.AreEqual(2, edges.Count(e => e.RefType == "Calls"));

        var firstOwns = edges.First(e => e.RefType == "Owns");
        Assert.AreEqual("workflow", firstOwns.SourceResourceType, "组合边源为 workflow 资源");
        Assert.AreEqual("wf", firstOwns.SourceId);
        Assert.AreEqual("node", firstOwns.TargetResourceType, "组合边目标为步骤节点");
        Assert.AreEqual("wf.collect", firstOwns.TargetId, "端点 Id = {workflowId}.{nodeKey}");

        var ab = edges.First(e => e.RefType == "Calls" && e.SourceId == "wf.collect");
        Assert.AreEqual("wf.analyze", ab.TargetId, "控制流 collect → analyze");
    }

    [TestMethod]
    public void 控制流边_空步骤_返回空集()
    {
        Assert.AreEqual(0, NodeRelationDeriver.DeriveWorkflowControlFlowEdges("wf", new List<string>()).Count);
        Assert.AreEqual(0, NodeRelationDeriver.DeriveWorkflowControlFlowEdges("", new[] { "a" }).Count);
    }

    [TestMethod]
    public void 控制流边_单步骤_仅Owns无Calls()
    {
        var edges = NodeRelationDeriver.DeriveWorkflowControlFlowEdges("wf", new[] { "only" });
        Assert.AreEqual(1, edges.Count, "单步骤只有 1 条组合边");
        Assert.AreEqual("Owns", edges[0].RefType);
    }

    // ---------- 适配器 fail-fast（替代静默降级 Other） ----------

    [TestMethod]
    public async Task Store_注入注册表_未声明类型拒绝()
    {
        var registry = ReferenceTypeRegistry.LoadFromFile(Path.Combine(CommittedSchemaDir(), "reference-schema.generated.json"));
        var store = new UniversalReferenceResourceReferenceStore(new FakeManager(), registry);
        var id = new ResourceIdentifier { ResourceType = "workflow", Id = "1" };

        Assert.IsTrue(await ThrowsInvalid(() => store.AddReferenceAsync(id, id, "NotDeclaredType")),
            "未声明引用类型应 fail-fast 拒绝");
    }

    [TestMethod]
    public async Task Store_注入注册表_已声明类型放行()
    {
        var registry = ReferenceTypeRegistry.LoadFromFile(Path.Combine(CommittedSchemaDir(), "reference-schema.generated.json"));
        var manager = new FakeManager();
        var store = new UniversalReferenceResourceReferenceStore(manager, registry);
        var id = new ResourceIdentifier { ResourceType = "workflow", Id = "1" };

        await store.AddReferenceAsync(id, id, "Dependency");

        Assert.IsNotNull(manager.LastReferenceType, "已声明类型应放行到管理器");
        Assert.AreEqual(ReferenceType.Dependency, manager.LastReferenceType);
    }

    [TestMethod]
    public async Task Store_注入注册表_废弃类型拒绝()
    {
        var schema = new Plate.DslMethod.Schema.ReferenceSchema();
        schema.Edges.Add(new ReferenceEdgeType("Legacy", "any", "any", 1.0, "旧", 1, Deprecated: true, "Dependency", "", 0));
        schema.Edges.Add(new ReferenceEdgeType("Dependency", "any", "any", 1.0, "依赖", 1, Deprecated: false, null, "", 0));
        var registry = new ReferenceTypeRegistry(schema);
        var store = new UniversalReferenceResourceReferenceStore(new FakeManager(), registry);
        var id = new ResourceIdentifier { ResourceType = "workflow", Id = "1" };

        Assert.IsTrue(await ThrowsInvalid(() => store.AddReferenceAsync(id, id, "Legacy")),
            "废弃引用类型应 fail-fast 拒绝");
    }

    private static async Task<bool> ThrowsInvalid(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException) { return true; }
        return false;
    }

    /// <summary>记录最后被管理器收到的引用类型，验证校验放行后才触达底层。</summary>
    private sealed class FakeManager : IUniversalReferenceManager
    {
        public ReferenceType? LastReferenceType { get; private set; }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task AddResourceAsync(ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null) => Task.CompletedTask;
        public Task UpdateResourceAsync(ResourceIdentifier resourceId, Dictionary<string, object>? metadata = null, string? symbol = null) => Task.CompletedTask;
        public Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId) => Task.FromResult<Resource?>(null);
        public Task<List<Resource>> GetAllResourcesAsync() => Task.FromResult(new List<Resource>());
        public Task RemoveResourceAsync(ResourceIdentifier resourceId) => Task.CompletedTask;
        public Task AddReferenceAsync(ResourceIdentifier sourceResourceId, ResourceIdentifier targetResourceId, ReferenceType referenceType, string? location = null, string? description = null, double strength = 1.0)
        {
            LastReferenceType = referenceType;
            return Task.CompletedTask;
        }
        public Task RemoveReferenceAsync(ResourceIdentifier sourceResourceId, ResourceIdentifier targetResourceId, ReferenceType referenceType) => Task.CompletedTask;
        public Task<List<(ResourceIdentifier Source, ResourceIdentifier Target, ReferenceType Type, string? Location, string? Description, double Strength, bool IsIncoming)>> GetReferencesAsync(ResourceIdentifier resourceId, bool includeIncoming = true)
            => Task.FromResult(new List<(ResourceIdentifier, ResourceIdentifier, ReferenceType, string?, string?, double, bool)>());
        public Task<List<ResourceIdentifier>> GetDependenciesAsync(ResourceIdentifier resourceId, int maxDepth = 5) => Task.FromResult(new List<ResourceIdentifier>());
        public Task<List<ResourceIdentifier>> GetDependentsAsync(ResourceIdentifier resourceId, int maxDepth = 5) => Task.FromResult(new List<ResourceIdentifier>());
        public Task<List<List<ResourceIdentifier>>> FindPathsAsync(ResourceIdentifier startResourceId, ResourceIdentifier endResourceId, int maxDepth = 10) => Task.FromResult(new List<List<ResourceIdentifier>>());
        public Task<List<ResourceIdentifier>> FindShortestPathAsync(ResourceIdentifier startResourceId, ResourceIdentifier endResourceId) => Task.FromResult(new List<ResourceIdentifier>());
        public Task<List<Resource>> GetResourcesByTypeAsync(string resourceType) => Task.FromResult(new List<Resource>());
        public Task CleanupNonExistentResourcesAsync() => Task.CompletedTask;
        public bool ContainsResource(ResourceIdentifier resourceId) => false;
    }
}


