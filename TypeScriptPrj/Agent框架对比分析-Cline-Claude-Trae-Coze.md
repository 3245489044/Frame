# Agent SDK 对比分析：Claude / Cline / Trae / Coze / 自研框架

> 范围：聚焦 **SDK 层面**（API 设计、调用入口、状态/会话接口、工具扩展、权限模型、Provider 抽象、流式事件、可嵌入性），横向对比五类 Agent SDK，定位自研 SDK（ClineApi + MCPBridge + C# 工作流引擎 + CSG 契约）的位置与演进。
> 不做产品形态分析。时间：2026-08-14

---

## 目录

1. [五 SDK 定位与调用入口](#1-五-sdk-定位与调用入口)
2. [核心 API 风格对比](#2-核心-api-风格对比)
3. [会话与状态管理接口](#3-会话与状态管理接口)
4. [工具扩展接口](#4-工具扩展接口)
5. [权限与审批模型](#5-权限与审批模型)
6. [Provider 抽象与模型路由](#6-provider-抽象与模型路由)
7. [流式与事件接口](#7-流式与事件接口)
8. [错误处理与可观测](#8-错误处理与可观测)
9. [可嵌入性与跨语言](#9-可嵌入性与跨语言)
10. [自研 SDK 定位与演进](#10-自研-sdk-定位与演进)
11. [Coze vs 自研：持久化与架构理念深度对比](#11-coze-vs-自研持久化与架构理念深度对比)
12. [总结矩阵](#12-总结矩阵)

---

## 1. 五 SDK 定位与调用入口

| SDK | 包名 / 形态 | 调用入口 | 核心抽象 |
|---|---|---|---|
| **Claude Agent SDK** | `@anthropic-ai/claude-agent-sdk`（TS）/ `claude-agent-sdk`（Py） | `query({prompt, options})` → `AsyncGenerator<SDKMessage>` | query 函数 + 流式消息 |
| **Cline SDK** | `@cline/sdk`（底层）+ 项目 `ClineApi` 封装 | `new ClineClient().ask(prompt)` → `Promise<ClineRunResult>` | Client 类 + Transport + Engine |
| **Trae Agent** | `trae-agent`（Python 开源） | `BaseAgent` 实例 + run 循环 | Agent 类 + ToolCaller + LLMClient + TrajectoryRecorder |
| **Coze SDK** | `@coze/api`（JS）/ `cozepy`（Py）/ `@coze/cli` | `new CozeAPI({token}).chat.stream(...)` / `workflow.stream_run(...)` | Client 类 + 资源 API + 事件流 |
| **自研 SDK** | `ClineApi`（Node）+ C# 工作流引擎 + CSG 契约 | Node: `ClineClient.ask()` / C#: `NodeExecutor.Execute(WorkflowContext)` | 双层：工作流引擎 + 执行 Agent + 跨语言契约 |

**关键观察**：
- Claude/Trae 是「函数式/类式 Agent 本体 SDK」——一个入口跑完整 agent loop。
- Coze 是「平台资源 API SDK」——编排能力在平台侧，SDK 只是调用接口。
- Cline（底层 `@cline/sdk`）是 Agent 本体；项目 `ClineApi` 是对它的二次封装（加了凭据/状态机/daemon 分离）。
- 自研是「双层 SDK」——C# 工作流引擎 + Node Cline 执行层，靠 CSG 契约联动。

---

## 2. 核心 API 风格对比

### Claude Agent SDK：AsyncGenerator 流式

```typescript
import { query } from "@anthropic-ai/claude-agent-sdk";

const response = query({
  prompt: "Review this code",
  options: {
    model: "claude-sonnet-4-5",
    workingDirectory: process.cwd(),
    allowedTools: ["Read", "Grep", "Glob"],
    permissionMode: "acceptEdits",
    hooks: { PostToolUse: [...] },
    max_turns: 50,
    max_budget_usd: 5,
  },
});

for await (const message of response) {
  if (message.type === "assistant") console.log(message.content);
}
```

- **风格**：函数式 + AsyncGenerator，调用方消费流式消息。
- **options 一次配齐**：model/工具/权限/hooks/预算/轮次上限。
- **边界**：官方明确「不含重试/持久化/多租户」，需外层实现。

### Cline（项目 ClineApi 封装）：Client 类 + Promise

见 [client.ts](file:///d:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\src\client.ts#L23-L92)：

```typescript
const client = new ClineClient({ defaults: { provider: "deepseek" } });
const result = await client.ask("重构这个模块", { thinking: "medium" });
await client.resume(result.sessionId, "继续");
```

- **风格**：类式 + Promise，ask/plan/resume/history 显式方法。
- **defaults 合并**：构造时全局默认 + 每次调用覆盖。
- **底层链路**：`ClineClient.run` → `runCline` → `SdkTransport.run` → `ClineCore`（见 [engine.ts](file:///d:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\src\engine.ts)）。
- **daemon/engine 分离**：执行面子进程独占 `@cline/sdk`，控制面心跳/预算杀进程互不阻塞。

### Trae Agent：BaseAgent 类 + run 循环

```python
agent = BaseAgent(config)
await agent.run(task)
# 内部：消息管理 / LLM 调用编排 / 工具调用管理 / 步骤追踪
# discover_mcp_tools() 动态扩展工具集
```

- **风格**：类式 + async run，agent 内部维护消息/步骤/工具集。
- **研究友好**：TrajectoryRecorder 记录完整执行轨迹，便于消融研究。
- **模块化**：ToolCaller / LLMClient / TrajectoryRecorder 可替换。

### Coze SDK：Client 类 + 资源 API + 事件流

```typescript
const client = new CozeAPI({ token: "pat", baseURL: COZE_COM_BASE_URL });
const v = await client.chat.createAndPoll({ bot_id, additional_messages: [...] });
// 或流式
for await (const event of client.chat.stream({ bot_id, ... })) {
  if (event.event === ChatEventType.CONVERSATION_MESSAGE_DELTA) { ... }
}
// 工作流
for await (const e of client.workflow.stream_run({ workflow_id, app_id, parameters })) { ... }
```

- **风格**：资源化 API（chat/bots/datasets/workflow/variables/voice），每个资源一组方法。
- **createAndPoll vs stream**：非流式轮询 / 流式事件二选一。
- **编排在平台**：SDK 只调接口，工作流节点（LLM/Plugin/Code/Condition/Loop/知识库/长期记忆）在平台定义。

### 自研 SDK：双层调用

- **Node 侧**：`ClineClient.ask()`（同 Cline 封装）。
- **C# 侧**：`NodeExecutor.Execute(WorkflowContext)`，工作流引擎编排节点，每节点调 Node 侧 Cline。
- **契约**：CSG CanonicalForm 跨语言上报状态。

**风格对比**：

| SDK | 范式 | 返回 | 编排位置 |
|---|---|---|---|
| Claude | 函数 + AsyncGenerator | 流式消息 | 调用方消费 |
| Cline | 类 + Promise | ClineRunResult | 调用方编排 |
| Trae | 类 + async run | trajectory | Agent 内部 |
| Coze | 资源 API | 事件流/轮询 | 平台侧 |
| 自研 | 双层（C# 编排 + Node 执行） | ClineRunResult + CSG | C# 工作流引擎 |

---

## 3. 会话与状态管理接口

| 维度 | Claude Agent SDK | Cline（ClineApi） | Trae | Coze | 自研 |
|---|---|---|---|---|---|
| **会话恢复 API** | `resume(sessionId)` | `client.resume(sessionId, prompt)` | 无显式 | conversation id | `client.resume(sessionId)` |
| **状态机接口** | 无（黑盒） | CSG CanonicalForm（显式） | TrajectoryRecorder | 平台日志 | **CSG 上报**（[state-machine.ts](file:///d:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\src\csg\state-machine.ts)） |
| **上下文压缩 API** | 无（CLI 侧 /compact） | 无 | 无 | 清除上下文节点 | 无 |
| **子任务 API** | subagent 编排 | 无 | 无 | 多 Agent @派发（平台） | CallFrame（C#） |
| **token 用量** | message 内带 | `ClineRunResult.meta`（input/output/cacheRead） | trajectory | `chat.usage` | **meta + stopReason** |
| **跨会话状态** | handoff doc（外化） | sessionId/dataDir | trajectory 文件 | 平台资产 | **SQLite（agent.db）+ LiteDB（graph.db）** |

**关键差异**：
- **Claude/Cline 的会话恢复是「同进程 sessionId 续聊」**，状态仍在 session 内。
- **Coze 的会话状态在平台**，SDK 只传 conversation id，平台管上下文。
- **自研的会话状态在 C# 工作流上下文**，Node 侧 Cline 只管单节点执行，CSG 把 Node 侧生命周期上报给 C#——这是唯一「跨语言状态契约」的 SDK。
- **自研的持久化是双库架构**：SQLite（[agent.db](file:///d:\Project\Program\WorkFlowPrj\Agent\data\agent.db)）作骨干 + LiteDB（[graph.db](file:///d:\Project\Program\WorkFlowPrj\Agent\data\graph.db)）作图引用层，支持 `PersistSessionAsync`/`RebuildAsync` 落库重放、`ISnapshotStore`/`ICheckpointStore`、三区存储、事务。详见 [DatabaseModule.cs](file:///d:\Project\Program\WorkFlowPrj\Agent\src\Agent.Launcher\Modules\DatabaseModule.cs)。

---

## 4. 工具扩展接口

| SDK | 内置工具 | 自定义工具 | MCP | 扩展机制 |
|---|---|---|---|---|
| **Claude** | 11 种（Read/Write/Edit/Bash/Glob/Grep...） | zod schema 定义 + `@tool`（Py） | `mcpServers` option | hooks 拦截 |
| **Cline** | 文件/终端/编辑（CLI 内置） | MCP Server | `clientType` 路由 | commandPermissions |
| **Trae** | bash/edit/str_replace_editor/view_image | ToolCaller 注册 | `discover_mcp_tools()` + `MCPClient.connect_and_discover` | 动态扩展工具集 |
| **Coze** | 插件节点（平台） | 插件开发 | 平台集成 | 工作流节点编排 |
| **自研** | Cline 内置 + MCPBridge | **MCPBridge（硬约束）** | MCPBridge 统一桥接 | 反射 `TryRegister*` |

**自研特点**：工具扩展受硬约束——「新文件系统/终端工具必须基于 MCPBridge 实现」（你 memory 里的约束），保证工具调用走统一桥接层，可观测、可审批。Claude 的 hooks 是「事件拦截」，自研的 MCPBridge 是「统一入口」，强度更高。

---

## 5. 权限与审批模型

| SDK | 模式 | 交互审批 | 命令级控制 |
|---|---|---|---|
| **Claude** | `permissionMode`: default/acceptEdits/plan/bypassPermissions | `canUseTool` 回调（异步） | `allowedTools`/`disallowedTools` + 模式匹配（`Bash(sudo *)`） |
| **Cline** | `autoApprove` boolean | 无（自动批准或不开） | `commandPermissions.allow/deny` + `allowRedirects`（见 [cline.config.json](file:///d:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\cline.config.json#L10-L22)） |
| **Trae** | 自动运行 + 黑名单 | 无 | 命令黑名单 |
| **Coze** | 节点配置 | 等待确认节点 | 平台配置 |
| **自研** | Cline 的 autoApprove + commandPermissions | 无 | 同 Cline |

**关键差异**：
- **Claude 的 `canUseTool` 是异步交互式审批**——可在工具执行前弹给用户/审批服务，是唯一支持「运行时动态审批」的 SDK。
- **Cline 是二元 autoApprove** + 命令黑白名单，无运行时交互审批。
- **自研沿用 Cline 模型**，无 `canUseTool` 等价物——若要接 Claude 的交互审批能力，需在 MCPBridge 层补 hook。

---

## 6. Provider 抽象与模型路由

| SDK | Provider 数 | 路由方式 | 自定义端点 |
|---|---|---|---|
| **Claude** | 1（Anthropic，多云 Bedrock/Vertex/Azure） | env + model id | `ANTHROPIC_BASE_URL` |
| **Cline** | 多（deepseek/openai/anthropic/gemini...） | 集中凭据 `credentials/cline.json` + `clientType` 路由 | `baseUrl` + `clientType`（见 [credentials.ts](file:///d:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\src\credentials.ts#L20-L41)） |
| **Trae** | 7+（OpenAI/Anthropic/Google...） | LLMClient 适配 | 配置文件 |
| **Coze** | 平台托管，主流模型切换 | 平台配置 | 平台配置 |
| **自研** | 同 Cline（多 Provider） | 同 Cline + 运行时 `ClineRunOptions.provider` 切换 | 同 Cline |

**自研优势**：`ClineRunOptions.provider` 支持每次运行切 provider，天然支持「任务分级路由」（简单任务 DeepSeek，复杂任务 Claude）。Claude Agent SDK 虽支持 DeepSeek 兼容（`ANTHROPIC_BASE_URL=https://api.deepseek.com/anthropic`），但本质仍是 Anthropic 协议单家。

---

## 7. 流式与事件接口

| SDK | 流式原语 | 事件类型 | 阶段追踪 |
|---|---|---|---|
| **Claude** | `AsyncGenerator<SDKMessage>` | assistant/user/result/stream | message.type |
| **Cline** | `EventCollector` + NDJSON | say/ask + reasoning + partial | `trace`（run-start/load-sdk/ensure-core/core-start/首块/ended）见 [sdk-transport.ts](file:///d:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\src\transport\sdk-transport.ts) |
| **Trae** | async run | step/tool_call | trajectory 记录 |
| **Coze** | stream / websocket | PING/Message/Done + CONVERSATION_MESSAGE_DELTA | 事件流 |
| **自研** | Cline EventCollector + CSG 阶段帧 | 同 Cline + csg 帧 | **trace + CSG（spawned/loading/ready/api）** |

**自研特点**：`trace` 是传输层阶段追踪（定位「卡在哪个阶段」），CSG 是生命周期状态机投影（跨语言契约）。两者互补——trace 给调试用，CSG 给 C# 编排层用。这是其他 SDK 没有的「双层可观测」。

---

## 8. 错误处理与可观测

| SDK | 重试 | 超时 | 失败归因 | 可观测输出 |
|---|---|---|---|---|
| **Claude** | 无（外层实现） | `max_turns`/`max_budget_usd` | error.code | message 流 |
| **Cline** | `retries` | `timeoutSec` | `stage`: ensureCore/api | ClineRunResult.meta + outputState + emptyReason |
| **Trae** | 无 | 配置 | trajectory | trajectory 文件 |
| **Coze** | 端上自动重试 | 平台 | 平台日志 | usage + 事件流 |
| **自研** | Cline retries + MCP 回退路径 | Cline timeoutSec + C# 预算杀 | `stage` + CSG 阶段 | **meta + stopReason + CSG + 诊断内存** |

**自研独特**：
- **失败归因到阶段**：`ClineRunResult.stage` 区分 `ensureCore`（SDK 初始化卡死）vs `api`（API 超时），见 [types.ts](file:///d:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\src\types.ts#L119-L124)。
- **空结果归因**：`outputState=missing` 时有 `emptyReason`（no_assistant_text/session_empty）。
- **回退路径**：Cline 失败 → MCP 回退（sk_plan→check_complete→dynamic_workflow_executor），你 memory 里的硬约束。
- **短板**：诊断数据存内存不导出（硬约束），跨进程调试观测性受限。

---

## 9. 可嵌入性与跨语言

| SDK | 语言 | 跨语言 | 嵌入形态 | 依赖 |
|---|---|---|---|---|
| **Claude** | TS / Py | 无（同语言内） | 库 + 内置 CLI | Node 18+ / Py 3.10+ |
| **Cline** | TS | 无 | 库 + headless CLI | `@cline/sdk` |
| **Trae** | Py | 无 | 库 | Py 3.10+ |
| **Coze** | JS / Py | 多语言 SDK | 平台 API client | HTTP |
| **自研** | C# + TS | **CSG CanonicalForm** | 工作流引擎 + 执行层 | C# + Node + CSG 协议 |

**自研独特**：唯一「跨语言状态契约」的 SDK。CSG 把 Node 侧 ClineCore 生命周期投影为 CanonicalForm JSON，上报给 C# 编排层——C# 不直接持有 Node 状态，但能观测/决策。这是 Claude/Cline/Trae 都没有的能力（它们都是单语言内闭环）。

---

## 10. 自研 SDK 定位与演进

### 10.1 定位

自研 SDK 在五者中的独特位置：**双层 + 跨语言契约 + 工作流编排内嵌**。

- 相对 Claude Agent SDK：有工作流编排层（C#），状态权威不在 session；有跨语言契约（CSG）；但缺 `canUseTool` 交互审批、缺 subagent 编排 API。
- 相对 Cline 底层 SDK：ClineApi 封装加了凭据集中管理 + CSG 状态机 + daemon/engine 分离 + 失败归因，是「生产化封装」。
- 相对 Trae：Trae 研究友好（trajectory），自研生产友好（CSG 可观测 + 回退路径）。
- 相对 Coze：自研是本地引擎 + 双库持久化（SQLite+LiteDB），Coze 是平台 API + 平台托管存储；自研的「工作流节点」在 C# 引擎，Coze 的在平台。自研在结构化持久化（图引用层/事务/Checkpoint/三区存储）上更细致，但缺语义化长期记忆（Memory.md/向量检索/RAG）。

### 10.2 SDK 层短板

1. **无交互审批 API**：缺 Claude `canUseTool` 等价物，运行时不能动态审批工具。
2. **无 subagent 编排 API**：CallFrame 在 C# 侧，Node 侧 ClineApi 无子任务接口。
3. **无 compaction API**：单次 run 内长 prompt 无自动总结。
4. **无 rewind API**：run 失败只能整体重跑或切 provider。
5. **诊断内存不导出**：跨进程调试受限（注：会话/快照/Checkpoint 落 SQLite，诊断数据内存不导出是硬约束，两者正交）。
6. **无语义向量检索**：有长期记忆（快照 + 引用图 + 事件三区 + 知识表），但缺 Memory.md 式语义向量检索与 RAG——是检索形态差异，非"无长期记忆"。

### 10.3 演进建议（SDK 层，按优先级）

**P0：补 rewind API（ClineApi 层）**

在 `ClineClient` 加 `retryFrom(sessionId, messageIndex, hint)`：基于 `ClineRunResult.messages` 截断到某条消息，带教训重试。利用已有 [messages 数组](file:///d:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\src\types.ts#L111) 做截断点。成本低、收益直接。

**P1：补 canUseTool 等价物（MCPBridge 层）**

在 MCPBridge 加工具调用前 hook，支持异步审批回调。对齐 Claude `canUseTool` 模型，让自研也有运行时动态审批。这是安全审计场景的刚需。

**P2：补 subagent API（ClineApi 层）**

`ClineClient.subagent(prompt, { isolated: true })`：独立 sessionId/dataDir，只回传 resultText。对应 Claude subagent 编排，落地到你 memory 的 CallFrame 抽象。

**P3：补节点级 compaction（C# 工作流层）**

工作流节点间传递 handoff summary 而非全量 messages。参考 Claude handoff doc 思路，但落到 nodeKey 间。

**P4：Claude 作模型级 fallback（ClineApi 层，零架构改动）**

利用 [types.ts](file:///d:\Project\Program\WorkFlowPrj\TypeScriptPrj\TsWorkspace\ClineApi\src\types.ts#L46-L51) 已支持的 `clientType:"anthropic"`，在 `credentials/cline.json` 加 anthropic provider，Cline 失败时切 Claude 重试一次再走 MCP 回退。

### 10.4 不建议做的

- **不要直接用 `@anthropic-ai/claude-agent-sdk` 替代 ClineApi**：会丢 CSG 状态机、daemon/engine 分离、失败归因、凭据集中管理这些生产化封装。
- **不要把 Coze 式平台 API 引入**：自研是本地引擎，平台 API 化会牺牲可控性与跨语言契约。
- **不要为 compaction 牺牲按节点重置**：自研的「节点级 prompt 受控」已规避大部分 context rot，compaction 只在单节点长 prompt 时补。

---

## 11. Coze vs 自研：持久化与架构理念深度对比

> 本节针对 Coze 与自研框架在「上下文存储与长期化」上的具体差异，从架构设计与使用理念两个层面展开。

### 11.1 本质定位

- **Coze**：无状态 Bot + 平台组件拼装。Bot 本身 Stateless（对话变量会话结束即清），长期化靠拼装平台组件（表格/记忆文件/知识库/数据库）。
- **自研**：有状态工作流引擎 + 双库持久化 + 跨语言契约。C# 工作流引擎持权威状态，SQLite 骨干 + LiteDB 图引用层，内存后盾 ↔ 落盘后盾双层。

### 11.2 自研的双库架构（先厘清事实）

| 数据库 | 类型 | 文件 | 角色 |
|---|---|---|---|
| **SQLite** | 关系型 | [agent.db](file:///d:\Project\Program\WorkFlowPrj\Agent\data\agent.db) | 持久化骨干 |
| **LiteDB** | NoSQL 文档型 | [graph.db](file:///d:\Project\Program\WorkFlowPrj\Agent\data\graph.db) | 图引用层 |

配置见 [agent-config.json](file:///d:\Project\Program\WorkFlowPrj\Agent\Config\agent-config.json)：`Provider: "Sqlite"`, `TablePrefix: "ur_"`, `AutoCreateSchema: true`。

**SQLite（agent.db）能力清单**（见 [DatabaseModule.cs](file:///d:\Project\Program\WorkFlowPrj\Agent\src\Agent.Launcher\Modules\DatabaseModule.cs)）：

| 能力 | 接口 | 说明 |
|---|---|---|
| 三区存储 | `IZoneStore` | Inputs/Outputs/Events，threadId 分组 + afterId 分页 |
| 快照 | `ISnapshotStore` | `SaveSnapshotAsync(threadId, sessionId, snapshotJson)` / `LoadSnapshotAsync` |
| Checkpoint | `ICheckpointStore` | 检查点存储 |
| 配置即数据行 | `IConfigStore` | Get/Set/Delete/GetAll |
| 会话落库 | `PersistSessionAsync` | session 序列化到长期库 |
| 会话重建 | `RebuildAsync` | 从长期库重放重建内存 |
| 事务 | `BeginTransactionAsync` | 数据库原生事务 |
| 元工作流表 | — | MetaWorkflows/MetaWorkflowNodes/MetaWorkflowRelationships |
| 通用数据表 | — | GenericData/InputTemplates/Workflows/Codes/DataSchemas/Prompts |

**LiteDB（graph.db）能力**：`IResourceReferenceStore` 资源引用 + 图节点/边 + 图算法（`find_shortest_path`/`get_dependencies`/`CalculateReferenceDiffAsync`），映射 `IUniversalReferenceManager`，连接 SQLite 与文件系统。详见 [06_数据库使用逻辑.md](file:///d:\Project\Program\WorkFlowPrj\docs\Introduction\Universal\06_数据库使用逻辑.md)。

**三层架构**：

```
内存后盾（InternalStorage）  ←→  SQLite 落盘骨干（agent.db）
                                    ↑ 数据同步
                              LiteDB 图引用层（graph.db）
                                    ↑ 引用同步
                              文件系统（模块代码/资源）
```

`DataStoreEngine` 协调内存后盾 ↔ 落盘后盾，`ReactiveZoneBridge` 反应式桥接（写即触发）。

### 11.3 架构设计层面的具体差异

**1. 状态归属：Bot 无状态 vs 工作流有状态**

- Coze：Bot Stateless，状态靠拼装平台组件，每次对话从表格/记忆/知识库加载。状态不在 Bot 里，在平台组件里。
- 自研：工作流引擎有状态常驻，`InternalStorage`（内存后盾）↔ `IDataStore`（落盘后盾）双层，`DataStoreEngine` 协调。状态在进程内存，落盘是同步而非加载。

**2. 持久化分层逻辑：按数据语义 vs 按存储职责**

- Coze：按数据语义分层——热（对话变量）/ 温（Coze 表格）/ 冷（外部 DB）/ 语义（Memory.md）/ RAG（知识库）。每种数据类型一个专门载体。
- 自研：按存储职责分层——内存后盾 / SQLite 骨干 / LiteDB 图引用层 / 文件系统。同一份数据可跨层，靠引擎协调。

**3. 记忆机制：文件化语义记忆 vs 结构化会话落库**

- Coze：记忆是语义资产——`SOUL.md`/`USER.md`/`MEMORY.md` 等 Markdown 文件，向量检索，自动提取 + 定期整理 + 跨端共享 + 可视化。越用越懂你。
- 自研：记忆是执行态快照——`PersistSessionAsync` 落库 + `RebuildAsync` 重放。进程崩溃可恢复，但无语义检索。

**4. 图能力：无 vs LiteDB 图引用层**

- Coze：无显式图数据库，依赖关系靠工作流节点连线。
- 自研：LiteDB 专门做引用关系层，跑图算法（`find_shortest_path`/`get_dependencies`），连接 SQLite 与文件系统。这是自研独有。

**5. 跨语言/跨进程：平台黑盒 vs CSG 契约**

- Coze：跨端共享靠平台，黑盒，用户只看到 Memory.md 结果。
- 自研：CSG CanonicalForm 是显式跨语言契约，C# 编排层能观测 Node Cline 执行层生命周期（spawned→loading→ready→api），可决策。

**6. 编排位置：平台节点画布 vs C# 工作流引擎**

- Coze：编排是平台侧节点画布，节点即状态机（清除上下文节点 = 显式 reset）。
- 自研：编排是 C# 工作流引擎，节点（nodeKey）在 PipelineContext，持久化在 DatabaseModule。

### 11.4 使用理念层面的具体差异

**1. 谁拥有数据：平台托管 vs 本地可控**

- Coze：数据在云端，平台托管，用户用 Memory.md 查看。跨端共享免费，但数据不在自己手里。
- 自研：数据在本地双库，完全可控，但跨端共享要自己实现。

**2. 谁来编排：可视化节点 vs 代码工作流**

- Coze：无代码/低代码，平台画布拖节点，适合非开发用户。
- 自研：代码工作流引擎（C# NodeDefinition 指定 Function/Condition/Loop/Parallel），适合开发者，更精细可控。

**3. Agent 定位：数字员工 vs 执行智能体**

- Coze：Agent 是「数字员工」——有专属邮箱、日程、工作台，养成系伙伴。
- 自研：Agent 是「执行智能体」——Cline 作执行末梢，工作流引擎调度，任务完成即释放。

**4. 扩展边界：平台生态 vs 本地硬约束**

- Coze：扩展靠平台插件市场 + 技能，多渠道接入（飞书/微信/网页）。
- 自研：扩展靠 MCPBridge 硬约束 + 反射 `TryRegister*` 容错注册，工具调用必须走统一桥接层。

**5. 失败哲学：平台 SLA 兜底 vs 显式回退路径**

- Coze：平台 SLA 兜底，端上自动重试（-1/3003 错误），用户无感。
- 自研：显式回退路径（CLine 失败 → MCP 回退：sk_plan→check_complete→dynamic_workflow_executor），CSG 可观测，失败归因到阶段。

**6. 三区存储理念：Coze 无对应 vs 自研独有**

- Coze：无「输入区/输出区/事件区」概念，对话就是对话。
- 自研：三区存储（Inputs/Outputs/Events，threadId 分组 + afterId 分页）+ 反应式桥接（ReactiveZoneBridge 写即触发）。为异步任务队列 + 事件溯源设计。

### 11.5 总览矩阵

| 维度 | Coze | 自研 |
|---|---|---|
| **状态归属** | Bot 无状态，组件拼装 | 工作流有状态常驻 |
| **持久化分层** | 按数据语义（热/温/冷/语义/RAG） | 按存储职责（内存/骨干/图/文件） |
| **关系型存储** | Coze 表格 | **SQLite（agent.db）** |
| **文档/图存储** | 无（平台黑盒） | **LiteDB（graph.db）+ 图算法** |
| **记忆机制** | 文件化语义记忆 + 向量检索 | 结构化会话落库 + 重放重建 |
| **会话落库/重建** | conversation id（平台管） | **PersistSessionAsync + RebuildAsync** |
| **快照** | 平台内部 | **ISnapshotStore（threadId+sessionId）** |
| **Checkpoint** | 无显式 | **ICheckpointStore** |
| **三区存储** | 无 | **Inputs/Outputs/Events + 分页** |
| **事务** | 无 | **BeginTransactionAsync** |
| **图能力** | 无 | **LiteDB + 图算法** |
| **编排位置** | 平台节点画布 | C# 工作流引擎 |
| **跨语言** | 平台黑盒 | CSG 显式契约 |
| **数据归属** | 平台托管 | 本地双库 |
| **Agent 定位** | 数字员工（养成） | 执行智能体（释放） |
| **扩展** | 平台插件市场 | MCPBridge 硬约束 |
| **失败处理** | 平台 SLA 兜底 | 显式回退路径 + 阶段归因 |
| **程序性记忆（执行状态）** | conversation id（平台管） | **快照 + Checkpoint + 会话落库/重放** |
| **结构性记忆（关系）** | 无 | **LiteDB 引用图 + 图算法** |
| **事件记忆（历史）** | 无 | **三区存储（Inputs/Outputs/Events）** |
| **知识记忆（配置/定义）** | 知识库（RAG 向量） | **元工作流表 + 通用数据表（结构化）** |
| **语义记忆（懂用户）** | Memory.md + 向量检索 | ❌ 无（检索形态差异，非无长期记忆） |
| **跨端共享** | 飞书/微信/网页 | ❌ 无（本地） |

### 11.6 核心洞察

两者的根本分歧在**记忆的形态与检索方式**，而非"有无长期记忆"：

**自研的长期记忆体系**（四类，比 Coze 更多元）：

| 记忆类型 | 载体 | 检索方式 |
|---|---|---|
| **程序性记忆**（执行状态） | 快照（ISnapshotStore）+ Checkpoint + 会话落库 | 按 threadId+sessionId 精确恢复 |
| **结构性记忆**（关系） | LiteDB 引用图 | 图算法（find_shortest_path/get_dependencies） |
| **事件记忆**（历史） | 三区存储（Inputs/Outputs/Events） | threadId 分组 + afterId 分页 |
| **知识记忆**（配置/定义） | 元工作流表 + 通用数据表 | 结构化查询 |

**Coze 的长期记忆体系**（两类）：

| 记忆类型 | 载体 | 检索方式 |
|---|---|---|
| 程序性记忆（执行状态） | conversation id（平台管） | 平台加载 |
| **语义记忆**（懂用户） | Memory.md + 向量检索 | 语义近似查询 |

- **Coze 把记忆做成了「语义资产」**——Memory.md 文件化，向量检索，自动养成，跨端共享。强在语义近似匹配，弱在无结构关系推理。
- **自研把记忆做成了「结构化执行态」**——快照/Checkpoint/重放保障崩溃恢复，引用图做依赖分析，三区做事件溯源。强在精确恢复与图关系推理，弱在无语义近似查询。

**自研不缺长期记忆**（四类齐全），缺的只是**「语义向量检索」这一种检索形态**——可选在现有 SQLite 上叠向量索引，让 `ClineRunResult.resultText` 可按语义近似查询，作为图算法精确检索的补充。这不是"补长期记忆"，而是"补一种检索形态"。

---

## 12. 总结矩阵

| 维度 | Claude Agent SDK | Cline（ClineApi） | Trae Agent | Coze SDK | 自研 SDK |
|---|---|---|---|---|---|
| **入口范式** | query AsyncGenerator | Client + Promise | BaseAgent run | 资源 API + 事件流 | 双层（C#+Node） |
| **会话恢复** | resume(sessionId) | resume(sessionId) | 无 | conversation id | resume(sessionId) |
| **状态机** | 黑盒 | CSG CanonicalForm | trajectory | 平台日志 | **CSG 显式** |
| **工具扩展** | zod + MCP + hooks | MCP + commandPermissions | MCPClient 动态发现 | 平台插件节点 | **MCPBridge 硬约束** |
| **交互审批** | canUseTool 回调 | autoApprove 二元 | 黑名单 | 等待确认节点 | autoApprove（无 canUseTool） |
| **Provider** | 单家（多云） | 多家凭据集中 | 7+ 适配 | 平台托管 | 多家 + 运行时切换 |
| **流式** | AsyncGenerator | EventCollector + NDJSON | async | stream/websocket | EventCollector + CSG 帧 |
| **失败归因** | error.code | stage(ensureCore/api) | trajectory | 平台日志 | **stage + emptyReason** |
| **重试/回退** | 无 | retries | 无 | 端上重试 | **retries + MCP 回退** |
| **跨语言** | 无 | 无 | 无 | 多语言 SDK | **CSG 契约** |
| **编排位置** | 调用方 | 调用方 | Agent 内部 | 平台 | **C# 工作流引擎** |
| **持久化** | 无（CLI 侧） | sessionId/dataDir | trajectory | 平台托管 | **SQLite+LiteDB 双库** |
| **长期记忆** | handoff doc | 无 | 无 | Memory.md+向量检索 | **快照+引用图+事件三区+知识表** |
| **核心优势** | 流式 + 交互审批 | 生产化封装 | 研究友好 | 平台生态 | 跨语言契约 + 编排内嵌 + 双库持久化 |
| **核心短板** | 无重试/持久化 | 无交互审批 | 无会话恢复 | SaaS 依赖 | 无 canUseTool/subagent |

---

## 参考来源

- Claude Agent SDK（TS）参考：https://docs.anthropic.com/id/docs/claude-code/sdk/sdk-typescript
- Claude Agent SDK 权限管理：https://platform.claude.com/docs/it/agent-sdk/permissions
- Claude Agent SDK 入门（TS/Py）：https://blog.csdn.net/weixin_63132747/article/details/160986405
- Claude Agent SDK 实践：https://www.clauder-navi.com/claude-agent-sdk/
- Trae Agent 深度技术分析：https://blog.csdn.net/zhailuxu/article/details/162495066
- Coze CLI 官方文档：https://docs.coze.cn/developer_guides_coze_cli
- Coze Python SDK Quick start：https://docs.coze.com/developer_guides/python_getting_started
- Coze JS SDK（@coze/api）：https://github.com/coze-dev/coze-js/blob/main/packages/coze-js/README.md
- Coze 工作流节点文档：https://github.com/Corddt/coze-workflow-docs/blob/main/workflow_nodes.md
- Anthropic Claude SDK 自定义端点：https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/typescript
