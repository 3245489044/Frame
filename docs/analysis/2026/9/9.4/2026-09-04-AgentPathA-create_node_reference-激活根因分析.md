# Agent Path A 激活缺陷 —— 根因分析（create_node_reference 折叠内建任务未落 ur_tasks）

> **状态**：分析中（已排除 2 假设，待取证 3 候选断点） | **日期**：2026-09-04
> **关联**：`2026-09-04-GuiScriptAssertion-内容断言能力-分析与框架优化.md`（内容断言框架，本次 Path A 为待修验收点）·
> `agent_mcpinvoke_create_task_test.json`（GUI 脚本 10，含 `text-contains resultTextBox tp_llm_mcp_demo` 自动断言）
> **触发**：脚本 10 复跑 Path A 显性红 —— `GetStatus tp_llm_mcp_demo -> Cancelled`，V1 读 ur_tasks 无 `tp_llm_mcp_demo`，
> 而 Path B（确定性 enqueue `tp_mcpinvoke_ctrl`）正常 Completed。

---

## §0 一句话
Path A（LocalLlm 决策 → `create_node_reference` 真实模拟调用建任务）在 GUI 脚本 10 复跑中未真正把任务
`tp_llm_mcp_demo` 写入 ur_tasks（状态读回 Cancelled 实为清理阶段留在任务池内存态的陈旧值）。经逐层排查已排除
「建后被取消」与「工具未暴露」两种假设；当前候选断点收窄到「折叠装配未走 / 工具未真正执行 / 入队未落库」三类，
需运行时取证锁定。

---

## §1 现象与验收点

### 1.1 脚本 10 观测序列
| 步骤 | 输出 | 含义 |
|---|---|---|
| [0.7] 清理 | `TaskPoolCancel tp_llm_mcp_demo -> Cancelled` → `delete: 未找到任务(无删除)` → `removeResource node` | 起点无该任务/节点 |
| [A1] conversation/send (answer) | 会话折叠跑完（guard_input/provide_prompt/cline_execute/rule_match → decision=Success） | LLM 决策执行发生 |
| [A1 后] `GetStatus tp_llm_mcp_demo` | `-> Cancelled` | 任务池内存态回 Cancelled |
| [V1] 读 ur_tasks | **无 tp_llm_mcp_demo 行** | 未落库 |
| 结尾清理 | `delete: 未找到任务 tp_llm_mcp_demo(无删除)` | 始终无该库记录 |

### 1.2 验收点
Path A 任务 `tp_llm_mcp_demo` 应：创建 → 入池（ur_tasks 落库）→ LocalLlm 消费 → **Completed**；
脚本经 4 处 `text-contains` + 1 处结果级 `expectedContains`（Path B）自动校验，`tp_llm_mcp_demo` 缺失即 fail-fast 红。
当前为红，属**待修 Path A**（非内容断言框架缺陷）。

---

## §2 链路与已定位环节

```
conversation/send(answer, taskKey=tp_mcpinvoke_conv)
  → ConversationCentralPipeline / FoldConversationRunner（agent-round 骨架）
  → cline_execute（LLM 节点）→ LocalLlm 命中 local-llm.rules.json#r-create-task（Mode=tool_call）
  → LlmResult.ToolCalls = [ create_node_reference + CreateTaskGraphRequest JSON ]
  → LlmToolExecutor.ExecuteToolCallsAsync(result, tools)
  → tools 命中 LlmTool「create_node_reference」(= CreateNodeReferenceTool, ConversationHosting)
  → TaskGraphService.CreateTaskWithRelationsAsync(req)
      ├ EnsureResourceAsync（node 写入；resources=null 时仅诊断）
      ├ EnsureReferenceAsync（引用边；resources=null 时仅诊断）
      └ ITaskGraphTaskStore.EnqueueAsync(TaskGraphTaskSpec)  // AgentTaskGraphStoreAdapter → AgentTaskPoolAdapter → TaskPool
```

### 关键装配（组合根，源码现状）
- `AgentFramework.StartAsync`（Agent.Launcher/Composition/AgentFramework.cs:93）调
  `ConversationHosting.TryAttach(a.TaskPool, executor, tablePrefix, a.ReferenceGrounding, a.DataStore?.DataStore.Nodes)`。
- `TryAttach`（ConversationHosting.cs:51）：`pool==null → return false`（无会话）。
- `TryBuildFoldOperator(pool)`（ConversationHosting.cs:195）：
  - `TaskGraphService(new AgentTaskGraphStoreAdapter(pool), resources: null)`（L208-210）—— **resources 硬编码 null**；
  - `createNodeTool = CreateNodeReferenceTool(taskGraph)`（L213）接入 **answer extraTools（L219-222）+ 折叠复合工具面（L214-215）**；
  - 确定性执行端 `TaskGraphFoldExecutor(taskGraph)`（L228-231）。

---

## §3 排查过程与已排除假设

### 3.1 假设 A：任务「建后被宿主/主循环取消」—— 已排除
- 埋点：`AgentTaskPoolAdapter.CancelAsync`（AgentTaskPool.cs）加 `TaskPool_Cancel_Diagnostic` 来源日志（含调用方方法名），
  注册到 Agent.Core `agent_log_messages.json`；构建 FileDriven.sln（0 错）后复跑脚本 10。
- 结果：全 run **0 条**取消诊断 —— `tp_llm_mcp_demo`（及任何任务）从未经该取消漏斗被取消。
- 结论：不是「入队后被取消」；状态读回 Cancelled 更可能是清理阶段 `TaskPoolCancel` 留在 `_pool` 内存态字典的**陈旧值**
  （删除仅清 persistence、未清内存态），而任务实际从未建/入队落库。

### 3.2 假设 B：`create_node_reference` 未作为 Function Calling 工具暴露 → 已排除
- 读码 `ConversationHosting.TryBuildFoldOperator`：`CreateNodeReferenceTool` 已把 create_node_reference 包装为 `LlmTool`
  并接入 answer extraTools 与折叠 composite 工具面；确定性执行端也已注册。
- 结论：工具面源码装配是齐的（若该装配被 GUI 宿主真正走到）。

### 3.3 现状：矛盾点
源码装配已接「create 工具 + pool 入队端口」，但实测 ur_tasks 无记录 → 装配链路**未按预期生效**，
断点必在上述装配的「是否走到 / 是否真执行 / 是否真落库」三处之一。

---

## §4 当前候选根因（待验断点）

| # | 候选 | 证据指向 | 判定法 |
|---|---|---|---|
| ① | GUI 宿主实际未走 `AgentFramework.StartAsync→TryAttach`，或 `a.TaskPool==null` → TryAttach 提前 return false / `TaskGraphService(pool:null)` 入队被跳过 | TaskGraphService L77-93：`_taskPool null → 跳过入队仅诊断`；TryAttach L60 pool null 早退 | 取证 TryAttach 收到 pool 是否非空 |
| ② | 会话折叠实际执行的聊天路径（executor 分支 / stepKey）工具集仍不含 `createNodeTool` → LlmToolExecutor 报 `[工具不存在: create_node_reference]` | SkConversationFoldOperator ③ in-process executor（answer/cline）分叉 | 取证 LlmToolExecutor 命中/不存在 |
| ③ | 工具执行了但入队未落库（EnqueueAsync 返回 -1 / 异常 / 池未启动消费即被覆盖） | AgentTaskGraphStoreAdapter/AgentTaskPoolAdapter.EnqueueAsync → TaskPool | 取证 TaskGraphService.enqueued 返回值 + _pool null |
| ④（注） | `GetStatus Cancelled` 仅为清理陈旧内存态，任务从未建 | §3.1 佐证 | 随 ①②③ 一并确认 |

> 备注：`TaskGraphService` 在折叠装配面 **`resources` 恒为 null**（L210），故即便任务落库成功，节点/引用边写入也会跳过（仅诊断）——
> 属已知装配缺口，需单独补（接 `IResourceReferenceStore`），不阻塞"任务落 ur_tasks"主验收。

---

## §5 建议下一步取证（锁定后最小修复）

一次性加 4 处运行诊断 → 构建 FileDriven.sln → 复跑脚本 10（约 60s）→ 依日志定断点：

| # | 埋点位置 | 观测量 |
|---|---|---|
| 1 | `ConversationHosting.TryAttach`（入参）与 `TryBuildFoldOperator(pool)`（取参） | pool / grounding / nodeStore 是否非空 |
| 2 | `CreateNodeReferenceTool` 的 `LlmTool.ExecuteAsync` 入口（ConversationHosting L253） | create 工具是否被 LocalLlm tool_call 真正调起 |
| 3 | `LlmToolExecutor.ExecuteToolCallsAsync`（命中 / `[工具不存在]`） | tool_call 是否命中 create_node_reference 工具 |
| 4 | `TaskGraphService.CreateTaskWithRelationsAsync`（_taskPool/_resources null 判定 + enqueued 值） | 入队是否真落 ur_tasks |

> 取证用日志消息建议注册到 Agent.Core（`agent_log_messages.json`），category=`Agent_Core`，level=Info。
> 依据断点：
> - 断点 1 为空 → 修 `AgentFramework.StartAsync`/GUI 宿主装配（传非空 pool）或改 `TryBuildFoldOperator` 对 pool null 显式诊断。
> - 断点 2/3 为空 → 修 answer/折叠实际聊天路径工具集（把 `createNodeTool` 并入真正执行的 executor 工具面）。
> - 断点 4 `enqueued==-1`/未调 → 修 `TaskGraphService` 任务端口注入/`AgentTaskGraphStoreAdapter`。
> 任一确认后做最小修复，再复跑脚本 10 至 Path A `tp_llm_mcp_demo` 达 Completed、脚本 `success=true`。

---

## §6 代码位置速查

| 关注点 | 文件 : 位置 |
|---|---|
| 会话折叠算子 | `Projects/Agent/src/Agent.Core/Signal/SkConversationFoldOperator.cs`（DispatchAsync ②确定性/③in-process，ChatStepAsync L191） |
| 确定性技能执行端 | `Projects/Agent/src/Agent.Core/Signal/TaskGraphFoldExecutor.cs`（SkillName=create_node_reference） |
| 确定性写图服务 | `CsharpPrj/Operation/HierarchyMethod/Nodes/TaskGraph/TaskGraphService.cs`（CreateTaskWithRelationsAsync L55，resources/pool 可空） |
| 任务池适配器 | `Projects/Agent/src/Agent.Core/TaskPool/AgentTaskPool.cs`（AgentTaskPoolAdapter；CancelAsync L363） |
| 任务入队桥 | `Projects/Agent/src/Agent.Core/TaskPool/AgentTaskGraphStoreAdapter.cs`（ITaskGraphTaskStore→AgentTaskPoolAdapter） |
| 取消/终止 | `Projects/Agent/src/Agent.Core/TaskPool/HistoryMainLoop.cs`、`Loops/TaskOrchestrateFunction.cs` |
| 工具执行器 | `Projects/Agent/src/Agent.Contract/Llm/LlmToolExecutor.cs`（ExecuteToolCallsAsync；工具不存在→`[工具不存在]`） |
| LocalLlm 决策 | `Projects/Agent/Config/rules/local-llm.rules.json#r-create-task`、`agent-config.json`（Mode=tool_call） |
| 组合根装配 | `Projects/Agent/src/Agent.Launcher/Composition/AgentFramework.cs:93`、`ConversationHosting.cs`（TryAttach L51 / TryBuildFoldOperator L195 / CreateNodeReferenceTool L247） |
| answer 执行器 | `Projects/Agent/src/Agent.Modules/Answer/StatelessAnswerExecutor.cs`（ChatAsync L238，tools=LocalDb+extra） |
| 脚本 10 | `FileDriven/DrivenConfig/Scripts/Agent/agent_mcpinvoke/agent_mcpinvoke_create_task_test.json`（Path A L33-38） |

---

## §7 备注（改动/埋点状态）
- **临时诊断埋点（未提交，供后续取证复用或清理）**：
  - `Projects/Agent/src/Agent.Core/TaskPool/AgentTaskPool.cs`：`CancelAsync` 加 `TaskPool_Cancel_Diagnostic` + `CallerMethodName()`；
  - `Projects/Agent/src/Agent.Core/Diagnostics/agent_log_messages.json`：注册 `TaskPool_Cancel_Diagnostic`。
- 已提交（上个任务，内容断言框架）：`CommandSystems.cs`（exec 结果级 expectedContains）、`UiCommandSystems.cs`（text-contains）、
  `backend_log_messages.json`、`ScriptCommand.cs`/`MetaCommandExtensions.cs`、脚本 10、`GuiScriptAssertion` 分析文档（commit `61c700ba7`）。
- Path A 修复（create_node_reference 落 ur_tasks + resources 装配 + 复跑转绿）**待 §5 取证后实施**。

