using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.ScopeFramework;

namespace Share.ScopeFramework.Tests;

/// <summary>
/// ScopeCommandTranslator 单测：展开序列正确性 + 短路分支覆盖 + 命名派生。
/// 执行端口用桩记录收到的 JSON（不执行真实 RunJsonAsync）。
/// </summary>
[TestClass]
public class ScopeCommandTranslatorTests
{
    private sealed class RecordingPort
    {
        public List<string> Received { get; } = new();

        public Func<string, Task<string>> Handler => async json =>
        {
            Received.Add(json);
            return await Task.FromResult($"OK:{json.Length}").ConfigureAwait(false);
        };
    }

    private static ScopeCommand Cmd(
        string cmd,
        string? taskKey = "tp_x",
        string? workflowId = null,
        string? name = null,
        string? nodeType = null,
        object? payload = null,
        IReadOnlyList<string>? relatedNodes = null,
        bool guard = true,
        bool pool = true)
        => new(new ScopeContext(cmd, taskKey, workflowId, name, nodeType, payload, relatedNodes,
            GraphGuardAssembled: guard, TaskPoolAssembled: pool));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string[] Actions(ScopeCommandResult r)
        => r.ProducedJson.Select(j => Parse(j).GetProperty("action").GetString()!).ToArray();

    [TestMethod]
    public async Task AddNode_GuardAssembled_ExpandsToWriteResourceEdge()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        var result = await translator.ExecuteAsync(
            Cmd("addNode", name: "source", nodeType: "output", payload: new { data = "x" })).ConfigureAwait(false);

        // S1 node.write → S2 addResource → S3 addReference
        CollectionAssert.AreEqual(
            new[] { "node", "addResource", "addReference" }, Actions(result));
        Assert.AreEqual(3, result.ProducedJson.Count);

        // S1: nodeKey=nodes.{wf}.{name}, threadId={wf}
        var write = Parse(result.ProducedJson[0]);
        Assert.AreEqual("nodes.tp_x.source", write.GetProperty("nodeKey").GetString());
        Assert.AreEqual("output", write.GetProperty("nodeType").GetString());
        Assert.AreEqual("tp_x", write.GetProperty("threadId").GetString());

        // S2: 图端点 id = {wf}.{name}（无 nodes 前缀）
        var res = Parse(result.ProducedJson[1]);
        Assert.AreEqual("tp_x.source", res.GetProperty("id").GetString());

        // S3: task→node 引用边
        var refJson = Parse(result.ProducedJson[2]);
        Assert.AreEqual("task", refJson.GetProperty("sourceType").GetString());
        Assert.AreEqual("tp_x", refJson.GetProperty("sourceId").GetString());
        Assert.AreEqual("node", refJson.GetProperty("targetType").GetString());
        Assert.AreEqual("tp_x.source", refJson.GetProperty("targetId").GetString());
        Assert.AreEqual("node_to_node", refJson.GetProperty("refType").GetString());

        Assert.AreEqual(0, result.Diagnostics.Count(d => d.Kind == DiagnosticKind.Skip));
    }

    [TestMethod]
    public async Task AddNode_NodeTypeNode_GuardAssembled_SkipsS2()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        // nodeType=node 且守卫已装配：WriteGuardPort Put 自动建图端点 → S2 跳过（无 addResource）
        var result = await translator.ExecuteAsync(
            Cmd("addNode", name: "n1", nodeType: "node", payload: new { v = 1 })).ConfigureAwait(false);

        CollectionAssert.AreEqual(new[] { "node", "addReference" }, Actions(result));
        Assert.IsTrue(result.Diagnostics.Any(d => d.Kind == DiagnosticKind.Skip && d.Step.Id == "S2"));
    }

    [TestMethod]
    public async Task AddNode_GuardNotAssembled_S2FallsBack_S3DeclaresAndSkipsEdge()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        // 守卫未装配：S2 补建 addResource；S3 仅写 ur_nodes 声明节点（图边子项跳过并记诊断）
        var result = await translator.ExecuteAsync(
            Cmd("addNode", name: "source", nodeType: "output", payload: new { data = "x" }, guard: false)).ConfigureAwait(false);

        CollectionAssert.AreEqual(new[] { "node", "addResource", "node" }, Actions(result));

        // S3 声明节点：NodeType=relation
        Assert.AreEqual("relation", Parse(result.ProducedJson[2]).GetProperty("nodeType").GetString());

        // 诊断含 S3 图边子项 Skip
        Assert.IsTrue(result.Diagnostics.Any(d => d.Kind == DiagnosticKind.Skip && d.Step.Id == "S3" && d.Message.Contains("图守卫未装配")));
        // 不阻断其余步骤：node write + addResource 均产出 Ok
        Assert.IsTrue(result.Diagnostics.Any(d => d.Kind == DiagnosticKind.Ok && d.Step.Id == "S1"));
        Assert.IsTrue(result.Diagnostics.Any(d => d.Kind == DiagnosticKind.Ok && d.Step.Id == "S2"));
    }

    [TestMethod]
    public async Task GetNodes_ProducesNodesByTaskQuery()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        var result = await translator.ExecuteAsync(Cmd("getNodes")).ConfigureAwait(false);

        var query = Parse(result.ProducedJson.Single());
        Assert.AreEqual("query", query.GetProperty("action").GetString());
        Assert.AreEqual("nodesByTask", query.GetProperty("target").GetString());
        Assert.AreEqual("tp_x", query.GetProperty("key").GetString());
        Assert.AreEqual("tp_x", query.GetProperty("threadId").GetString());
    }

    [TestMethod]
    public async Task RemoveNode_CascadesToEdgeCleanup_WhenGuardAssembled()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        var result = await translator.ExecuteAsync(
            Cmd("removeNode", name: "source", nodeType: "output")).ConfigureAwait(false);

        CollectionAssert.AreEqual(new[] { "node", "removeResource" }, Actions(result));
        Assert.AreEqual("delete", Parse(result.ProducedJson[0]).GetProperty("op").GetString());
        Assert.AreEqual("nodes.tp_x.source", Parse(result.ProducedJson[0]).GetProperty("nodeKey").GetString());
    }

    [TestMethod]
    public async Task CreateTask_WithRelatedNodes_GuardAssembled_EdgesThenEnqueue()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        var result = await translator.ExecuteAsync(
            Cmd("createTask", relatedNodes: new[] { "tp_x.a", "tp_x.b" })).ConfigureAwait(false);

        // S3（每条 relatedNode 建边）×2 → S7 enqueue
        CollectionAssert.AreEqual(new[] { "addReference", "addReference", "task" }, Actions(result));
        Assert.AreEqual("enqueue", Parse(result.ProducedJson[2]).GetProperty("op").GetString());
        Assert.AreEqual("tp_x", Parse(result.ProducedJson[2]).GetProperty("taskKey").GetString());
    }

    [TestMethod]
    public async Task CreateTask_NoRelatedNodes_OnlyEnqueue()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        var result = await translator.ExecuteAsync(Cmd("createTask")).ConfigureAwait(false);

        CollectionAssert.AreEqual(new[] { "task" }, Actions(result));
    }

    [TestMethod]
    public async Task CreateTask_GuardNotAssembled_SkipsEdgeSubItem_WithDiagnostic()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        var result = await translator.ExecuteAsync(
            Cmd("createTask", relatedNodes: new[] { "tp_x.a" }, guard: false, pool: true)).ConfigureAwait(false);

        // 无 addReference（图边子项整体跳过）；仅 enqueue
        CollectionAssert.AreEqual(new[] { "task" }, Actions(result));
        Assert.IsTrue(result.Diagnostics.Any(d => d.Kind == DiagnosticKind.Skip && d.Step.Id == "S3" && d.Message.Contains("createTask")));
    }

    [TestMethod]
    public async Task NoScope_Passthrough_RawJsonEchoed()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        const string raw = "{ \"action\":\"node\",\"op\":\"write\",\"nodeKey\":\"nodes.agent-round.x\" }";
        var scope = new ScopeContext("addNode", TaskKey: null, RawRequestJson: raw);
        var result = await translator.ExecuteAsync(new ScopeCommand(scope)).ConfigureAwait(false);

        CollectionAssert.AreEqual(new[] { raw }, result.ProducedJson.ToArray());
    }

    [TestMethod]
    public void Preview_ExpandsWithoutExecuting()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        var preview = translator.Preview(
            Cmd("addNode", name: "source", nodeType: "output", payload: new { v = 1 }));

        CollectionAssert.AreEqual(new[] { "S1", "S2", "S3" }, preview.Select(s => s.Id).ToArray());
        Assert.AreEqual(0, port.Received.Count); // dry-run 不执行
    }

    [TestMethod]
    public void NamingRule_Default_NodeKeyPrefixByNodeType()
    {
        var rule = new DefaultScopeNamingRule();
        var scope = new ScopeContext("addNode", "tp_x", Name: "n1", NodeType: "output");

        Assert.AreEqual("tp_x", rule.ResolveWorkflowId(scope));
        Assert.AreEqual("nodes.tp_x.n1", rule.ResolveNodeKey(scope, "output", "n1"));
        Assert.AreEqual("events.tp_x.n1", rule.ResolveNodeKey(scope, "event", "n1"));
        Assert.AreEqual("keymapping.tp_x.n1", rule.ResolveNodeKey(scope, "keymapping", "n1"));
        Assert.AreEqual("config.n1", rule.ResolveNodeKey(scope, "config", "n1"));
        Assert.AreEqual("tp_x", rule.ResolveThreadId(scope));
    }

    [TestMethod]
    public void NamingRule_WorkflowOverride_TaskIsolationRespectsOverride()
    {
        var rule = new DefaultScopeNamingRule();
        var scope = new ScopeContext("addNode", "tp_x", WorkflowId: "custom", Name: "n1", NodeType: "output");

        Assert.AreEqual("custom", rule.ResolveWorkflowId(scope));
        Assert.AreEqual("nodes.custom.n1", rule.ResolveNodeKey(scope, "output", "n1"));
        Assert.AreEqual("custom", rule.ResolveThreadId(scope));
    }

    [TestMethod]
    public async Task Enqueue_NoMode_DefaultsDeterministic()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        var result = await translator.ExecuteAsync(Cmd("createTask")).ConfigureAwait(false);

        // D1 修复：未显式调度模式 → 默认 Deterministic（零 LLM 纯簿记，不再硬编码 Hybrid）。
        var enqueue = Parse(result.ProducedJson.Single());
        Assert.AreEqual("enqueue", enqueue.GetProperty("op").GetString());
        Assert.AreEqual("Deterministic", enqueue.GetProperty("mode").GetString());
    }

    [TestMethod]
    public async Task Enqueue_RelatedNodes_Normalized()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        // P2：S7 入队经 NormalizeNodeKeys 归一化 relatedNodes（过滤空白 + Trim + 去重保序）。
        var scope = new ScopeContext("createTask", "tp_x", TaskPoolAssembled: true,
            RelatedNodes: new[] { " a ", "b", "a", "" });
        var result = await translator.ExecuteAsync(new ScopeCommand(scope)).ConfigureAwait(false);

        var enqueue = Parse(result.ProducedJson.Single());
        var related = enqueue.GetProperty("relatedNodes").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "a", "b" }, related);
    }

    [TestMethod]
    public void Enqueue_DefaultSchedulingMode_SingleSource()
    {
        // 单一源锚点：入队空 mode 默认 Deterministic 的常量（框架 S7 与 Agent 执行侧共享，消除三处字面量）。
        Assert.AreEqual("Deterministic", ScopeEnqueueDefaults.DefaultSchedulingMode);
    }

    [TestMethod]
    public async Task Enqueue_ExplicitMode_PassesThrough()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        // 显式 SchedulingMode=Hybrid → 透传到 enqueue mode。
        var scope = new ScopeContext("createTask", "tp_x", TaskPoolAssembled: true, SchedulingMode: "Hybrid");
        var result = await translator.ExecuteAsync(new ScopeCommand(scope)).ConfigureAwait(false);

        var enqueue = Parse(result.ProducedJson.Single());
        Assert.AreEqual("Hybrid", enqueue.GetProperty("mode").GetString());
    }

    [TestMethod]
    public async Task NodeWrite_ThreadIdStable()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        // 换序列化器（ScopeAction + ScopeActionSerializer）后 nodeKey/threadId 语义不变。
        var result = await translator.ExecuteAsync(
            Cmd("addNode", name: "source", nodeType: "output", payload: new { data = "x" })).ConfigureAwait(false);

        var write = Parse(result.ProducedJson[0]);
        Assert.AreEqual("nodes.tp_x.source", write.GetProperty("nodeKey").GetString());
        Assert.AreEqual("output", write.GetProperty("nodeType").GetString());
        Assert.AreEqual("tp_x", write.GetProperty("threadId").GetString());
    }

    [TestMethod]
    public void Registry_RegisterCustom_OverridesDefault()
    {
        var registry = new ScopeCommandPathRegistry();
        var addNodeCmd = Cmd("addNode", name: "n1");
        var original = registry.Resolve(addNodeCmd); // 空注册表未命中 → 默认 S0 直通

        // 注册自定义 myCmd → 复用内置 getNodes 的步骤
        var customPath = ScopeCommandPathRegistry.CreateDefault();
        var customSteps = customPath.Resolve(Cmd("getNodes"));
        Assert.IsTrue(registry.TryRegister("myCmd", _ => customSteps));
        Assert.AreEqual("S4", registry.Resolve(Cmd("myCmd")).Single().Id);

        // 用内置默认注册表验证：与已注册命令名冲突 → 返回 false 且原路径不变（TryRegister* 惯例：不抛、保默认）
        var defaultRegistry = ScopeCommandPathRegistry.CreateDefault();
        var originalAddNode = defaultRegistry.Resolve(addNodeCmd);
        Assert.IsFalse(defaultRegistry.TryRegister("addNode", _ => customSteps));
        CollectionAssert.AreEqual(
            originalAddNode.Select(s => s.Id).ToArray(),
            defaultRegistry.Resolve(addNodeCmd).Select(s => s.Id).ToArray());
    }

    [TestMethod]
    public async Task Preview_MatchesExecutionActions()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        var cmd = Cmd("addNode", name: "source", nodeType: "output", payload: new { v = 1 });

        // PreviewActions（同一 ProduceActions 描述）与 ExecuteAsync 收到 JSON 的动作序列必须一致。
        var previewNames = translator.PreviewActions(cmd).Select(a => a.Action).ToArray();
        var result = await translator.ExecuteAsync(cmd).ConfigureAwait(false);
        var executedNames = result.ProducedJson.Select(j => Parse(j).GetProperty("action").GetString()!).ToArray();

        CollectionAssert.AreEqual(previewNames, executedNames);
    }

    [TestMethod]
    public void Naming_NormalizeNodeKeys_FiltersTrimsDedups()
    {
        var rule = new DefaultScopeNamingRule();

        // 过滤空白 + Trim + 去重（保序）；null → 空列表。
        var normalized = rule.NormalizeNodeKeys(new[] { " a ", "b", "a", null, "" });
        CollectionAssert.AreEqual(new[] { "a", "b" }, normalized.ToArray());

        Assert.AreEqual(0, rule.NormalizeNodeKeys(null).Count);
    }

    [TestMethod]
    public void Diagnostic_StructuredEntry_FormatsBackToLegacyText()
    {
        // 结构化诊断条目可格式化回既有 "[kind] stepId: message" 文本，兼容字符串消费方。
        var step = ScopeCommandPathRegistry.CreateDefault()
            .Resolve(Cmd("getNodes")).Single();
        var entry = new DiagnosticEntry(step, DiagnosticKind.Skip, "前置条件不满足");

        Assert.AreEqual("S4", entry.Step.Id);
        Assert.AreEqual(DiagnosticKind.Skip, entry.Kind);
        Assert.AreEqual("[skip] S4: 前置条件不满足", entry.Format());
    }

    [TestMethod]
    public void ITemplateExpander_Contract_ScopeTranslatorExpandsLikePreview()
    {
        var port = new RecordingPort();
        var translator = new ScopeCommandTranslator(new DefaultScopeNamingRule(), port.Handler);

        // 实现 ITemplateExpander<ScopeCommand, IStep>：Expand 即 Preview（dry-run 展开为线性步骤序列）。
        Assert.IsInstanceOfType<ITemplateExpander<ScopeCommand, IStep>>(translator);
        var expander = (ITemplateExpander<ScopeCommand, IStep>)translator;

        var cmd = Cmd("addNode", name: "source", nodeType: "output", payload: new { v = 1 });
        CollectionAssert.AreEqual(
            translator.Preview(cmd).Select(s => s.Id).ToArray(),
            expander.Expand(cmd).Select(s => s.Id).ToArray());
    }

    [TestMethod]
    public void L0Naming_EmptyWorkflowId_DefaultsAgentRound()
    {
        var rule = new L0ScopeNamingRule();

        // L0 空/空白 workflowId → agent-round；非空 Trim（裸字符串便捷入口）。
        Assert.AreEqual("agent-round", rule.ResolveWorkflowId((string?)null));
        Assert.AreEqual("agent-round", rule.ResolveWorkflowId("  "));
        Assert.AreEqual("custom", rule.ResolveWorkflowId(" custom "));

        // 经 ScopeContext（空 WorkflowId）→ 同样 agent-round；threadId 取 L0 workflowId。
        var scope = new ScopeContext("attach", "tp_x");
        Assert.AreEqual("agent-round", rule.ResolveWorkflowId(scope));
        Assert.AreEqual("agent-round", rule.ResolveThreadId(scope));
    }
}
