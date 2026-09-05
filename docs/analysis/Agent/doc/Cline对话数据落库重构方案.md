# Cline 对话数据落库重构方案

> **版本**：v1.0
> **日期**：2026-08-22
> **范围**：将子 Cline（主程序通过 daemon 池调用的 @cline/sdk 实例）的对话消息
> 从文件系统（`~/.cline/data/sessions/`）迁移到 SQLite 基础节点库 `ur_nodes`，
> 与主程序自身的 `ConversationLog` 形成统一的库内对话归档体系。
> **决策**：归档粒度采用「每轮一条 NodeKey」，可追溯每轮历史。

---

## 1. 背景与目标

### 1.1 背景

主对话框（[AgentRuntimeUI.json](file:///D:/Project/Program/WorkFlowPrj/FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json) 第 4 页「主对话框」）输入的提示词，经 `AgentUi_ConversationSend` → `ConversationPipeline.RunSignalAsync` → 工作流 Cline 步骤 → `ClineAgentExecutor` → `ClineClient` → daemon 池 → Node 桥 → @cline/sdk 调用链，最终落盘到：

```
%USERPROFILE%\.cline\data\sessions\{sessionId}\{sessionId}.messages.json
```

该目录是 VSCode Cline 插件自身的会话存储根目录，导致主程序写入的对话历史在 VSCode 插件侧被动可见，且无统一治理（与 `ur_tasks` 的 `ConversationLog` 完全分离）。

### 1.2 目标

1. **统一存储**：子 Cline 每轮对话消息以节点形式归档到 `ur_nodes`，与主程序 `ConversationLog` 共用同一基础节点库。
2. **隔离文件**：把 Cline SDK 的临时文件根目录切到独立 `dataDir`，不再污染 `~/.cline`。
3. **可追溯**：每轮一条 NodeKey，支持按 sessionId + 轮次回查与回放。
4. **resume 可重建**：从 `ur_nodes` 反向回灌 messages.json 到独立 `dataDir`，让 SDK resume 加载历史上下文。
5. **零侵入 SDK**：不修改 @cline/sdk 内部 IO；仅由 C# 主程序在调用前后做归档/回灌。

### 1.3 非目标

- 不替换 @cline/sdk 的存储后端（未公开注入接口，风险高）。
- 不修改 VSCode Cline 插件行为（独立 `dataDir` 后插件自然不再看到本程序写入的消息）。
- 不改动主程序 `ConversationLog` 落库路径（保持 `ur_tasks.PayloadJson` 现有语义）。

---

## 2. 现状分析

### 2.1 数据流现状

```
[UI 主对话框] conversationInputTextBox
      │ (AgentUi_ConversationSend, 参数 sessionName+text)
      ▼
[ConversationPipeline.RunSignalAsync] ─── 写 ──► ur_tasks.PayloadJson (ConversationLog)
      │ (种入 lease.Data[RoundKeys.SessionId] = item.TaskKey)
      ▼
[工作流 Cline 步骤] (collect-analyze-act.json)
      │ (调用 IAgentExecutor.ExecuteAsync)
      ▼
[ClineAgentExecutor.ExecuteAsync]
      │ (ComposePrompt + AskAsync)
      ▼
[ClineClient.AskAsync] ── daemon 池 IPC ──► [Node engine.ts] ──► @cline/sdk
                                                                          │
                                                                  写 ~/.cline/data/sessions/{id}/{id}.messages.json
                                                                          │
                                                  VSCode Cline 插件 ◄── 被动可见（共享目录）
```

### 2.2 痛点

| 痛点 | 描述 | 影响 |
|------|------|------|
| 存储分离 | 主程序 `ConversationLog` 走 `ur_tasks`，子 Cline 走文件 | 双轨不可联查 |
| 目录共享 | Cline SDK 默认 `~/.cline`，与 VSCode 插件共用 | 插件被动可见主程序对话 |
| 不可追溯 | 文件以全量覆写方式持久化，无轮次粒度 | 无法回查第 N 轮输入/输出 |
| resume 依赖文件 | `SdkTransport.resolveMessagesPath` 推导路径后 SDK 自行加载 | 文件丢失则 resume 失效 |
| 无治理 | 子 Cline 调用无统一审计、无 size 治理 | 长期累积膨胀 |

### 2.3 关键代码定位

| 角色 | 文件 | 关键行 |
|------|------|--------|
| C# 节点库接口 | [INodeStore](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Abstractions/NodeStore.cs#L74-L95) | PutAsync/GetAsync/ListAsync |
| 节点库实现 | [SqliteNodeStore](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Sqlite/SqliteNodeStore.cs#L41-L59) | 3 列表，json_extract 索引 |
| NodeRecord 模型 | [NodeStore.cs:11-43](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Abstractions/NodeStore.cs#L11-L43) | ThreadId/PayloadJson/Status 等 |
| Cline 执行器 | [ClineAgentExecutor.cs](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/ClineAgentExecutor.cs) | ExecuteAsync:42-120 |
| Cline 客户端 | [ClineClient.cs](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/ClineClient.cs) | AskAsync:88-92, BuildPayload:210-233 |
| Daemon 池配置 | [ClineDaemonOptions.cs:38-41](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/Pool/ClineDaemonOptions.cs#L38-L41) | Isolation=Shared 默认 |
| Daemon 进程启动 | [ClineDaemonHost.cs:39-40](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/Pool/ClineDaemonHost.cs#L39-L40) | Isolated 时注入 CLINE_HOME |
| Node 桥落盘路径 | [sdk-transport.ts:235-240](file:///D:/Project/Program/WorkFlowPrj/TypeScriptPrj/TsWorkspace/ClineApi/src/transport/sdk-transport.ts#L235-L240) | resolveMessagesPath |
| 会话工作流 | [ConversationPipeline.cs:77-152](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs#L77-L152) | RunSignalAsync |
| 装配入口 | [ConversationHosting.cs:21-35](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Composition/ConversationHosting.cs#L21-L35) | 默认工作流装配 |

---

## 3. 目标架构

### 3.1 目标数据流

```
[UI 主对话框]
      │
      ▼
[ConversationPipeline.RunSignalAsync] ─── 写 ──► ur_tasks.PayloadJson (ConversationLog, 不变)
      │
      ▼
[工作流 Cline 步骤]
      │
      ▼
[ClineAgentExecutor.ExecuteAsync]
      │
      ├─► (resume 前) IClineMessageArchive.LoadAsync(sessionId, round)
      │       └─► 从 ur_nodes 读出 N-1 轮消息
      │              └─► 写回 {isolatedDataDir}/data/sessions/{id}/{id}.messages.json
      │
      ├─► ClineClient.AskAsync(...) ── daemon 池 ──► Node 桥 ──► @cline/sdk
      │                                                     │
      │                                            写 {isolatedDataDir}/data/sessions/{id}/{id}.messages.json
      │
      └─► (调用后) IClineMessageArchive.ArchiveAsync(sessionId, round, messagesJson, status)
              └─► INodeStore.PutAsync(NodeRecord {
                      NodeKey = cline.session.{sessionId}.round.{round},
                      NodeType = cline_messages,
                      ThreadId = sessionId,
                      Key = "round-{round}",
                      Status = "completed" | "faulted",
                      PayloadJson = <messages.json 全文>
                  })
```

### 3.2 与现状差异

| 维度 | 现状 | 目标 |
|------|------|------|
| 子 Cline 落盘根 | `~/.cline`（共享 VSCode 插件） | `{isolatedDataDir}`（如 `D:\...\ClineRuntime`，独立） |
| 子 Cline 消息归档 | 无 | 每轮一条 NodeKey 入 ur_nodes |
| resume 历史来源 | 文件（依赖落盘完整） | ur_nodes 反查 → 回灌文件 → SDK 加载 |
| 治理能力 | 无 | NodeStore.ListAsync(threadId) 统计/审计/清理 |
| 与 ConversationLog 联查 | 不可能 | 同 ThreadId 跨库（ur_nodes/ur_tasks）联查 |

---

## 4. 详细设计

### 4.1 NodeKey 与 NodeType 设计（每轮一条）

**NodeKey 格式**：

```
cline.session.{sessionId}.round.{round}
```

- `sessionId`：来自 UI 会话下拉框的会话名（即 `item.TaskKey`，[ConversationPipeline.cs:257](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs#L257)）。
- `round`：从 1 起的轮次序号（与 `IAgentLoopState.Round` 对齐；首轮为 1）。
- 唯一性：NodeKey UNIQUE 约束保证幂等 upsert。

**字段映射**：

| NodeRecord 字段 | 取值 | 语义 |
|-----------------|------|------|
| `NodeKey` | `cline.session.{sessionId}.round.{n}` | 幂等键 |
| `NodeType` | `cline_messages`（新增常量） | 语义判别，与 `event/checkpoint/...` 同级 |
| `ThreadId` | `{sessionId}` | 按会话过滤（与 ur_tasks 同 sessionId，可跨库联查） |
| `Key` | `round-{n}` | 业务辅助列，便于按轮次排序/分页 |
| `Status` | `completed` / `faulted` / `partial` | 取自 `ClineRunResult.Ok` + `FailureStage` |
| `PayloadJson` | messages.json 全文（JSON 字符串转义后内嵌） | 完整消息数组 |
| `RelationsJson` | `{"taskKey":"{sessionId}","conversationLogArchive":"__conversation"}` 可选 | 跨库边声明（与图库对账） |
| `CreatedAt` | 调用完成 UTC ISO | 首次写入 |
| `UpdatedAt` | 重跑该轮时更新 | 覆写时更新 |

**查询示例**：

```sql
-- 列出某会话全部轮次消息（按轮次升序）
SELECT [Id],[NodeKey],[Json] FROM [ur_nodes]
WHERE json_extract([Json],'$.NodeType')='cline_messages'
  AND json_extract([Json],'$.ThreadId')=@sid
ORDER BY json_extract([Json],'$.Key');

-- 取第 N 轮消息
SELECT [Json] FROM [ur_nodes]
WHERE [NodeKey]=@nk LIMIT 1;
-- @nk = 'cline.session.{sessionId}.round.{n}'
```

### 4.2 IClineMessageArchive 抽象

新增抽象（位于 `Agent.Modules/Cline/IClineMessageArchive.cs`）：

```csharp
namespace Agent.Modules;

/// <summary>
/// 子 Cline 对话消息归档端口（ur_nodes 节点库承载）。
/// 每轮一条 NodeKey（cline.session.{sessionId}.round.{round}），幂等 upsert。
/// </summary>
public interface IClineMessageArchive
{
    /// <summary>归档一轮对话消息（按 NodeKey 幂等 upsert）。</summary>
    /// <param name="sessionId">Cline 会话 ID（= 主程序会话名）。</param>
    /// <param name="round">轮次序号（从 1 起）。</param>
    /// <param name="messagesJson">messages.json 全文。</param>
    /// <param name="status">归档状态：completed / faulted / partial。</param>
    Task ArchiveAsync(string sessionId, int round, string messagesJson, string status, CancellationToken ct = default);

    /// <summary>读取指定轮次消息（resume 回灌用）。</summary>
    Task<string?> LoadAsync(string sessionId, int round, CancellationToken ct = default);

    /// <summary>读取会话最近一轮消息（= 已归档的最大 round）。</summary>
    Task<string?> LoadLatestAsync(string sessionId, CancellationToken ct = default);

    /// <summary>列出会话全部已归档轮次（按轮次升序）。</summary>
    Task<IReadOnlyList<int>> ListRoundsAsync(string sessionId, CancellationToken ct = default);
}
```

### 4.3 NodeStoreClineMessageArchive 实现

新增实现（位于 `Agent.Modules/Cline/NodeStoreClineMessageArchive.cs`），复用 `INodeStore`：

```csharp
using Operation.DataStoreDriven.Abstractions;

namespace Agent.Modules;

/// <summary>
/// 基于 ur_nodes 的子 Cline 消息归档实现。
/// NodeKey = cline.session.{sessionId}.round.{round}；NodeType = cline_messages。
/// </summary>
public sealed class NodeStoreClineMessageArchive : IClineMessageArchive
{
    public const string NodeType = "cline_messages";
    private const string KeyPrefix = "cline.session.";
    private const string RoundSuffix = ".round.";

    private readonly INodeStore _nodes;

    public NodeStoreClineMessageArchive(INodeStore nodes) => _nodes = nodes;

    public async Task ArchiveAsync(string sessionId, int round, string messagesJson, string status, CancellationToken ct = default)
    {
        var record = new NodeRecord
        {
            NodeKey = BuildNodeKey(sessionId, round),
            NodeType = NodeType,
            ThreadId = sessionId,
            Key = $"round-{round}",
            Status = status,
            PayloadJson = messagesJson,
        };
        await _nodes.PutAsync(record, ct).ConfigureAwait(false);
    }

    public async Task<string?> LoadAsync(string sessionId, int round, CancellationToken ct = default)
    {
        var record = await _nodes.GetAsync(BuildNodeKey(sessionId, round), ct).ConfigureAwait(false);
        return record?.PayloadJson;
    }

    public async Task<string?> LoadLatestAsync(string sessionId, CancellationToken ct = default)
    {
        var list = await _nodes.ListAsync(nodeType: NodeType, threadId: sessionId, limit: 1000, ct: ct).ConfigureAwait(false);
        if (list.Count == 0) return null;
        // 取 Key=round-N 中最大的 N
        var maxRound = list
            .Select(r => ParseRound(r.Key))
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .DefaultIfEmpty(-1)
            .Max();
        if (maxRound < 0) return null;
        var latest = list.First(r => r.Key == $"round-{maxRound}");
        return latest.PayloadJson;
    }

    public async Task<IReadOnlyList<int>> ListRoundsAsync(string sessionId, CancellationToken ct = default)
    {
        var list = await _nodes.ListAsync(nodeType: NodeType, threadId: sessionId, limit: 1000, ct: ct).ConfigureAwait(false);
        return list
            .Select(r => ParseRound(r.Key))
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .OrderBy(n => n)
            .ToList();
    }

    private static string BuildNodeKey(string sessionId, int round)
        => $"{KeyPrefix}{sessionId}{RoundSuffix}{round}";

    private static int? ParseRound(string? key)
        => int.TryParse(key?.Replace("round-", ""), out var n) ? n : null;
}
```

**配套修改**：在 [NodeStore.cs:50-72](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Abstractions/NodeStore.cs#L50-L72) 的 `NodeTypes` 类追加常量：

```csharp
public const string ClineMessages = "cline_messages";
```

并在 `LogicRecordTypes` 集合中追加 `ClineMessages`。

### 4.4 ClineAgentExecutor 改造

[ClineAgentExecutor.cs:32-37](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/ClineAgentExecutor.cs#L32-L37) 构造函数增加两个依赖：

```csharp
public ClineAgentExecutor(
    ClineClient? client = null,
    ClineRunOptions? defaults = null,
    string? name = null,
    IClineMessageArchive? archive = null,        // 新增：归档端口
    string? isolatedDataDir = null)              // 新增：隔离根目录
{
    _name = string.IsNullOrWhiteSpace(name) ? "cline" : name.Trim();
    _client = client ?? new ClineClient();
    _defaults = defaults;
    _archive = archive;
    _isolatedDataDir = isolatedDataDir;
}
```

[ClineAgentExecutor.cs:42-120](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/ClineAgentExecutor.cs#L42-L120) `ExecuteAsync` 改造（仅展示关键插入点）：

```csharp
public async Task<AgentExecutorResult> ExecuteAsync(AgentExecutorRequest request, CancellationToken ct = default)
{
    // ... 现有 prompt + LlmMode.LocalOnly 短路逻辑保持不变 ...

    // 1) 隔离 dataDir：执行期把 SDK 写盘根切到独立目录
    if (!string.IsNullOrWhiteSpace(_isolatedDataDir) && string.IsNullOrWhiteSpace(options.DataDir))
        options = options with { DataDir = _isolatedDataDir };

    // 2) resume 前：从 ur_nodes 回灌最近一轮 messages.json 到独立目录
    if (_archive is not null && !string.IsNullOrWhiteSpace(request.SessionId))
    {
        await TryRestoreMessagesFileAsync(request.SessionId, options.DataDir, ct).ConfigureAwait(false);
    }

    // 3) 调用（保持原逻辑）
    var clineSw = System.Diagnostics.Stopwatch.StartNew();
    var r = await _client.AskAsync(prompt, options, ct).ConfigureAwait(false);
    clineSw.Stop();
    LlmMode.RecordExternalCall("cline", "cline-daemon", clineSw.ElapsedMilliseconds, r.Succeeded, r.Failure);

    // 4) 归档：把 messages.json 内容写入 ur_nodes，每轮一条 NodeKey
    if (_archive is not null && !string.IsNullOrWhiteSpace(r.SessionId))
    {
        await TryArchiveRoundAsync(request, r, options.DataDir, ct).ConfigureAwait(false);
    }

    // ... 现有 TokenUsage/ClineResult 构造保持不变 ...
}
```

**辅助方法**：

```csharp
private async Task TryRestoreMessagesFileAsync(string sessionId, string? dataDir, CancellationToken ct)
{
    try
    {
        var messages = await _archive!.LoadLatestAsync(sessionId, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(messages)) return;
        var path = ResolveMessagesPath(dataDir, sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, messages, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        // 回灌失败不阻断主流程（SDK 会按空历史启动新会话）
        ClineClientDiagnostics.Emit?.Invoke("ClineArchive_RestoreFailed", new { sessionId, error = ex.Message });
    }
}

private async Task TryArchiveRoundAsync(AgentExecutorRequest request, ClineRunResult r, string? dataDir, CancellationToken ct)
{
    try
    {
        var path = ResolveMessagesPath(dataDir, r.SessionId!);
        if (!File.Exists(path)) return;
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var round = Math.Max(1, request.Round);  // AgentExecutorRequest 需新增 Round 字段（见 4.5）
        var status = r.Succeeded ? "completed" : "faulted";
        await _archive!.ArchiveAsync(r.SessionId!, round, json, status, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        // 归档失败不阻断返回链（已付费的 Cline 调用结果照常返回）
        ClineClientDiagnostics.Emit?.Invoke("ClineArchive_ArchiveFailed", new { sessionId = r.SessionId, error = ex.Message });
    }
}

private static string ResolveMessagesPath(string? dataDir, string sessionId)
{
    var home = dataDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\.cline";
    return Path.Combine(home, "data", "sessions", sessionId, $"{sessionId}.messages.json");
}
```

### 4.5 AgentExecutorRequest 增加 Round 字段

[IAgentExecutor 调用链](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Contract/IAgentExecutor.cs) 的 `AgentExecutorRequest` 需新增 `Round` 字段，由 `ConversationPipeline` 把 `((IAgentLoopState)item).Round + 1`（[ConversationPipeline.cs:236](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs#L236) 推进后的值）注入，作为本轮归档的轮次序号。

```csharp
public sealed record AgentExecutorRequest
{
    // ... 现有字段 ...
    public int Round { get; init; }  // 新增：当前轮次序号（从 1 起）
}
```

**注入位置**：`WorkflowRunner` 或 `RoundStepInvoker` 构造 `AgentExecutorRequest` 时，从 `RoundPipelineContext.Round` 透传。

### 4.6 隔离 dataDir 配置

[ClineDaemonOptions.cs:38-41](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/Pool/ClineDaemonOptions.cs#L38-L41) 的 `Isolation` 默认改为 `Isolated`，`DataDir` 默认指向程序基目录下的 `ClineRuntime`：

```csharp
DataIsolation Isolation = DataIsolation.Isolated,
string? DataDir = null,  // null 时由 ClineDaemonHost 兜底为 AppContext.BaseDirectory/ClineRuntime
```

[ClineDaemonHost.cs:39-40](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/Pool/ClineDaemonHost.cs#L39-L40) 改造：

```csharp
var dataDir = !string.IsNullOrWhiteSpace(dataDir)
    ? dataDir
    : Path.Combine(AppContext.BaseDirectory, "ClineRuntime");
if (isolation == DataIsolation.Isolated)
    _process.StartInfo.EnvironmentVariables["CLINE_HOME"] = Path.Combine(dataDir, workerId);
```

**装配入口**（`ConversationHosting.cs` 或 `AgentFrameworkAssembler`）注入：

```csharp
var nodeStore = host.Services.GetRequiredService<INodeStore>();
var archive = new NodeStoreClineMessageArchive(nodeStore);
var executor = new ClineAgentExecutor(
    client: clineClient,
    defaults: clineDefaults,
    archive: archive,
    isolatedDataDir: Path.Combine(AppContext.BaseDirectory, "ClineRuntime"));
```

### 4.7 resume 回灌策略

**默认策略**：resume 调用前主动从 `ur_nodes` 回灌最近一轮 messages.json 到 `{isolatedDataDir}/data/sessions/{id}/`，让 SDK 加载历史上下文继续。

**容错**：
- 若 ur_nodes 无归档记录 → 跳过回灌，SDK 按新会话启动（不报错）。
- 若回灌的 JSON 损坏 → SDK 内部 try/catch 会忽略，按空会话启动。
- 若回灌写入文件失败 → 诊断日志 `ClineArchive_RestoreFailed`，不阻断调用。

**选择回灌最近一轮而非历史合并**：
- @cline/sdk 内部 messages.json 是顺序追加的数组，覆写最近一轮即可让 SDK 恢复完整上下文（每轮 messages.json 本身就是累积全量）。
- 不需要做轮次合并逻辑（避免引入复杂性）。

---

## 5. 实施步骤（分阶段）

### 阶段 1：核心归档与隔离（最小可用）

| 步骤 | 内容 | 验证标准 |
|------|------|---------|
| 1.1 | 在 [NodeStore.cs:50-72](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Abstractions/NodeStore.cs#L50-L72) 追加 `NodeTypes.ClineMessages` 常量 | 编译通过；NodeType 集合含新常量 |
| 1.2 | 新增 `IClineMessageArchive.cs` 接口 | 编译通过 |
| 1.3 | 新增 `NodeStoreClineMessageArchive.cs` 实现 | 单元测试：ArchiveAsync 后 LoadAsync 能取回原文 |
| 1.4 | `AgentExecutorRequest` 增加 `Round` 字段 | 编译通过；现有调用方未破坏 |
| 1.5 | `WorkflowRunner`/`RoundStepInvoker` 透传 Round | 调试断点验证 Round=1, 2, 3 递增 |
| 1.6 | `ClineAgentExecutor` 注入 archive + isolatedDataDir | 编译通过 |
| 1.7 | `ClineAgentExecutor.ExecuteAsync` 插入回灌 + 归档逻辑 | 调用后 `ur_nodes` 出现 `cline.session.{sessionId}.round.1` 记录 |
| 1.8 | `ClineDaemonOptions` 默认 Isolation=Isolated | daemon 进程环境变量含 `CLINE_HOME` 指向独立目录 |
| 1.9 | `ClineDaemonHost` dataDir 兜底解析 | 启动后 `AppContext.BaseDirectory/ClineRuntime` 目录存在 |
| 1.10 | 装配入口注入 archive + isolatedDataDir | 启动主程序无异常；DI 容器解析 `ClineAgentExecutor` 成功 |

### 阶段 2：验证与端到端测试

| 步骤 | 内容 | 验证标准 |
|------|------|---------|
| 2.1 | UI 主对话框发送一条消息 | `ur_nodes` 新增 1 条 `cline_messages` 节点 |
| 2.2 | 同会话连续发送 3 条消息 | `ur_nodes` 出现 round=1/2/3 三条记录，PayloadJson 各自完整 |
| 2.3 | 删除独立 dataDir 后 resume 同会话 | 回灌成功；SDK 加载历史；新增 round=4 归档 |
| 2.4 | VSCode Cline 插件查看 `~/.cline/data/sessions/` | 不再出现主程序会话目录（隔离生效） |
| 2.5 | `AgentUi_DbReadTableMarkdown` db=nodes 过滤 NodeType=cline_messages | 列出全部子 Cline 归档节点 |
| 2.6 | 长会话压测 50 轮 | `ur_nodes` 单节点 PayloadJson 大小可控；查询 ListRoundsAsync 性能 < 50ms |

### 阶段 3（可选）：去文件中转（事件流直采）

> 阶段 2 稳定后再考虑。改造 Node 桥 `engine.ts`，把 `EventCollector.snapshotMessages` 通过 IPC 响应帧回带 C# 主程序，归档优先走 IPC 直采，文件作兜底。改动较大且依赖 @cline/sdk 行为稳定性。

---

## 6. 验证标准

### 6.1 功能性

- **归档完整性**：每次 `AgentUi_ConversationSend` 调用成功后，`ur_nodes` 新增一条 `cline_messages` 节点，PayloadJson 与独立 dataDir 中的 messages.json 内容逐字符一致。
- **轮次递增**：同会话连续调用，NodeKey 中的 round 字段从 1 起递增。
- **resume 恢复**：删除独立 dataDir 后 resume 同会话，SDK 能加载前轮上下文（从 `ur_nodes` 回灌）。
- **隔离生效**：独立 dataDir 启用后，`~/.cline/data/sessions/` 不再出现主程序写入的会话目录。

### 6.2 非功能性

- **幂等性**：同一轮次重复归档（如重跑）→ upsert 不产生重复 NodeKey，UpdatedAt 刷新。
- **失败容错**：归档/回灌异常不阻断 Cline 调用主流程，仅发 `ClineArchive_*` 诊断事件。
- **查询性能**：`ListAsync(nodeType=cline_messages, threadId={sessionId})` 在单会话 100 轮内 < 50ms。
- **存储膨胀**：单条 PayloadJson 大小受 SQLite TEXT 限制（理论上 GB 级），单会话 50 轮内可控。

### 6.3 兼容性

- **不破坏 ConversationLog**：`ur_tasks.PayloadJson` 中的 `__conversation` 归档路径与格式不变。
- **不破坏 VSCode 插件**：插件按原有方式读写 `~/.cline`，不受独立 dataDir 影响。
- **不破坏本地联调**：`LlmMode.LocalOnly` 短路路径仍走 `LlmGateway.ChatAsync`，不触发归档（无可写文件）。

---

## 7. 风险与回退

### 7.1 风险

| 风险 | 概率 | 影响 | 缓解 |
|------|------|------|------|
| @cline/sdk 写盘路径与 `resolveMessagesPath` 推导不一致 | 低 | 归档读不到文件 | TryArchiveRoundAsync 中 `File.Exists` 守卫；缺失则跳过归档 |
| 独立 dataDir 与 daemon 池 worker 隔离冲突 | 中 | SDK 初始化失败 | 阶段 1.9 验证 daemon 启动；保留 Shared 回退开关 |
| Round 字段透传链断裂 | 中 | 归档到错误轮次号 | 阶段 1.5 调试断点验证 |
| 归档 PayloadJson 过大拖慢 ur_nodes | 低 | 查询变慢 | 阶段 2.6 压测；必要时拆分为多节点分片 |
| resume 回灌的 messages.json 格式不被 SDK 接受 | 低 | resume 失败 | SDK 内部 try/catch 兜底；诊断事件可观测 |

### 7.2 回退方案

**配置级回退**（不改代码）：
- 把 `ClineDaemonOptions.Isolation` 改回 `Shared`，`DataDir=null` → 回到 `~/.cline` 共享目录（与现状一致）。
- 装配入口注入 `archive: null` → 关闭归档，ClineAgentExecutor 走原逻辑。

**代码级回退**：
- `ClineAgentExecutor` 构造函数 `archive` 与 `isolatedDataDir` 都是可选注入；不注入时行为与重构前完全一致。
- `IClineMessageArchive` 抽象独立于 `ClineAgentExecutor`，可整体下线不影响主流程。

**数据级回退**：
- `ur_nodes` 中的 `cline_messages` 节点可批量清理：
  ```sql
  DELETE FROM [ur_nodes] WHERE json_extract([Json],'$.NodeType')='cline_messages';
  ```

---

## 8. 后续演进

### 8.1 阶段 3：事件流直采（去文件中转）

让 Node 桥 `engine.ts` 在 IPC 响应帧追加 `messagesJson` 字段（来自 `EventCollector.snapshotMessages`），C# 主程序优先走 IPC 直采，文件仅作兜底。

**改造点**：
- [sdk-transport.ts:114-123](file:///D:/Project/Program/WorkFlowPrj/TypeScriptPrj/TsWorkspace/ClineApi/src/transport/sdk-transport.ts#L114-L123) `EventCollector` 已缓存 `snapshotMessages`，无需新增缓存。
- [engine.ts:87-109](file:///D:/Project/Program/WorkFlowPrj/TypeScriptPrj/TsWorkspace/ClineApi/src/engine.ts#L87-L109) 响应帧追加 `messagesJson`。
- [ClineModels.cs:17-29](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/ClineModels.cs#L17-L29) `ClineRunResult` 增加 `MessagesJson` 字段。
- `ClineAgentExecutor` 优先用 `r.MessagesJson` 归档，文件读取作兜底。

### 8.2 跨库联查

利用 `ThreadId = sessionId` 在 `ur_nodes` 与 `ur_tasks` 之间建立隐式关联，可在 UI 数据库页新增视图：

```sql
SELECT
    t.NodeKey AS task_key,
    json_extract(t.Json,'$.Status') AS task_status,
    c.NodeKey AS cline_round,
    json_extract(c.Json,'$.Status') AS round_status,
    json_length(json_extract(c.Json,'$.PayloadJson')) AS message_size
FROM [ur_tasks] t
LEFT JOIN [ur_nodes] c
    ON json_extract(c.Json,'$.ThreadId') = t.NodeKey
   AND json_extract(c.Json,'$.NodeType') = 'cline_messages'
ORDER BY t.Id, c.Id;
```

### 8.3 治理与清理

新增定期任务：
- 按 `UpdatedAt` 清理超过 N 天的 `cline_messages` 节点。
- 按会话归档总大小阈值触发压缩（合并历史轮次到单条 `cline.session.{id}.archive`）。

### 8.4 图库边声明

`RelationsJson` 字段携带 `{"taskKey":"{sessionId}","conversationLogArchive":"__conversation"}`，让 `ur_nodes` 的子 Cline 节点与 `ur_tasks` 的 ConversationLog 通过图库（LiteDB `graph`）声明式建立边，支撑后续图查询。

---

## 9. 附录

### 9.1 涉及修改文件清单

| 文件 | 修改类型 | 量级 |
|------|---------|------|
| [NodeStore.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/DataStoreDriven/Abstractions/NodeStore.cs) | 修改：追加 `NodeTypes.ClineMessages` | 极小（~3 行） |
| `Agent.Modules/Cline/IClineMessageArchive.cs` | 新增：抽象接口 | 小（~30 行） |
| `Agent.Modules/Cline/NodeStoreClineMessageArchive.cs` | 新增：ur_nodes 实现 | 小（~80 行） |
| [Agent.Contract/IAgentExecutor.cs](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Contract/IAgentExecutor.cs) | 修改：`AgentExecutorRequest` 加 Round | 极小（~2 行） |
| [ClineAgentExecutor.cs](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/ClineAgentExecutor.cs) | 修改：构造函数 + ExecuteAsync 插入归档/回灌 | 中（~50 行） |
| [ClineDaemonOptions.cs](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/Pool/ClineDaemonOptions.cs) | 修改：默认 Isolation=Isolated | 极小（~1 行） |
| [ClineDaemonHost.cs](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/Pool/ClineDaemonHost.cs) | 修改：dataDir 兜底解析 | 小（~5 行） |
| `Agent.Launcher/Composition/ConversationHosting.cs` 或 `AgentFrameworkAssembler.cs` | 修改：注入 archive + isolatedDataDir | 小（~10 行） |
| `Agent.Orchestration/StepExecution/RoundStepInvoker.cs` 或 `WorkflowRunner.cs` | 修改：透传 Round 到 AgentExecutorRequest | 小（~5 行） |

### 9.2 关键决策记录

| 决策 | 选择 | 理由 |
|------|------|------|
| 归档粒度 | 每轮一条 NodeKey | 可追溯每轮；支持按轮次回查/回放/重跑 |
| 存储后端 | ur_nodes（SQLite） | 复用现成 INodeStore；与 ConversationLog 同库可联查 |
| 隔离方式 | CLINE_HOME 环境变量 | SDK 三级回退天然支持；零侵入 |
| resume 回灌源 | ur_nodes 最近一轮 | SDK messages.json 是累积全量，覆写最近一轮即可恢复上下文 |
| 失败容错 | 归档/回灌失败不阻断主流程 | 已付费的 Cline 调用结果不能因归档失败丢失 |
| NodeType 命名 | 新增 `cline_messages` | 语义独立于 `event/checkpoint`，便于过滤与治理 |

### 9.3 术语表

| 术语 | 含义 |
|------|------|
| 子 Cline | 主程序通过 daemon 池调用的 @cline/sdk 实例（区别于 VSCode 插件侧 Cline） |
| ConversationLog | 主程序自身的对话日志，序列化后存 `ur_tasks.PayloadJson.__conversation` |
| messages.json | @cline/sdk 自身的会话消息文件，JSON 数组结构 |
| sessionId | 会话 ID；主程序中 = 会话名 = `item.TaskKey`；Cline 中 = SDK 会话 ID |
| 独立 dataDir | 重构后子 Cline 写盘根目录（默认 `AppContext.BaseDirectory/ClineRuntime`） |
| round | 轮次序号；与 `IAgentLoopState.Round` 对齐，从 1 起递增 |

---

**文档结束**。如需启动实施，请确认阶段 1 步骤 1.1–1.10 是否按序推进，以及 4.5 中 `AgentExecutorRequest.Round` 的透传位置（`WorkflowRunner` vs `RoundStepInvoker`）选择。
