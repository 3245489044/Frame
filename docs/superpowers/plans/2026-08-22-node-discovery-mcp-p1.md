# 节点发现与精炼 MCP — P1 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地设计文档《节点发现精炼MCP设计.md》P1 两件事——task 作用域候选投影（`node_project`）与预算内注入+压缩（`node_select`），以独立 MCP 工具形式交付，**零侵入现有 Agent(C#) 框架**。

**Architecture:** 投影核心逻辑（task 作用域寻址、拓扑序）已存在于 C# `ReferenceGrounding.ProjectTaskNodesAsync`；本计划在 `Agent.StorageBridge.exe` 新增 `node-project` / `node-select` 两个只读子命令（复用现有图协调器 + 节点库，不复制逻辑），MCPBridge 侧新增 `node_project` / `node_select` 两个本地工具（薄中继，直接 spawn exe，解析 `{status,message,data,errors}` 信封）。TS 侧不直连 LiteDB，杜绝双实现。

**Tech Stack:** C# .NET 10（Agent.StorageBridge + DataStoreDriven + LiteDB 引用图）、TypeScript（MCPBridge / mcp-framework）、MSTest、child_process。

**范围（P1）:** task 作用域寻址 + 候选投影 + 预算内注入 + 压缩。**不涉及**：LLM 结构化取用声明协议（P2）、node_validate 读时自愈（P2）、版本增量收敛与 checkpoint（P3）、RefType 扩展与状态机（P3）、方案 B 统一收口（P3）。框架侧 `ResolveInputsAsync` 等代码**一行不改**。

---

## 文件结构

| 动作 | 路径 | 职责 |
|---|---|---|
| 新建 | `Projects\Agent\src\Agent.StorageBridge\Discovery\NodeDiscoveryModels.cs` | 候选/注入 DTO（CandidateItem、ProjectionResult、InjectionResult） |
| 新建 | `Projects\Agent\src\Agent.StorageBridge\Discovery\NodeDiscoveryHelpers.cs` | 节点记录解析（双键形）、一句话摘要、token 估算 |
| 新建 | `Projects\Agent\src\Agent.StorageBridge\Discovery\NodeProjectionService.cs` | projection 层：task 作用域候选索引 + 摘要 + 预算 |
| 新建 | `Projects\Agent\src\Agent.StorageBridge\Discovery\NodeInjectionService.cs` | selection 层：载荷加载 + 逐节点压缩 + 预算注入 |
| 修改 | `Projects\Agent\src\Agent.StorageBridge\Program.cs` | 路由 `node-project` / `node-select` 子命令 |
| 新建 | `CentralTest\AgentTest\Agent.Persistence.Tests\NodeDiscoveryProjectionTests.cs` | 投影服务测试 |
| 新建 | `CentralTest\AgentTest\Agent.Persistence.Tests\NodeInjectionServiceTests.cs` | 注入服务测试 |
| 修改 | `CentralTest\AgentTest\Agent.Persistence.Tests\Agent.Persistence.Tests.csproj` | 添加 `Agent.StorageBridge` 项目引用 |
| 新建 | `TypeScriptPrj\TsWorkspace\MCPBridge\tools-definitions\node_project.json` | 工具定义（projection） |
| 新建 | `TypeScriptPrj\TsWorkspace\MCPBridge\tools-definitions\node_select.json` | 工具定义（selection） |
| 新建 | `TypeScriptPrj\TsWorkspace\MCPBridge\src\tools\storage-bridge.ts` | 共享：定位 exe + 解析信封 + 读 nodeDiscovery 配置 |
| 新建 | `TypeScriptPrj\TsWorkspace\MCPBridge\src\tools\node-project.ts` | handler（薄中继） |
| 新建 | `TypeScriptPrj\TsWorkspace\MCPBridge\src\tools\node-select.ts` | handler（薄中继） |
| 修改 | `TypeScriptPrj\TsWorkspace\MCPBridge\mcp-bridge.config.json` | 新增 `nodeDiscovery` 配置段 |

关键既有设施（只复用不修改）：
- `ReferenceGrounding.ProjectTaskNodesAsync`（[ReferenceGrounding.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Core/ReferenceGrounding.cs#L156-L195)）：cross_db_link 挂载 + Kahn 拓扑序，已实现 task 作用域寻址。
- `ReferenceGraphCoordinator.CreateAsync`：LiteDB 引用图（测试中直接使用，与 `ReferenceGroundingWriteThroughTests` 一致）。
- `NodeOutputKeys.NodeKey(wf, node)` → `nodes.{wf}.{node}`（输出记录键）；业务节点记录键为裸 `{node}`。
- `NodeOutputCodec.DecodeDoc` / `EncodeValue`：端口载荷编解码，用于一句话摘要。
- 测试基础设施：`InMemoryNodeStore`（`Operation.DataStoreDriven.Core`）、`NodeTypes`（`Operation.DataStoreDriven.Abstractions`）。

---

### Task 1: C# 数据模型与工具（DTO + Helpers）

**Files:**
- Create: `D:\Project\Program\WorkFlowPrj\Projects\Agent\src\Agent.StorageBridge\Discovery\NodeDiscoveryModels.cs`
- Create: `D:\Project\Program\WorkFlowPrj\Projects\Agent\src\Agent.StorageBridge\Discovery\NodeDiscoveryHelpers.cs`

- [ ] **Step 1: 新建 `Discovery\NodeDiscoveryModels.cs`**

```csharp
namespace Agent.StorageBridge.Discovery;

/// <summary>候选节点索引项（projection 输出，不携带载荷）。</summary>
public sealed class CandidateItem
{
    public string NodeKey { get; set; } = string.Empty;
    public int Order { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int EstTokens { get; set; }
}

/// <summary>node-project 输出（候选清单 + 累计预算外露）。</summary>
public sealed class ProjectionResult
{
    public string TaskKey { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public List<CandidateItem> Candidates { get; set; } = new();
    public int TotalEstTokens { get; set; }
}

/// <summary>node-select 输出（预算内注入文本 + 命中/丢弃清单）。</summary>
public sealed class InjectionResult
{
    public string TaskKey { get; set; } = string.Empty;
    public int BudgetTokens { get; set; }
    public int UsedTokens { get; set; }
    public List<string> Loaded { get; set; } = new();
    public List<string> Dropped { get; set; } = new();
    public string Injected { get; set; } = string.Empty;
}
```

- [ ] **Step 2: 新建 `Discovery\NodeDiscoveryHelpers.cs`**

```csharp
using Agent.Axioms.Nodes;
using Operation.DataStoreDriven.Abstractions;

namespace Agent.StorageBridge.Discovery;

/// <summary>节点记录解析：给定 workflow + 节点键，按「输出记录 → 业务节点记录」顺序解析（两种键形）。
/// 图端点 Id = {wf}.{node}（NodeRelationFactory.NodeId），GetTaskNodeKeysAsync 剥离 {wf}. 前缀返回裸 nodeKey；
/// 输出记录 NodeKey = nodes.{wf}.{node}（NodeOutputKeys），业务节点记录 NodeKey = 裸 {node}。</summary>
public static class NodeRecordResolver
{
    public static async Task<NodeRecord?> ResolveAsync(INodeStore nodes, string workflowId, string nodeKey, CancellationToken ct)
    {
        var outputKey = NodeOutputKeys.NodeKey(workflowId, nodeKey);
        var output = await nodes.GetAsync(outputKey, ct).ConfigureAwait(false);
        if (output is not null) return output;
        return await nodes.GetAsync(nodeKey, ct).ConfigureAwait(false);
    }
}

/// <summary>一句话摘要：NodeOutputDoc 端口取前 3 个（key=截断值），非文档载荷取首 120 字符。</summary>
public static class NodeSummaryBuilder
{
    private const int MaxPorts = 3;
    private const int MaxPortValueChars = 40;
    private const int MaxFallbackChars = 120;

    public static string Build(NodeRecord? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.PayloadJson)) return string.Empty;
        var doc = NodeOutputCodec.DecodeDoc(node.PayloadJson);
        if (doc.Ports.Count == 0)
        {
            var t = node.PayloadJson.Trim();
            return t.Length <= MaxFallbackChars ? t : t[..MaxFallbackChars] + "…";
        }
        var parts = new List<string>();
        foreach (var (k, v) in doc.Ports.Take(MaxPorts))
        {
            var text = v.Value?.ToString() ?? "null";
            text = text.Length <= MaxPortValueChars ? text : text[..MaxPortValueChars] + "…";
            parts.Add($"{k}={text}");
        }
        return string.Join(" | ", parts);
    }
}

/// <summary>token 估算（字符近似，4 字符 ≈ 1 token；对齐配置 compressor=tiktoken-truncate 的朴素实现）。</summary>
public static class TokenEstimator
{
    public static int Estimate(int charCount) => (int)Math.Ceiling(Math.Max(0, charCount) / 4.0);
    public static int Estimate(string? text) => Estimate(text?.Length ?? 0);
}
```

- [ ] **Step 3: 编译验证**

Run:
```powershell
dotnet build "D:\Project\Program\WorkFlowPrj\Projects\Agent\src\Agent.StorageBridge\Agent.StorageBridge.csproj" -c Debug
```
Expected: BUILD SUCCEEDED（两个新文件仅编译，尚未接入命令）。

- [ ] **Step 4: Commit**

```bash
git add Projects/Agent/src/Agent.StorageBridge/Discovery/NodeDiscoveryModels.cs Projects/Agent/src/Agent.StorageBridge/Discovery/NodeDiscoveryHelpers.cs
git commit -m "feat(node-discovery): add DTOs and helpers for node projection/injection"
```

---

### Task 2: 投影服务 `NodeProjectionService`（TDD）

**Files:**
- Create: `D:\Project\Program\WorkFlowPrj\Projects\Agent\src\Agent.StorageBridge\Discovery\NodeProjectionService.cs`
- Create: `D:\Project\Program\WorkFlowPrj\CentralTest\AgentTest\Agent.Persistence.Tests\NodeDiscoveryProjectionTests.cs`
- Modify: `D:\Project\Program\WorkFlowPrj\CentralTest\AgentTest\Agent.Persistence.Tests\Agent.Persistence.Tests.csproj`

- [ ] **Step 1: 写失败测试（含 csproj 引用）**

修改 `Agent.Persistence.Tests.csproj`，在 `<ItemGroup>`（ProjectReference 组）末尾添加：

```xml
    <!-- 节点发现 P1：投影/注入服务测试需要引用 Agent.StorageBridge（Exe 项目） -->
    <ProjectReference Include="$(AgentProjectRoot)\src\Agent.StorageBridge\Agent.StorageBridge.csproj" />
```

新建 `NodeDiscoveryProjectionTests.cs`：

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Axioms.Nodes;
using Agent.StorageBridge.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;

namespace Agent.Persistence.Tests;

[TestClass]
public class NodeDiscoveryProjectionTests
{
    private const string Wf = "agent-round";
    private static string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "NodeDiscoveryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Teardown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* 清理忽略 */ }
    }

    [TestMethod]
    public async Task ProjectTaskNodes_ReturnsOnlyTaskScopedCandidatesWithSummary()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(Path.Combine(_dir, "graph.db"));
        var grounding = new ReferenceGrounding(graph);
        var store = new InMemoryNodeStore();

        await WriteNodeAsync(store, grounding, "task-A", "n1", "result=hello");
        await WriteNodeAsync(store, grounding, "task-A", "n2", "result=world");
        await WriteNodeAsync(store, grounding, "task-B", "other", "result=other-task");

        var service = new NodeProjectionService(grounding, store);
        var result = await service.ProjectAsync("task-A", Wf, 0, CancellationToken.None);

        Assert.AreEqual(2, result.Candidates.Count, "只应投影 task-A 挂载节点，排除跨 task 污染");
        var keys = result.Candidates.Select(c => c.NodeKey).OrderBy(x => x).ToList();
        CollectionAssert.AreEqual(new[] { "n1", "n2" }, keys);
        Assert.IsTrue(result.Candidates.All(c => !string.IsNullOrWhiteSpace(c.Summary)), "候选应携带一句话摘要");
        Assert.IsTrue(result.TotalEstTokens > 0, "候选应携带累计 token 估算");
    }

    [TestMethod]
    public async Task ProjectTaskNodes_LimitTruncatesCandidateList()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(Path.Combine(_dir, "graph.db"));
        var grounding = new ReferenceGrounding(graph);
        var store = new InMemoryNodeStore();
        await WriteNodeAsync(store, grounding, "task-A", "n1", "a");
        await WriteNodeAsync(store, grounding, "task-A", "n2", "b");
        await WriteNodeAsync(store, grounding, "task-A", "n3", "c");

        var service = new NodeProjectionService(grounding, store);
        var result = await service.ProjectAsync("task-A", Wf, 2, CancellationToken.None);

        Assert.AreEqual(2, result.Candidates.Count);
    }

    [TestMethod]
    public async Task ProjectTaskNodes_UnknownTaskReturnsEmpty()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(Path.Combine(_dir, "graph.db"));
        var grounding = new ReferenceGrounding(graph);
        var service = new NodeProjectionService(grounding, new InMemoryNodeStore());

        var result = await service.ProjectAsync("task-unknown", Wf, 0, CancellationToken.None);

        Assert.AreEqual(0, result.Candidates.Count);
    }

    /// <summary>写业务节点 + 输出记录（摘要来源）+ task→node cross_db_link 边。</summary>
    private static async Task WriteNodeAsync(InMemoryNodeStore store, ReferenceGrounding grounding, string taskKey, string nodeKey, string portValue)
    {
        await store.PutAsync(new NodeRecord
        {
            NodeKey = nodeKey,
            NodeType = NodeTypes.Node,
            ThreadId = Wf,
            Status = "executed",
            PayloadJson = $"{{\"Symbol\":\"{nodeKey}\"}}",
        }).ConfigureAwait(false);

        var doc = new NodeOutputDoc();
        doc.Ports[nodeKey] = NodeOutputCodec.EncodeValue(portValue);
        await store.PutAsync(new NodeRecord
        {
            NodeKey = NodeOutputKeys.NodeKey(Wf, nodeKey),
            NodeType = NodeTypes.Output,
            ThreadId = Wf,
            Status = "completed",
            PayloadJson = NodeOutputCodec.EncodeDoc(doc),
        }).ConfigureAwait(false);

        await grounding.EnsureTaskNodeLinkAsync(taskKey, Wf, nodeKey, CancellationToken.None).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:
```powershell
dotnet test "D:\Project\Program\WorkFlowPrj\CentralTest\AgentTest\Agent.Persistence.Tests\Agent.Persistence.Tests.csproj" -c Debug --filter "FullyQualifiedName~NodeDiscoveryProjectionTests"
```
Expected: FAIL（编译错误：`NodeProjectionService` 不存在）。

- [ ] **Step 3: 实现 `Discovery\NodeProjectionService.cs`**

```csharp
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;

namespace Agent.StorageBridge.Discovery;

/// <summary>
/// 投影层（projection，框架确定性）：给定 task，沿 LiteDB 引用边投影候选节点索引（不含载荷），
/// 携带一句话摘要 + 大小 + token 估算，供 selection 层限额注入。task 作用域寻址经
/// <see cref="ReferenceGrounding.ProjectTaskNodesAsync"/>（cross_db_link 挂载 + NodeToNode 拓扑）承载，根除跨 task 污染。
/// </summary>
public sealed class NodeProjectionService
{
    private readonly ReferenceGrounding _grounding;
    private readonly INodeStore _nodes;

    public NodeProjectionService(ReferenceGrounding grounding, INodeStore nodes)
    {
        _grounding = grounding;
        _nodes = nodes;
    }

    public async Task<ProjectionResult> ProjectAsync(string taskKey, string workflowId, int limit, CancellationToken ct)
    {
        var result = new ProjectionResult { TaskKey = taskKey, WorkflowId = workflowId };
        var projected = await _grounding.ProjectTaskNodesAsync(taskKey, workflowId, ct).ConfigureAwait(false);
        if (projected.Count == 0) return result;

        var order = 0;
        foreach (var item in projected)
        {
            if (limit > 0 && result.Candidates.Count >= limit) break;
            var node = await NodeRecordResolver.ResolveAsync(_nodes, workflowId, item.NodeKey, ct).ConfigureAwait(false);
            var size = node?.PayloadJson?.Length ?? 0;
            result.Candidates.Add(new CandidateItem
            {
                NodeKey = item.NodeKey,
                Order = ++order,
                NodeType = node?.NodeType ?? string.Empty,
                Status = node?.Status ?? string.Empty,
                UpdatedAt = node?.UpdatedAt ?? string.Empty,
                SizeBytes = size,
                Summary = NodeSummaryBuilder.Build(node),
                EstTokens = TokenEstimator.Estimate(size),
            });
        }
        result.TotalEstTokens = result.Candidates.Sum(c => c.EstTokens);
        return result;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: 同上 Step 2 命令。
Expected: PASS（3 个测试全绿）。

- [ ] **Step 5: Commit**

```bash
git add Projects/Agent/src/Agent.StorageBridge/Discovery/NodeProjectionService.cs CentralTest/AgentTest/Agent.Persistence.Tests/NodeDiscoveryProjectionTests.cs CentralTest/AgentTest/Agent.Persistence.Tests/Agent.Persistence.Tests.csproj
git commit -m "feat(node-discovery): task-scoped candidate projection service"
```

---

### Task 3: 注入服务 `NodeInjectionService`（TDD）

**Files:**
- Create: `D:\Project\Program\WorkFlowPrj\Projects\Agent\src\Agent.StorageBridge\Discovery\NodeInjectionService.cs`
- Create: `D:\Project\Program\WorkFlowPrj\CentralTest\AgentTest\Agent.Persistence.Tests\NodeInjectionServiceTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `NodeInjectionServiceTests.cs`：

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Axioms.Nodes;
using Agent.StorageBridge.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;

namespace Agent.Persistence.Tests;

[TestClass]
public class NodeInjectionServiceTests
{
    private const string Wf = "agent-round";

    [TestMethod]
    public async Task InjectAsync_LoadsSelectedNodesPayloadsWithinBudget()
    {
        var store = new InMemoryNodeStore();
        await store.PutAsync(new NodeRecord { NodeKey = NodeOutputKeys.NodeKey(Wf, "n1"), NodeType = NodeTypes.Output, Status = "completed", PayloadJson = new string('x', 1000) }).ConfigureAwait(false);
        await store.PutAsync(new NodeRecord { NodeKey = NodeOutputKeys.NodeKey(Wf, "n2"), NodeType = NodeTypes.Output, Status = "completed", PayloadJson = new string('y', 1000) }).ConfigureAwait(false);

        var service = new NodeInjectionService(store, defaultBudgetTokens: 6000);
        var result = await service.InjectAsync(Wf, new[] { "n1", "n2" }, null, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(2, result.Loaded.Count);
        Assert.AreEqual(0, result.Dropped.Count);
        Assert.IsTrue(result.UsedTokens <= 6000, "注入不得超出预算");
        Assert.IsTrue(result.Injected.Contains("n1") && result.Injected.Contains("n2"));
    }

    [TestMethod]
    public async Task InjectAsync_DropsNodesExceedingBudget()
    {
        var store = new InMemoryNodeStore();
        // 每个载荷 20000 字符 ≈ 5000 tokens；预算 3000 → 仅 big1 可截断容纳，big2 被 dropped。
        await store.PutAsync(new NodeRecord { NodeKey = NodeOutputKeys.NodeKey(Wf, "big1"), NodeType = NodeTypes.Output, PayloadJson = new string('a', 20000) }).ConfigureAwait(false);
        await store.PutAsync(new NodeRecord { NodeKey = NodeOutputKeys.NodeKey(Wf, "big2"), NodeType = NodeTypes.Output, PayloadJson = new string('b', 20000) }).ConfigureAwait(false);

        var service = new NodeInjectionService(store, defaultBudgetTokens: 3000);
        var result = await service.InjectAsync(Wf, new[] { "big1", "big2" }, null, CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(result.UsedTokens <= 3000, "注入不得超出预算");
        Assert.AreEqual(1, result.Dropped.Count, "超出预算的节点应被 dropped");
        Assert.IsTrue(result.Injected.Contains("[truncated]"), "超限载荷应截断注入而非丢弃");
    }

    [TestMethod]
    public async Task InjectAsync_MissingNodeIsDropped()
    {
        var store = new InMemoryNodeStore();
        var service = new NodeInjectionService(store, defaultBudgetTokens: 6000);

        var result = await service.InjectAsync(Wf, new[] { "ghost" }, null, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, result.Dropped.Count);
        Assert.AreEqual(0, result.Loaded.Count);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:
```powershell
dotnet test "D:\Project\Program\WorkFlowPrj\CentralTest\AgentTest\Agent.Persistence.Tests\Agent.Persistence.Tests.csproj" -c Debug --filter "FullyQualifiedName~NodeInjectionServiceTests"
```
Expected: FAIL（编译错误：`NodeInjectionService` 不存在）。

- [ ] **Step 3: 实现 `Discovery\NodeInjectionService.cs`**

```csharp
using System.Text;
using Operation.DataStoreDriven.Abstractions;

namespace Agent.StorageBridge.Discovery;

/// <summary>
/// 选择层（selection，预算内注入）：给定 select 节点键集，从节点库加载载荷、逐节点压缩、按 token_budget 限额注入。
/// 框架侧强制预算（默认 6000 tokens）；超出预算的节点按序 dropped。实现「给得更少但更对」。
/// </summary>
public sealed class NodeInjectionService
{
    private readonly INodeStore _nodes;
    private readonly int _defaultBudgetTokens;

    public NodeInjectionService(INodeStore nodes, int defaultBudgetTokens = 6000)
    {
        _nodes = nodes;
        _defaultBudgetTokens = defaultBudgetTokens > 0 ? defaultBudgetTokens : 6000;
    }

    public async Task<InjectionResult> InjectAsync(string workflowId, IReadOnlyList<string> select, int? budgetTokens, CancellationToken ct)
    {
        var budget = budgetTokens is > 0 ? budgetTokens.Value : _defaultBudgetTokens;
        var result = new InjectionResult { BudgetTokens = budget };
        var remaining = budget;
        var sb = new StringBuilder();

        foreach (var key in select)
        {
            var node = await NodeRecordResolver.ResolveAsync(_nodes, workflowId, key, ct).ConfigureAwait(false);
            if (node is null) { result.Dropped.Add($"{key}(missing)"); continue; }

            var header = $"### {key} [{node.NodeType}/{node.Status}] {node.UpdatedAt}\n";
            var payload = node.PayloadJson ?? "{}";
            var headerTokens = TokenEstimator.Estimate(header.Length);

            if (headerTokens >= remaining)
            {
                result.Dropped.Add(key);
                continue;
            }

            // 剩余字符配额：剩余 token * 4（扣除 header 与末尾换行；截断标记另计）。
            const string marker = "…[truncated]";
            var allowedChars = remaining * 4 - header.Length - 1;
            var truncated = payload.Length > allowedChars;
            if (truncated)
                payload = payload[..Math.Max(0, allowedChars - marker.Length)] + marker;

            var block = header + payload + "\n";
            var blockTokens = TokenEstimator.Estimate(block.Length);
            if (blockTokens > remaining) { result.Dropped.Add(key); continue; } // 防御：估算边界

            sb.Append(block);
            remaining -= blockTokens;
            result.UsedTokens += blockTokens;
            result.Loaded.Add(key);
        }

        result.Injected = sb.ToString();
        return result;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: 同上 Step 2 命令。
Expected: PASS（3 个测试全绿）。

- [ ] **Step 5: Commit**

```bash
git add Projects/Agent/src/Agent.StorageBridge/Discovery/NodeInjectionService.cs CentralTest/AgentTest/Agent.Persistence.Tests/NodeInjectionServiceTests.cs
git commit -m "feat(node-discovery): budgeted injection with per-node compression"
```

---

### Task 4: StorageBridge 子命令路由 `node-project` / `node-select`

**Files:**
- Modify: `D:\Project\Program\WorkFlowPrj\Projects\Agent\src\Agent.StorageBridge\Program.cs`

- [ ] **Step 1: 添加 using 与命令路由**

在 `Program.cs` 顶部 using 区追加：

```csharp
using Agent.StorageBridge.Discovery;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;
```

在 `Main` 的 `command switch` 中 `"help" => Help(a),` 之前插入两行：

```csharp
            "node-project" => await NodeProjectAsync(a).ConfigureAwait(false),
            "node-select" => await NodeSelectAsync(a).ConfigureAwait(false),
```

- [ ] **Step 2: 添加两个命令方法 + 图守卫解析助手（置于 `QueryAsync` 方法之后、`ResolveRouteTable` 之前）**

```csharp
    // ---- 节点发现：projection（候选投影）----

    private static async Task<int> NodeProjectAsync(List<string> a)
    {
        var taskKey = Get(a, "--taskKey");
        var workflowId = Get(a, "--workflowId") ?? string.Empty;
        var limit = int.TryParse(Get(a, "--limit"), out var l) ? l : 0;
        if (string.IsNullOrWhiteSpace(taskKey))
        {
            Write(new Envelope("failure", "param validation failed", null, new[] { "TaskKey_required" }));
            return 1;
        }

        try
        {
            await using var db = await DatabaseBootstrapper.TryBootstrapAsync(null, CancellationToken.None).ConfigureAwait(false);
            if (db is null)
            {
                Write(new Envelope("failure", "database not available", null, new[] { "storage_db_missing" }));
                return 1;
            }
            var service = new NodeProjectionService(ResolveGrounding(db), db.Engine.DataStore.Nodes);
            var data = await service.ProjectAsync(taskKey, workflowId, limit, CancellationToken.None).ConfigureAwait(false);
            Write(new Envelope("success", $"projected task={taskKey} candidates={data.Candidates.Count}", data, Array.Empty<string>()));
            return 0;
        }
        catch (Exception ex)
        {
            Write(new Envelope("failure", ex.Message, null, new[] { "storage_bridge_error" }));
            return 1;
        }
    }

    // ---- 节点发现：selection（预算内注入）----

    private static async Task<int> NodeSelectAsync(List<string> a)
    {
        var taskKey = Get(a, "--taskKey") ?? string.Empty;
        var workflowId = Get(a, "--workflowId") ?? string.Empty;
        var selectRaw = Get(a, "--select");
        var budget = int.TryParse(Get(a, "--budgetTokens"), out var b) ? b : 0;
        if (string.IsNullOrWhiteSpace(selectRaw) || ParseSelect(selectRaw).Count == 0)
        {
            Write(new Envelope("failure", "param validation failed: --select 需为节点键 JSON 数组", null, new[] { "Select_required" }));
            return 1;
        }

        try
        {
            await using var db = await DatabaseBootstrapper.TryBootstrapAsync(null, CancellationToken.None).ConfigureAwait(false);
            if (db is null)
            {
                Write(new Envelope("failure", "database not available", null, new[] { "storage_db_missing" }));
                return 1;
            }
            var service = new NodeInjectionService(db.Engine.DataStore.Nodes);
            var data = await service.InjectAsync(workflowId, ParseSelect(selectRaw), budget, CancellationToken.None).ConfigureAwait(false);
            data.TaskKey = taskKey;
            Write(new Envelope("success", $"selected {data.Loaded.Count} nodes, dropped {data.Dropped.Count}", data, Array.Empty<string>()));
            return 0;
        }
        catch (Exception ex)
        {
            Write(new Envelope("failure", ex.Message, null, new[] { "storage_bridge_error" }));
            return 1;
        }
    }

    /// <summary>装配图守卫：db.Resources 运行时为 ReferenceGraphCoordinator（DatabaseBootstrapper 注入），
    /// 节点库装配为投影载荷源。非协调器实现 → 明确报错（不静默降级）。</summary>
    private static ReferenceGrounding ResolveGrounding(DatabaseModule db)
    {
        if (db.Resources is not ReferenceGraphCoordinator coordinator)
            throw new InvalidOperationException("Resources 未装配为 ReferenceGraphCoordinator（node-discovery 依赖图协调器）。");
        return new ReferenceGrounding(coordinator) { NodeStore = db.Engine.DataStore.Nodes };
    }

    private static List<string> ParseSelect(string raw)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(raw);
            return parsed?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList() ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
```

- [ ] **Step 3: 编译验证**

Run:
```powershell
dotnet build "D:\Project\Program\WorkFlowPrj\Projects\Agent\src\Agent.StorageBridge\Agent.StorageBridge.csproj" -c Debug
```
Expected: BUILD SUCCEEDED。

- [ ] **Step 4: 回归测试（既有 + 新增）**

Run:
```powershell
dotnet test "D:\Project\Program\WorkFlowPrj\CentralTest\AgentTest\Agent.Persistence.Tests\Agent.Persistence.Tests.csproj" -c Debug --filter "FullyQualifiedName~NodeDiscoveryProjectionTests|FullyQualifiedName~NodeInjectionServiceTests"
```
Expected: PASS（6 个测试全绿）。

- [ ] **Step 5: Commit**

```bash
git add Projects/Agent/src/Agent.StorageBridge/Program.cs
git commit -m "feat(node-discovery): route node-project/node-select storage bridge subcommands"
```

---

### Task 5: CLI 冒烟验证（真实库）

**Files:** 无（验证步骤）

- [ ] **Step 1: 用真实 taskKey 跑投影**

先用 `get-resource` 确认桥可用，再投影（taskKey 取一个真实会话/任务键，workflowId 取 `agent-round`）：

```powershell
& "D:\Project\Program\WorkFlowPrj\Agent\BuildOutput\Debug\Agent.StorageBridge.exe" node-project --taskKey "YOUR_TASK_KEY" --workflowId "agent-round"
```
Expected: `{"status":"success",...,"data":{"taskKey":"...","candidates":[...],"totalEstTokens":N}}`。
- 若该 task 无挂载节点 → `candidates: []`（链路正常，说明无数据而非报错）。
- 若跨 task 污染存在 → 出现他 task 节点键（应修复链路数据，而非本代码）。

- [ ] **Step 2: 用真实 select 跑注入**

```powershell
& "D:\Project\Program\WorkFlowPrj\Agent\BuildOutput\Debug\Agent.StorageBridge.exe" node-select --taskKey "YOUR_TASK_KEY" --workflowId "agent-round" --select "[\"n1\",\"n2\"]" --budgetTokens 6000
```
Expected: `data.injected` 含 `### n1 [...]` 块，`usedTokens <= 6000`，超限节点进 `dropped`。

- [ ] **Step 3: 非法参数冒烟**

```powershell
& "D:\Project\Program\WorkFlowPrj\Agent\BuildOutput\Debug\Agent.StorageBridge.exe" node-project
& "D:\Project\Program\WorkFlowPrj\Agent\BuildOutput\Debug\Agent.StorageBridge.exe" node-select --select "[]"
```
Expected: 均返回 `status:"failure"` + 对应错误码（`TaskKey_required` / `Select_required`），退出码 1。

---

### Task 6: TS 工具定义 + handler（`node_project` / `node_select`）

**Files:**
- Create: `D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge\src\tools\storage-bridge.ts`
- Create: `D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge\src\tools\node-project.ts`
- Create: `D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge\src\tools\node-select.ts`
- Create: `D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge\tools-definitions\node_project.json`
- Create: `D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge\tools-definitions\node_select.json`

- [ ] **Step 1: 新建共享助手 `src\tools\storage-bridge.ts`**

```typescript
/**
 * storage-bridge — Agent.StorageBridge.exe 调用助手
 *
 * node-discovery 工具经此模块定位并调用 Agent.StorageBridge.exe 的 node-* 子命令，
 * 解析 {status,message,data,errors} 信封。路径从 mcp-bridge.config.json 的 paths + nodeDiscovery 读取，
 * 缺省回退仓库约定路径 Agent\BuildOutput\Debug\Agent.StorageBridge.exe。
 */

import { execFileSync } from "child_process";
import * as fs from "fs";
import * as path from "path";
import { loadPathsConfig } from "../utils/paths.js";

export interface BridgeEnvelope {
  status: string;
  message: string;
  data: unknown;
  errors: string[];
}

export interface NodeDiscoveryConfig {
  enabled: boolean;
  defaultBudgetTokens: number;
  storageHost?: string;
}

const DEFAULT_BRIDGE_REL = path.join("Agent", "BuildOutput", "Debug", "Agent.StorageBridge.exe");

/** 读取 nodeDiscovery 配置段（缺省返回默认值；配置解析失败不抛错）。 */
export function readNodeDiscovery(): NodeDiscoveryConfig {
  const cfg = loadPathsConfig();
  const configPath = path.resolve(cfg.projectRoot, cfg.bridgeRoot, cfg.configFile);
  try {
    const raw = JSON.parse(fs.readFileSync(configPath, "utf-8")) as Record<string, any>;
    const nd = (raw?.nodeDiscovery ?? {}) as Record<string, any>;
    return {
      enabled: nd.enabled ?? true,
      defaultBudgetTokens: typeof nd.defaultBudgetTokens === "number" ? nd.defaultBudgetTokens : 6000,
      storageHost: nd.storageHost,
    };
  } catch {
    return { enabled: true, defaultBudgetTokens: 6000 };
  }
}

/** 定位 StorageBridge 可执行文件绝对路径（配置 nodeDiscovery.storageHost 优先，否则仓库默认）。 */
export function resolveBridgeExe(): string {
  const cfg = loadPathsConfig();
  const projectRoot = path.resolve(cfg.projectRoot);
  const custom = readNodeDiscovery().storageHost;
  const rel = custom || DEFAULT_BRIDGE_REL;
  const full = path.resolve(projectRoot, rel);
  if (!fs.existsSync(full)) {
    throw new Error(`Agent.StorageBridge.exe 未找到: ${full}（先构建 C# 侧 Agent.sln）`);
  }
  return full;
}

/** 执行 StorageBridge 子命令，返回信封（失败抛错）。非空参数才透传，空字符串跳过。 */
export function invokeBridge(subcommand: string, args: Record<string, string>): BridgeEnvelope {
  const exe = resolveBridgeExe();
  const argv = [subcommand];
  for (const [k, v] of Object.entries(args)) {
    if (v === undefined || v === null || v === "") continue;
    argv.push(`--${k}`, v);
  }
  const stdout = execFileSync(exe, argv, { encoding: "utf-8", maxBuffer: 64 * 1024 * 1024 });
  let parsed: BridgeEnvelope;
  try {
    parsed = JSON.parse(stdout) as BridgeEnvelope;
  } catch {
    throw new Error(`node-discovery ${subcommand} 输出非 JSON 信封: ${stdout.slice(0, 200)}`);
  }
  if (parsed.status !== "success") {
    throw new Error(`node-discovery ${subcommand} 失败: ${parsed.message} ${parsed.errors.join(",")}`);
  }
  return parsed;
}
```

- [ ] **Step 2: 新建 handler `src\tools\node-project.ts`**

```typescript
/**
 * node-project — 候选投影（projection 层，框架确定性）
 *
 * 给定 taskKey，沿 LiteDB 引用边投影本 task 候选节点索引（不携带载荷）：
 * task 作用域寻址（cross_db_link）+ NodeToNode 拓扑序 + 一句话摘要 + token 估算。
 */

import { invokeBridge, readNodeDiscovery } from "./storage-bridge.js";

export interface NodeProjectArgs {
  taskKey: string;
  workflowId?: string;
  limit?: number;
}

export async function nodeProject(args: NodeProjectArgs): Promise<string> {
  const nd = readNodeDiscovery();
  if (!nd.enabled) {
    return JSON.stringify({ status: "disabled", message: "nodeDiscovery.enabled=false" }, null, 2);
  }
  const envelope = invokeBridge("node-project", {
    taskKey: args.taskKey,
    workflowId: args.workflowId ?? "",
    limit: args.limit ? String(args.limit) : "",
  });
  return JSON.stringify(envelope.data, null, 2);
}

export default nodeProject;
```

- [ ] **Step 3: 新建 handler `src\tools\node-select.ts`**

```typescript
/**
 * node-select — 预算内注入（selection 层，框架侧强制预算）
 *
 * 给定 select 节点键集，加载载荷、逐节点压缩、按 token_budget 限额注入上下文文本。
 */

import { invokeBridge, readNodeDiscovery } from "./storage-bridge.js";

export interface NodeSelectArgs {
  taskKey?: string;
  workflowId?: string;
  select: string[];
  budgetTokens?: number;
}

export async function nodeSelect(args: NodeSelectArgs): Promise<string> {
  const nd = readNodeDiscovery();
  if (!nd.enabled) {
    return JSON.stringify({ status: "disabled", message: "nodeDiscovery.enabled=false" }, null, 2);
  }
  const budget = args.budgetTokens ?? nd.defaultBudgetTokens;
  const envelope = invokeBridge("node-select", {
    taskKey: args.taskKey ?? "",
    workflowId: args.workflowId ?? "",
    select: JSON.stringify(args.select ?? []),
    budgetTokens: String(budget),
  });
  return JSON.stringify(envelope.data, null, 2);
}

export default nodeSelect;
```

- [ ] **Step 4: 新建工具定义 `tools-definitions\node_project.json`**

```json
{
  "name": "node_project",
  "description": "候选投影：给定 taskKey，沿引用边投影本 task 候选节点索引（nodeKey/类型/状态/大小/一句话摘要 + token 估算），不携带载荷。",
  "handler": "node-project",
  "template": "with-args",
  "inputSchema": {
    "type": "object",
    "properties": {
      "taskKey": { "type": "string", "description": "task 键（task 作用域寻址依据）" },
      "workflowId": { "type": "string", "description": "workflow 标识，缺省由桥自动解析" },
      "limit": { "type": "number", "description": "候选数量上限，0=不限" }
    },
    "required": ["taskKey"]
  }
}
```

- [ ] **Step 5: 新建工具定义 `tools-definitions\node_select.json`**

```json
{
  "name": "node_select",
  "description": "预算内注入：给定 select 节点键集，加载载荷、逐节点压缩、按 token_budget 限额注入上下文文本。",
  "handler": "node-select",
  "template": "with-args",
  "inputSchema": {
    "type": "object",
    "properties": {
      "taskKey": { "type": "string", "description": "task 键" },
      "workflowId": { "type": "string", "description": "workflow 标识" },
      "select": { "type": "array", "items": { "type": "string" }, "description": "本轮要加载载荷的节点键集" },
      "budgetTokens": { "type": "number", "description": "注入预算（token），缺省取配置 defaultBudgetTokens" }
    },
    "required": ["select"]
  }
}
```

- [ ] **Step 6: 编译验证（TS → build/tools/）**

Run:
```powershell
npm run build
```
（cwd：`D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge`）
Expected: tsc 无错误，`build/tools/node-project.js`、`build/tools/node-select.js`、`build/tools/storage-bridge.js` 生成。

- [ ] **Step 7: Commit**

```bash
git add TypeScriptPrj/TsWorkspace/MCPBridge/src/tools/storage-bridge.ts TypeScriptPrj/TsWorkspace/MCPBridge/src/tools/node-project.ts TypeScriptPrj/TsWorkspace/MCPBridge/src/tools/node-select.ts TypeScriptPrj/TsWorkspace/MCPBridge/tools-definitions/node_project.json TypeScriptPrj/TsWorkspace/MCPBridge/tools-definitions/node_select.json
git commit -m "feat(node-discovery): mcp tools node_project/node_select bridging storage bridge"
```

---

### Task 7: 配置 `nodeDiscovery` 段

**Files:**
- Modify: `D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge\mcp-bridge.config.json`

- [ ] **Step 1: 在 `"servers"` 数组之前插入 `nodeDiscovery` 段**

在 `mcp-bridge.config.json` 的 `"localTools": {...}` 之后、`"servers"` 之前插入：

```json
  "nodeDiscovery": {
    "enabled": true,
    "defaultBudgetTokens": 6000,
    "compressor": "tiktoken-truncate",
    "projection": "relevance",
    "readVerify": true,
    "incrementalReconcile": true,
    "storageHost": "Agent\\BuildOutput\\Debug\\Agent.StorageBridge.exe"
  },
```

- [ ] **Step 2: JSON 校验**

Run:
```powershell
Get-Content "D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge\mcp-bridge.config.json" -Raw | ConvertFrom-Json | Out-Null; "OK"
```
Expected: 输出 `OK`（JSON 合法）。

- [ ] **Step 3: Commit**

```bash
git add TypeScriptPrj/TsWorkspace/MCPBridge/mcp-bridge.config.json
git commit -m "feat(node-discovery): add nodeDiscovery config section to mcp-bridge"
```

---

### Task 8: 端到端验证（MCP 工具）

**Files:** 无（验证步骤）

- [x] **Step 1: 重启 MCPBridge 使工具注册生效**

停止旧 `mcp-bridge` 进程后启动：
```powershell
npm start
```
（cwd：`D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge`）
Expected: 日志出现 `已注册 N 个本地工具`（N ≥ 6：原 4 + node_project + node_select）。

- [ ] **Step 2: 直跑 handler 验证投影（不经 MCP 客户端，直接 import 编译产物）**

```powershell
node --input-type=module -e "import('./build/tools/node-project.js').then(m => m.default({ taskKey: 'YOUR_TASK_KEY', workflowId: 'agent-round' })).then(console.log).catch(e => { console.error(e.message); process.exit(1); })"
```
（cwd：`D:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\MCPBridge`）
Expected: 打印候选清单 JSON；`nodeDiscovery.enabled=false` 时打印 disabled 信封。

- [ ] **Step 3: 直跑 handler 验证注入**

```powershell
node --input-type=module -e "import('./build/tools/node-select.js').then(m => m.default({ taskKey: 'YOUR_TASK_KEY', workflowId: 'agent-round', select: ['n1','n2'], budgetTokens: 6000 })).then(console.log).catch(e => { console.error(e.message); process.exit(1); })"
```
Expected: `data.injected` 含 `### n1` / `### n2` 块，`usedTokens <= 6000`。

- [x] **Step 4: 关闭开关验证降级（临时改 enabled=false 后重跑 Step 2）**

Expected: 打印 `{"status":"disabled",...}`，不调用 exe。

> **Task 8 验证发现并修复（非原计划文件）：** 从 MCPBridge CWD 调 StorageBridge 时，SQLite 相对路径
> `Projects\Agent\data\agent.db` 被后端按当前工作目录解析，分裂出 `TypeScriptPrj\TsWorkspace\MCPBridge\Projects\Agent\data\agent.db`，
> 导致真实库无候选。修复：`DatabaseBootstrapper.TryBootstrapAsync` 在装配前将相对数据库路径锚定到仓库根
> （复用既有 CWD 无关的 `ResolveSqliteAbsolutePath`，与图库一致）。改后从异 CWD 引导也能命中真实 `agent.db`。

---

## 自审

**1. 规格覆盖（对照设计文档 P1）**
- task 作用域寻址：Task 2 投影服务复用 `ProjectTaskNodesAsync`（cross_db_link 挂载 + NodeToNode 拓扑），测试含跨 task 隔离断言。✔
- 候选投影（索引 + 摘要 + 预算外露）：`CandidateItem` 含 nodeKey/类型/状态/大小/摘要/estTokens，`ProjectionResult.TotalEstTokens`。✔
- 预算内注入 + 压缩：Task 3 `NodeInjectionService` 逐节点截断 + `budgetTokens` 强制限额 + dropped 清单。✔
- 零侵入框架：只改 StorageBridge（新增子命令）+ MCPBridge（新增工具），不触碰 `ResolveInputsAsync` / `NodeExecutionService` / `RoundStepInvoker`。✔
- MCP 工具集约定（tools-definitions + src/tools + config）：Task 6/7 对齐 `invoke-ps-script` 模式。✔

**2. 占位符扫描**：无 "TBD/TODO"；每个代码步骤含完整代码；无泛泛的"添加错误处理"。✔

**3. 类型一致性**
- `ProjectionResult` / `InjectionResult` / `CandidateItem` 在 Task 1 定义、Task 2/3 构造、Task 4 Program.cs 序列化输出，命名一致。✔
- `NodeRecordResolver.ResolveAsync` / `NodeSummaryBuilder.Build` / `TokenEstimator.Estimate` 在 Task 1 定义、Task 2/3/4 使用，签名一致。✔
- `NodeInjectionService.InjectAsync(workflowId, select, budgetTokens, ct)` 在 Task 3 定义、Task 4 `NodeSelectAsync` 调用、Task 3 测试调用，签名一致。✔
- TS 侧 `readNodeDiscovery()` / `invokeBridge()` / `resolveBridgeExe()` 在 Task 6 Step 1 定义、Step 2/3 使用；`node_project.json`/`node_select.json` handler 名（`node-project`/`node-select`）与 `src/tools/*.ts` 文件名及 default export 一致（registry 动态 import `./node-project.js`）。✔

**4. 边界与风险提示（记录而非阻塞）**
- 投影返回的裸 nodeKey 与 SQLite 双键形（输出记录 `nodes.{wf}.{node}` vs 业务记录裸 `{node}`）已在 `NodeRecordResolver` 统一处理；测试覆盖输出记录路径。
- P2 起的「TS 侧抽象层（Strategy/Compressor）」不在本计划范围，压缩暂以 C# 字符近似实现；`compressor/projection/readVerify/incrementalReconcile` 配置项先落位、P2/P3 再消费。

---

## 执行交接

计划已保存。两种执行方式：

1. **Subagent-Driven（推荐）**：每个 Task 派发独立 subagent，任务间两阶段审查，快速迭代。
2. **Inline Execution**：本会话内用 executing-plans 按任务批量执行，带检查点供审查。

选择哪种方式？
