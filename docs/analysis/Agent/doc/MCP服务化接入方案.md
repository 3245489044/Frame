# MCP 服务化接入方案（Cline 等外部智能体 ⇄ Agent 框架）

> **版本**：1.1
> **日期**：2026-08-18（v1.0）· 2026-08-20（v1.1 落地补充：本地数据库读写工具面 + answer 作为调用方）
> **范围**：把 Agent 框架的「能力面」包装为 MCP Server（薄封装 `AgentUi_RunJson` 的 action 面），让 Cline 等 MCP 客户端智能体**作为主控**主动驱动框架（任务生命周期 / 节点读写 / 数据区查询 / 图查询）；控制面（五步骨架 / 混合调度 / 预算暂停门 / 写透对账）**保留在框架内**，不外包。
> **依据**：`Agent/README.md`（§MCP 工具调用链 / Cline 执行器）、`Agent/任务池调度循环执行链.md`（外部入口层 / PsRuntimeBinding）、`Agent/数据驱动反应式Agent架构设计.md`（§十 外部适配器 / 叠加门面）、`Agent/节点即存储单元：Agent三线存储架构介绍.md`（双库 / 写透守卫）、`Agent.Launcher/Modules/AgentUiLibraryRuntime.cs`（`RunJsonAsync` 分发）、`Agent.Modules/MCP/McpClientManager.cs` + `McpToolFunction.cs`（现有 MCP 客户端）、`Agent.Modules/Cline/ClineAgentExecutor.cs`（现有 Cline 执行器）。

---

## 一、背景与问题

### 1.1 框架的「控制程度高」是什么

Agent 框架的控制面由三层组成，全部是原生 C# 深度绑定：

| 层 | 内容 | 绑定方式 |
|----|------|----------|
| **执行层** | 节点函数（`WorkFlowMethodUsage.CallFunctionAsync`）、注册函数库 | C# API / 函数库装配 |
| **状态层** | 任务=快照（`ur_tasks.SnapshotJson`）、节点库（`ur_nodes`）、图库（`graph-log.db`） | 表语义 / NodeType 判别 / 写透守卫 |
| **编排层** | 五步骨架（guard_input→provide_prompt→cline_execute→rule_match→guard_output）、混合调度（`LlmStateDirector`/`TransitionGuard`）、预算/熔断/终止、暂停门 | `SnapshotDrivenLoopEngine` + `AgentRunner` |

对外部智能体（Cline 等）而言，**无法也不应直接接触这三层**——它们需要的不是控制面，而是**能力面**："帮我起一个任务、查它的状态、读某个节点输出、看影响链"。

### 1.2 核心命题

> **让 Cline 作为主控使用这套框架，最佳边界形态 = 把「能力面」包装为 MCP Server，薄封装 `AgentUi_RunJson` 这条已存在的协议化入口；「控制面」保留在框架内，经框架治理闸门放行。**

理由：
- MCP 是语言无关的标准工具协议，Cline 原生支持（工具名 + JSON 参数），Cline **无需理解任何 C# 内部细节**；
- 工具即白名单：MCP 工具清单 = 明确的能力边界，比 skill 提示词（软约束）强得多；
- 相比「纯数据面」（Cline 直接读写 ur_inputs/ur_outputs/节点表），工具面封装了**动作语义**（暂停/删除/重放不用变成"写什么行"），对智能体更友好。

---

## 二、现状盘点（已核实代码事实）

### 2.1 现有 MCP：客户端方向（Agent 消费外部工具）

```
McpToolFunction.CallToolAsync(context)        // 工作流函数 mcp_call_tool
  → McpClientManager.CallToolAsync(name, args)
      → stdio JSON-RPC（M 2.0）
      → MCPBridge（Node.js，TypeScriptPrj/TsWorkspace/MCPBridge）
      → HTTP/SSE → Docker 容器内 MCP 服务器（sequential-thinking / context7 / memory / lexis-mcp 等）
```

- 装配：`AgentOptionalModules.RegisterMcpTools`（`AgentConfig.RegisterMcpTools`，默认 true）；配置 `Agent/Config/mcp-servers.json`；
- 注册函数：`mcp_call_tool` / `mcp_list_tools`（`McpToolFunction.Register`，`WorkFlowMethodUsage.RegisterAsyncFunction`，RegisterOnce 幂等）；
- 注册失败被 try-catch 吞掉（仅记日志），不阻断启动。

> **结论**：现有 MCP 是「Agent 作为 MCP 客户端调用外部工具」——框架能力向外暴露的方向**尚未实现**，本方案补这一方向。

### 2.2 现有 Cline 接入：执行器方向（框架主控）

```
五步骨架 cline_execute
  → ClineAgentExecutor（IAgentExecutor）
      → ClineClient → daemon 池 → Node 桥（ClineDaemonPool / ClineDaemonIpc / ClineDaemonTransport）
      → 返回 ClineResult（含 token 遥测 / FailureStage / OutputState）
```

- 框架持有主循环与治理权：预算（5 分钟单次封顶）、暂停门（`CheckLlmGateAsync` → `WaitIfPausedAsync`）、空间边界（Cwd/DataDir）、取消令牌（Functor/AsyncLocal 透传）；
- 职责：Cline 在 `cline_execute` 步骤内做自由探索，**被框架编排**，无法主动编排多任务。

> **结论**：已有「框架主控 + Cline 执行器」路径；缺「Cline 主控 + 框架服务化」路径——本方案补上。

### 2.3 现成的协议化入口：`AgentUi_RunJson`（本方案的服务面底座）

`AgentUiLibraryRuntime.RunJsonAsync(requestJson)`：解析单一 JSON 请求体（`RunRequest` DTO，字段与 `DrivenConfig/Templates/AgentRun/*.json` 一一对齐），按 `action` 判别式分发：

| action | 语义 | op / 参数 |
|--------|------|-----------|
| `runLoop` | 前台瞬时循环（mode=Hybrid 缺省） | `input` / `mode` / `schedulerRulesKey` / `taskKey` |
| `task` | 统一任务请求（写 ur_tasks） | op=`enqueue`（写即激活）/ `status`（仅状态写）/ `delete` / `submit`（提交输入区） |
| `node` | 直写 ur_nodes 节点库 | op=`write`（NodeType 判别）/ `delete`；`nodeKey` / `nodeType` / `payload` |
| `addResource` 系 | Graph 资源镜像 | `resourceType` / `id` / `name` |
| `addReference` 系 | Graph 引用边 | `sourceType/sourceId/targetType/targetId/refType` |
| `query` | 统一只读查询 | `target`（runtimestate\|status\|reactivestatus\|agentstatuses\|processpending\|outputs\|inputs\|events\|sessions\|config\|configGet\|checkpoints\|resources\|snapshot\|taskList\|taskStatus）；`threadId` / `key` / `sessionId` |

> 该入口已覆盖本方案「能力面」的绝大部分动作，**MCP Server 只需薄封装它**，不新造逻辑（SingleImplementation，避免双真源）。

### 2.4 其他外部化先例

| 先例 | 形态 | 与本方案关系 |
|------|------|--------------|
| `Agent.StorageBridge.exe` | 独立进程 C# 写入口（store-resource / get-resource） | "框架能力包装为外部可调用协议"的已有实践 |
| `Invoke-AgentStep`（PsRuntimeBinding） | PowerShell 确定性动作协议（`read.file` / `store.resource` / `list.dir`...） | Cline 已经经 skill（analyze-folder）在使用——**MCP 是把"软约束提示词 + PS 动作"升级为强类型标准协议** |
| `NamedPipeClient` / `PipeModeRunner` | CLI 管道投喂（FILEDRIVEN_INSTANT_BODY） | stdio JSON-RPC 传输的现成经验 |

---

## 三、方案设计：MCP 能力面封装

### 3.1 边界原则（能力面 vs 控制面）

```
【暴露 · 能力面】请求→响应，无状态语义
  · 任务生命周期：enqueue / status / pause / resume / delete / processPending
  · 节点读写：node write / delete / output 查询
  · 数据区查询：outputs / inputs / events / config / checkpoints / snapshot / sessions
  · 图查询：resources / references / 影响链（FindPaths / Dependencies）

【保留 · 控制面】留在框架内，不外包
  · 五步骨架、混合调度（LlmStateDirector / TransitionGuard）
  · 预算 / 熔断 / 终止二分（budgetTerminate / TerminationDecider）
  · 暂停门（WaitIfPausedAsync）、取消令牌
  · 写透与对账（WriteThrough* / ReferenceGrounding / Reconcile）
```

> 外部智能体**必须经框架治理闸门**：Cline 通过 MCP 提交任务后，循环在框架内跑，预算/暂停/终止仍由框架裁决——对齐公理 `DeterministicCoreDisjoint`（确定性写入/裁决只归框架，不归外部 LLM）。

### 3.2 工具清单设计（草案）

两种实现方式（可组合）：

**方式 A：通用工具（最薄，零逻辑）**

| 工具 | 入参 | 行为 |
|------|------|------|
| `agent.run` | `json`（请求体，同 RunRequest 字段） | 透传 `RunJsonAsync(json)`，原样返回结果文本 |

**方式 B：强类型工具组（对智能体友好，每个工具内部仍只调 `RunJsonAsync`/存储桥）**

| 工具 | 入参（必填加粗） | 说明 |
|------|------------------|------|
| `task_enqueue` | **`input`**，`taskKey?` `mode?`(Hybrid\|Config\|Deterministic) `schedulerRulesKey?` `priority?` `maxRetries?` `relatedNodes?[]` `threadId?` `inputKey?` | 写 ur_tasks 即激活消费；返回 taskKey（异步，见 §3.3） |
| `task_status` | **`taskKey`** | 单任务状态 + 载荷摘要（Status/Priority/Rounds/LastEval...） |
| `task_pause` / `task_resume` | **`taskKey`** | 合作式暂停/恢复（Running⇄Paused 非终止） |
| `task_delete` | **`taskKey`** | 删除任务（级联清图镜像） |
| `task_process_pending` | — | 死 Running → NotStarted 重放续跑 |
| `task_list` | `status?` `limit?` | 任务池分页列表 |
| `node_write` | **`nodeKey`**，`nodeType?`(node\|input\|output\|event\|config\|checkpoint\|keymapping\|resource) `payload?` `threadId?` | 直写 ur_nodes（幂等 upsert） |
| `node_delete` | **`nodeKey`** | 删除节点 |
| `query` | **`target`**（16 个目标之一），`threadId?` `key?` `sessionId?` | 统一只读查询（复用 query 分发） |
| `graph_list_resources` | `resourceType?` | 图资源镜像列表 |
| `graph_list_references` | **`resourceType`** **`id`**，`includeIncoming?` | 引用边列表 |
| `graph_paths` | **`fromId`** **`toId`**，`maxDepth?` | 影响链/因果链路径（FindPaths） |

> 每个工具实现体内**只做参数映射 → 调 `RunJsonAsync`（或 `AgentUiLibraryData` 对应方法）→ 格式化返回**；不复制分发/校验/落库逻辑（对齐 `SingleImplementation` / TH-12）。工具返回统一信封：`{ status: success|error, data, message? }`。

### 3.3 异步任务模式

MCP tools 是**请求-响应**，任务池是**异步消费**——两者之间用「提交 + 轮询」模式：

```
task_enqueue(input, taskKey?)  → 立即返回 { taskKey, status: "NotStarted" }   // 写即激活
  ↓（框架任务池后台消费，框架内完成多轮循环）
task_status(taskKey)           → { status: "Running|Completed|Faulted|Paused", rounds, lastDecision, ... }
query(target=outputs, inputKey) → 轮询直至输出出现
```

- 不做同步阻塞（MCP 工具超时会断开，长时任务必须异步）；
- 可选增强（后续）：MCP notifications 推送任务完成事件，减少轮询；
- 幂等：`taskKey` 幂等 upsert，重复 enqueue 不产生重复任务（对齐 at-least-once + Idempotency 语义）。

---

## 四、实现选型

| 项 | 选型 | 说明 |
|----|------|------|
| 协议 SDK | C# 官方 `Microsoft.ModelContextProtocol`（首选）或复用现有 stdio JSON-RPC 基建 | 官方 SDK 免维护协议层；现有 `McpClientManager` / `NamedPipeClient` 的 JSON-RPC 经验可复用 |
| 传输 | **stdio 起步**；跨进程/远程再上 HTTP/SSE | 与框架宿主同机场景 stdio 最简；`MCPBridge` 已演示 HTTP/SSE 侧能力 |
| 进程模型 | MCP Server 作为**独立宿主进程**，内部加载 `AgentFramework`（组合根复用） | 复用 `AgentFrameworkAssembler` 装配链，不侵入现有 Launcher；与 `StorageBridge.exe` 同模式 |
| 与现有 MCPBridge | 并存，不冲突 | MCPBridge 是 Node 侧**客户端聚合**；新 Server 是 C# 侧**服务面**，方向互补 |

---

## 五、装配与配置

- 新增配置开关 `AgentConfig.EnableMcpServer`（**默认 false**，可回退，对齐 `RegisterMcpTools` 风格）；
- 装配点：`AgentOptionalModules`（与 `RegisterMcpTools` 并列，条件启用）；
- 配置：复用 `Agent/Config/mcp-servers.json` 扩展 `McpServers`（新增自身 server 声明），或独立 `mcp-server-config.json`；
- 生命周期：随框架宿主启动/停止；独立进程时经命名管道/端口暴露工具端点（stdin/stdout 或 HTTP）。

---

## 六、边界与治理

1. **白名单工具面**：Cline 只能调用工具清单内的能力——比 skill 提示词约束强；超出白名单的框架能力（内部函数、直接落库）不暴露；
2. **控制面不外包**：预算/终止/暂停/写透全部留在框架内，MCP 层无任何裁决逻辑；
3. **双真源防护**：MCP 工具实现只调 `RunJsonAsync`/`AgentUiLibraryData`，**不复制分发与落库逻辑**；校验参数映射到 RunRequest 字段即可；
4. **权限与审计**：工具调用可写日志/事件（对齐 `IObservabilitySink` 或 `ur_events`），外部智能体行为可追溯；
5. **失败语义**：任务失败保留错误行可重放（`task_process_pending`），不阻塞后续任务。

---

## 七、与现有形态的关系（两条路径并存）

```
路径一（现状，框架主控）：Agent 主循环 → cline_execute → ClineAgentExecutor（Cline 是被编排的执行器）
路径二（本方案，Cline 主控）：Cline（MCP 客户端）→ MCP Server（薄封装）→ RunJsonAsync → 任务池/节点库/图库
```

- 两者不互斥：同一框架可同时暴露「执行器」与「MCP 服务」两个面；
- 符合「叠加门面」原则：MCP 层是叠加在 `AgentUi_RunJson`/存储桥之上的门面，**不删不改现有 API，可开关可回退**；
- 场景划分：需要框架治理的复杂 Agent 编排 → 路径一；外部智能体要复用框架存储/任务能力做自己的编排 → 路径二。

---

## 八、风险与后续

| 风险 | 缓解 |
|------|------|
| 异步任务语义（工具请求-响应 vs 任务池异步） | §3.3 提交+轮询模式；后续 notifications 推送 |
| 双通道一致性（RunJson 与 MCP 两条入口） | 工具实现一律透传 `RunJsonAsync`（SingleImplementation） |
| 权限失控（外部智能体越权） | 白名单工具面 + 控制面不外包 + 审计事件 |
| 运维面增加（多一个进程/端口） | 独立 MCP 宿主进程复用组合根；stdi o 起步最小化 |
| Cline 协议端（`ClineAgentExecutor` 的 Node 桥）与 MCP 并存复杂度 | 职责划分明确：执行器 vs 服务面，文档沉淀 |

**后续可选**：MCP notifications 推送任务完成事件；`agent.run` 通用工具与强类型工具组的自动生成（从 `RunRequest` 字段 + `query` target 枚举反射生成 schema）；远程 HTTP/SSE 部署形态。

---

## 九、一句话总结

> **MCP 服务化 = 把 `AgentUi_RunJson` 的 action 面（任务/节点/查询/图）暴露为 MCP 工具白名单，只透传不复制、控制面留在框架内**——为 Cline 等外部智能体补上「主控」路径，与「框架主控 + Cline 执行器」并存，符合叠加门面、可开关可回退、SingleImplementation 不双真源的一贯原则。

---

## 十、落地补充：本地数据库读写工具面 + answer 作为调用方（v1.1，已实现）

> 本章是 v1.0 方案在「框架内部统一数据访问」方向的落地：**不另起独立 MCP Server 进程**（避免运维面增加），
> 而在 `Agent.Modules/MCP` 内把本地数据库读写封装为统一工具面，让 **answer 与 cline 都以标准工具接口调用**（answer 经 SK Function Calling 作为调用方）。

### 10.1 与 v1.0 的关系

| 维度 | v1.0 方案 | v1.1 落地 |
|------|-----------|-----------|
| 方向 | 框架能力向外暴露（Cline 作为主控连 MCP Server） | 框架内部统一数据访问（answer / cline 经 `db_*` 工具读写本地库） |
| 形态 | 独立 MCP Server 进程 + stdio/HTTP | `Agent.Modules/MCP/LocalDbMcpTools` 库内工具面（不新增进程） |
| 工具 | `agent.run` / `task_*` / `node_write` / `query` / `graph_*` | `db_read_node` / `db_list_nodes` / `db_write_node` / `db_delete_node` / `db_summary` |
| 调用方 | Cline（MCP 客户端） | answer（SK Function Calling，S3）+ cline（工作流函数 / FileDriven） |

### 10.2 工具面实现（LocalDbMcpTools）

`Projects/Agent/src/Agent.Modules/MCP/LocalDbMcpTools.cs`，两套入口收敛到共享 core（SingleImplementation）：

- **`db_*` 工作流函数**（`Register()` → `WorkFlowMethodUsage.RegisterAsyncFunction`，复用 `mcp_call_tool` 模式）：供引擎 / FileDriven / cline 经框架工具面调用。
- **`LlmTool` 工具集**（`GetTools()` → `ILlmClient.ChatWithToolsAsync`）：供 answer 作为调用方经 SK Function Calling 自主读写。
- 内部只调 `INodeStore` 存储端口（ur_nodes），不复制存储 schema、无双真源。

### 10.3 answer 作为调用方（S3 落点）

- `ILlmClient` 新增 `ChatWithToolsAsync`（默认回退 `ChatAsync`，不破坏既有实现）；`LlmTool` 为 Function Calling 最小契约。
- `SemanticKernelLlmClient` 实现：`LlmTool` → SK `KernelFunction` + `FunctionChoiceBehavior.Auto(functions)` 工具循环。
- `StatelessAnswerExecutor` 共享聊天核心注入 `LocalDbMcpTools.GetTools()`，answer 在回答中自主读写本地节点库。
- **边界**：answer 写入**业务数据节点**（ur_nodes 数据区），不触碰确定性核心（Budget / Token / Flag / Round / 任务池裁决）；引擎执行链函数（`guard_input` / `cline_execute` / `rule_match` / `guard_output`）仍禁止暴露（见 `docs/analysis/Agent/refactor/sk-function-calling-boundary.md` §四.2 / §五）。

### 10.4 cline 调用路径

`db_*` 注册为工作流函数（同 `mcp_call_tool` 模式），cline 执行器 / FileDriven 工作流 DSL 经 `funcName=db_*` 标准化调用。

### 10.5 装配与验证

- **装配**：`AgentOptionalModules` 注册函数名；`DataStoreHosting.RegisterAsync` 数据库装配完成后注入节点端口（保证非 null）。
- **验证**：`Agent.sln` 全仓编译通过；真实数据库 `ur_nodes` 节点写→读→列→删冒烟通过；`GetTools()` 工具执行器经真实数据库读写通过。
- **备注**：实际启用 answer 工具调用需 `provider=semantic`（真实 LLM key）；`LocalLlm` 联调走 `ChatWithToolsAsync` 默认回退（纯聊天）。
