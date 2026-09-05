using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.DslMethod.Parsing;
using Agent.Standardization.Compiler;
using Agent.Standardization.Runtime;
using TestInfrastructure;

namespace Agent.StorageTemplate.Tests;

/// <summary>
/// 储存模板 DSL 测试（Agent.Standardization.Compiler 扩展）：
/// 解析 [Template] → 注册中心 → 校验（未知表/重复/dep）→ 生成 JSON 产物 → 运行时模板路由（无分类动作）。
/// 验证「模板驱动直接储存」语义：命中且 mode/表允许 → Store=true；mode=none 或未命中 → 不入库。
/// </summary>
[TestClass]
[TestCategory("DslMethod")]
public class StorageTemplateDslTests
{
    private const string SampleDsl = """
    # sample
    [Template] execution_result: 执行结果 | kind=result | table=ur_outputs | mode=append | fields=status,payload | retention=days:30 | decay=hash | keep_fail=true | version=1
    [Template] decision_trace: 决策迹 | kind=decision | table=ur_events | mode=append | retention=days:180 | decay=summary | budget_items=10000 | version=1
    [Template] reference: 引用关系 | kind=reference | mode=none | rebuildable=true | version=1
    """;

    private static string RepoRoot() => RepoRootLocator.Root;

    private static string CommittedTemplateDir()
        => Path.Combine(RepoRoot(), "Projects", "Agent", "src", "Agent.Runtime", "Standardization", "generated");

    private static string CommittedDslDir()
        => Path.Combine(RepoRoot(), "Projects", "Agent", "src", "Agent.Runtime", "Standardization", "Dsl");

    private static StorageTemplateRegistry Load(string text)
    {
        var reg = new StorageTemplateRegistry();
        reg.Load(new[] { DslParser.Parse(text, "sample.dsl.md", "sample") });
        return reg;
    }

    // ---------- 解析 ----------

    [TestMethod]
    public void Parse_模板_提取正确()
    {
        var reg = Load(SampleDsl);

        Assert.AreEqual(3, reg.Templates.Count, "模板数");

        var result = reg.Templates.First(t => t.Name == "execution_result");
        Assert.AreEqual("result", result.Kind);
        Assert.AreEqual("ur_outputs", result.Table);
        Assert.AreEqual(StorageMode.AppendOnly, result.Mode);
        Assert.AreEqual(2, result.Fields.Count);
        Assert.AreEqual(StorageDecay.HashOnly, result.Decay);
        Assert.IsTrue(result.KeepFail);
        Assert.AreEqual(30, result.Retention!.Days);
        Assert.IsTrue(result.Stores);
    }

    [TestMethod]
    public void Parse_不入库模板_Stores为false()
    {
        var reg = Load(SampleDsl);
        var reference = reg.Templates.First(t => t.Name == "reference");

        Assert.AreEqual(StorageMode.None, reference.Mode);
        Assert.IsNull(reference.Table);
        Assert.IsTrue(reference.Rebuildable);
        Assert.IsFalse(reference.Stores, "mode=none 且无表 → 不入库");
    }

    // ---------- 编译验证（数据可编译验证） ----------

    [TestMethod]
    public void Validate_未知表_报错()
    {
        var bad = SampleDsl.Replace("table=ur_outputs", "table=ur_bogus");
        var issues = Load(bad).Validate();

        Assert.IsTrue(issues.Any(i => i.Message.Contains("未知表 'ur_bogus'")),
            $"应报未知表，实际: {string.Join(" | ", issues)}");
    }

    [TestMethod]
    public void Validate_重复模板_报错()
    {
        var bad = SampleDsl + "\n[Template] execution_result: 重复 | table=ur_outputs | version=1\n";
        var issues = Load(bad).Validate();

        Assert.IsTrue(issues.Any(i => i.Message.Contains("[Template] 重复定义")), "应报模板重复定义");
    }

    [TestMethod]
    public void Validate_dep目标未声明_报错()
    {
        var bad = SampleDsl + "\n[Template] legacy: 旧 | mode=none | dep=replaced-by(Missing)\n";
        var issues = Load(bad).Validate();

        Assert.IsTrue(issues.Any(i => i.Message.Contains("dep 目标 'Missing' 未声明")), "应报 dep 目标未声明");
    }

    // ---------- 生成（纯编译产物） ----------

    [TestMethod]
    public void Generator_发射JSON产物与报告()
    {
        var dsl = Path.Combine(CommittedDslDir(), "storage-template.dsl.md");
        var tmp = Path.Combine(Path.GetTempPath(), "stgen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = new StorageTemplateCodeGenerator().Generate(dsl, tmp, new StorageTemplateCodeGeneratorOptions { Version = "1.0" });

            Assert.IsTrue(File.Exists(result.SchemaFile), "缺少 JSON 产物");
            Assert.IsTrue(File.Exists(result.ReportFile), "缺少映射报告");
            Assert.AreEqual(0, result.Issues.Count, "提交 DSL 应校验通过");

            var coll = System.Text.Json.JsonSerializer.Deserialize<StorageTemplateCollection>(File.ReadAllText(result.SchemaFile));
            Assert.IsNotNull(coll);
            Assert.IsTrue(coll.Templates.Any(t => t.Name == "execution_result"), "产物应含 execution_result 模板");
            Assert.AreEqual("1.0", coll.Version);
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }

    // ---------- 运行时模板路由（无分类动作） ----------

    [TestMethod]
    public void Store_命中入库模板_路由到契约并Store为true()
    {
        var store = StorageTemplateStore.LoadFromFile(Path.Combine(CommittedTemplateDir(), "storage-template.generated.json"));

        var hit = store.Route(new StorageDataPoint("execution_result", Source: "Guardrails", PayloadJson: "{}"));

        Assert.IsNotNull(hit, "命中 execution_result 模板");
        Assert.AreEqual("ur_nodes", hit.Table, "三库收敛后执行结果落 ur_nodes（NodeType 判别）");
        Assert.IsTrue(hit.Store, "命中且可入库");
        Assert.IsTrue(hit.KeepFail, "失败结果永不衰减契约");
        Assert.AreEqual(30, hit.Retention!.Days);
    }

    [TestMethod]
    public void Store_命中不入库模板_Store为false()
    {
        var store = StorageTemplateStore.LoadFromFile(Path.Combine(CommittedTemplateDir(), "storage-template.generated.json"));

        // 三库收敛后 reference 已可入库（ur_nodes + upsert）；不入库契约现由 debug_log/token_stream（mode=none）承载。
        var hit = store.Route(new StorageDataPoint("token_stream", Source: null, PayloadJson: "{}"));

        Assert.IsNotNull(hit, "命中 token_stream 模板（不入库契约）");
        Assert.IsFalse(hit.Store, "mode=none → 不入库");
    }

    [TestMethod]
    public void Store_未命中模板_返回null()
    {
        var store = StorageTemplateStore.LoadFromFile(Path.Combine(CommittedTemplateDir(), "storage-template.generated.json"));

        Assert.IsNull(store.Route(new StorageDataPoint("not_declared", Source: null, PayloadJson: "{}")), "未声明类别 → 无模板 → 不入库");
    }

    [TestMethod]
    public void Store_Parse_按fields解析列()
    {
        var store = StorageTemplateStore.LoadFromFile(Path.Combine(CommittedTemplateDir(), "storage-template.generated.json"));
        var directive = store.Route(new StorageDataPoint("execution_result", Source: null, PayloadJson: "{}"))!;

        var cols = store.Parse(directive, """{ "status": "success", "payload": { "text": "ok" }, "ignored": 1 }""");

        Assert.AreEqual("success", cols["status"]);
        Assert.AreEqual("""{ "text": "ok" }""", cols["payload"]);
        Assert.IsFalse(cols.ContainsKey("ignored"), "仅解析模板 fields 声明的列");
    }

    // ---------- 提交 DSL 可重生成（可重放、幂等；产物写入临时目录，不污染提交树） ----------

    [TestMethod]
    public void Regenerate_提交DSL_可重生成且校验通过()
    {
        var dsl = Path.Combine(CommittedDslDir(), "storage-template.dsl.md");
        var tmp = Path.Combine(Path.GetTempPath(), "stgen_reg_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = new StorageTemplateCodeGenerator().Generate(dsl, tmp, new StorageTemplateCodeGeneratorOptions { Version = "1.0" });

            Assert.IsTrue(File.Exists(Path.Combine(tmp, "storage-template.generated.json")), "缺少重生成 JSON 产物");
            Assert.IsTrue(File.Exists(Path.Combine(tmp, "storage-template-report.txt")), "缺少重生成报告");
            Assert.AreEqual(0, result.Issues.Count, "提交 DSL 校验应通过");

            // 与提交产物逐字节一致 = 幂等可重放（提交产物为纯编译产物，禁止漂移）
            var committed = Path.Combine(CommittedTemplateDir(), "storage-template.generated.json");
            Assert.IsTrue(File.Exists(committed), "提交产物缺失");
            Assert.AreEqual(
                File.ReadAllText(committed),
                File.ReadAllText(Path.Combine(tmp, "storage-template.generated.json")),
                "重生成产物应与提交产物一致");
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }
}
